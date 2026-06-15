using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Security;
using TaskOTime.AppServer.TimeBooking;
using TaskOTime.DataLayer;
using TaskOTime.DTOs;

namespace TaskOTime.AppServer.Services
{
    public sealed class TimeBookingService : ApplicationServiceBase, ITimeBookingService
    {
        private const int DefaultRecentTemplateCount = 10;
        private const int MaximumRecentTemplateCount = 50;

        public TimeBookingService()
        {
        }

        public TimeBookingService(Func<TaskOTimeContext> contextFactory, IPasswordHasher passwordHasher)
            : base(contextFactory, passwordHasher)
        {
        }

        public ServiceResult<TimeBookingDayDto> GetBookingDay(GetBookingDayRequest request)
        {
            if (request == null)
            {
                return ServiceResult<TimeBookingDayDto>.Fail("InvalidRequest", "A booking-day request is required.");
            }

            using (var context = CreateContext())
            {
                var access = ValidateAccess(context, request.AccessContext);
                if (!access.Success)
                {
                    return ServiceResult<TimeBookingDayDto>.Fail(access.ErrorCode, access.ErrorMessage);
                }

                var options = new TimeBookingOptions
                {
                    IgnoreDeletedItems = !request.IncludeDeletedItems
                };
                var bookingDate = request.BookingDate.Date;
                var bookingDayItems = LoadBookingDayItems(
                    context,
                    request.AccessContext,
                    access.Value.IsAdmin,
                    bookingDate,
                    request.IncludeDeletedItems,
                    options);
                var changeSet = TimeBookingAlgorithm.NormalizeBookingDay(bookingDayItems, options);

                return ServiceResult<TimeBookingDayDto>.Ok(ToBookingDayDto(
                    request.AccessContext.IdTenant,
                    request.AccessContext.IdBookingUser,
                    bookingDate,
                    changeSet.TimelineItems,
                    options));
            }
        }

        public ServiceResult<TimeBookingMutationResult> AddTimeBooking(SaveTimeBookingRequest request)
        {
            if (request == null || request.Item == null)
            {
                return ServiceResult<TimeBookingMutationResult>.Fail("InvalidRequest", "A time-booking item is required.");
            }

            using (var context = CreateContext())
            using (var transaction = context.Database.BeginTransaction())
            {
                var access = ValidateAccess(context, request.AccessContext);
                if (!access.Success)
                {
                    return ServiceResult<TimeBookingMutationResult>.Fail(access.ErrorCode, access.ErrorMessage);
                }

                var validation = ValidateSaveRequest(context, request.AccessContext, access.Value.IsAdmin, request.Item, isEdit: false);
                if (!validation.Success)
                {
                    return validation;
                }

                if (request.Item.IdTimeItem != Guid.Empty &&
                    context.TimeItem.Any(item => item.IdTimeItem == request.Item.IdTimeItem))
                {
                    return ServiceResult<TimeBookingMutationResult>.Fail("TimeItemExists", "A time booking with this ID already exists.");
                }

                try
                {
                    var now = DateTimeOffset.UtcNow;
                    var options = new TimeBookingOptions();
                    var newItem = CreateTimeItem(request.Item, request.AccessContext, now, null, options);
                    var bookingDate = ResolveBookingDate(newItem);
                    var bookingDayItems = LoadBookingDayItems(
                        context,
                        request.AccessContext,
                        access.Value.IsAdmin,
                        bookingDate,
                        includeDeletedItems: false,
                        options: options);

                    var changeSet = TimeBookingAlgorithm.AddTimeItem(bookingDayItems, newItem, options);
                    PersistChangeSet(context, changeSet, now);
                    context.SaveChanges();
                    transaction.Commit();

                    return ServiceResult<TimeBookingMutationResult>.Ok(ToMutationResult(
                        request.AccessContext.IdTenant,
                        request.AccessContext.IdBookingUser,
                        bookingDate,
                        changeSet,
                        options));
                }
                catch (TimeBookingValidationException ex)
                {
                    return ServiceResult<TimeBookingMutationResult>.Fail("InvalidTimeBooking", ex.Message);
                }
            }
        }

        public ServiceResult<TimeBookingMutationResult> EditTimeBooking(SaveTimeBookingRequest request)
        {
            if (request == null || request.Item == null)
            {
                return ServiceResult<TimeBookingMutationResult>.Fail("InvalidRequest", "A time-booking item is required.");
            }

            if (request.Item.IdTimeItem == Guid.Empty)
            {
                return ServiceResult<TimeBookingMutationResult>.Fail("InvalidRequest", "The time-booking ID is required.");
            }

            using (var context = CreateContext())
            using (var transaction = context.Database.BeginTransaction())
            {
                var access = ValidateAccess(context, request.AccessContext);
                if (!access.Success)
                {
                    return ServiceResult<TimeBookingMutationResult>.Fail(access.ErrorCode, access.ErrorMessage);
                }

                var validation = ValidateSaveRequest(context, request.AccessContext, access.Value.IsAdmin, request.Item, isEdit: true);
                if (!validation.Success)
                {
                    return validation;
                }

                var existingItem = LoadAccessibleTimeItem(context, request.AccessContext, access.Value.IsAdmin, request.Item.IdTimeItem);
                if (existingItem == null)
                {
                    return ServiceResult<TimeBookingMutationResult>.Fail("TimeItemNotFound", "The time booking was not found.");
                }

                try
                {
                    var now = DateTimeOffset.UtcNow;
                    var options = new TimeBookingOptions();
                    var editedItem = CreateTimeItem(request.Item, request.AccessContext, now, existingItem, options);
                    var existingBookingDate = ResolveBookingDate(existingItem);
                    var editedBookingDate = ResolveBookingDate(editedItem);
                    if (existingBookingDate != editedBookingDate)
                    {
                        return ServiceResult<TimeBookingMutationResult>.Fail("BookingDateChangeNotSupported", "Changing the booking date of an existing time booking is not supported.");
                    }

                    var bookingDayItems = LoadBookingDayItems(
                        context,
                        request.AccessContext,
                        access.Value.IsAdmin,
                        existingBookingDate,
                        includeDeletedItems: false,
                        options: options);

                    var changeSet = TimeBookingAlgorithm.EditTimeItem(bookingDayItems, editedItem, options);
                    PersistChangeSet(context, changeSet, now);
                    context.SaveChanges();
                    transaction.Commit();

                    return ServiceResult<TimeBookingMutationResult>.Ok(ToMutationResult(
                        request.AccessContext.IdTenant,
                        request.AccessContext.IdBookingUser,
                        existingBookingDate,
                        changeSet,
                        options));
                }
                catch (TimeBookingValidationException ex)
                {
                    return ServiceResult<TimeBookingMutationResult>.Fail("InvalidTimeBooking", ex.Message);
                }
            }
        }

        public ServiceResult<TimeBookingMutationResult> DeleteTimeBooking(DeleteTimeBookingRequest request)
        {
            if (request == null)
            {
                return ServiceResult<TimeBookingMutationResult>.Fail("InvalidRequest", "A delete time-booking request is required.");
            }

            if (request.IdTimeItem == Guid.Empty)
            {
                return ServiceResult<TimeBookingMutationResult>.Fail("InvalidRequest", "The time-booking ID is required.");
            }

            using (var context = CreateContext())
            using (var transaction = context.Database.BeginTransaction())
            {
                var access = ValidateAccess(context, request.AccessContext);
                if (!access.Success)
                {
                    return ServiceResult<TimeBookingMutationResult>.Fail(access.ErrorCode, access.ErrorMessage);
                }

                try
                {
                    var now = DateTimeOffset.UtcNow;
                    var options = new TimeBookingOptions();
                    var bookingDate = request.BookingDate.Date;
                    var bookingDayItems = LoadBookingDayItems(
                        context,
                        request.AccessContext,
                        access.Value.IsAdmin,
                        bookingDate,
                        includeDeletedItems: false,
                        options: options);

                    var changeSet = TimeBookingAlgorithm.RemoveTimeItem(bookingDayItems, request.IdTimeItem, options);
                    PersistChangeSet(context, changeSet, now);
                    context.SaveChanges();
                    transaction.Commit();

                    return ServiceResult<TimeBookingMutationResult>.Ok(ToMutationResult(
                        request.AccessContext.IdTenant,
                        request.AccessContext.IdBookingUser,
                        bookingDate,
                        changeSet,
                        options));
                }
                catch (TimeBookingValidationException ex)
                {
                    return ServiceResult<TimeBookingMutationResult>.Fail("InvalidTimeBooking", ex.Message);
                }
            }
        }

        public ServiceResult<TimeBookingMutationResult> InsertWorkBreak(InsertSystemTimeMarkerRequest request)
        {
            return InsertSystemTimeMarker(request, SystemTimeMarkerKind.WorkBreak);
        }

        public ServiceResult<TimeBookingMutationResult> InsertStopMark(InsertSystemTimeMarkerRequest request)
        {
            return InsertSystemTimeMarker(request, SystemTimeMarkerKind.StopMark);
        }

        public ServiceResult<IReadOnlyList<TimeBookingTemplateDto>> GetRecentTimeTemplates(RecentTimeTemplatesRequest request)
        {
            if (request == null)
            {
                return ServiceResult<IReadOnlyList<TimeBookingTemplateDto>>.Fail("InvalidRequest", "A recent-template request is required.");
            }

            using (var context = CreateContext())
            {
                var access = ValidateAccess(context, request.AccessContext);
                if (!access.Success)
                {
                    return ServiceResult<IReadOnlyList<TimeBookingTemplateDto>>.Fail(access.ErrorCode, access.ErrorMessage);
                }

                var maximumTemplateCount = request.MaximumTemplateCount <= 0
                    ? DefaultRecentTemplateCount
                    : Math.Min(request.MaximumTemplateCount, MaximumRecentTemplateCount);
                var options = new TimeBookingOptions();
                var rows = (from item in context.TimeItem
                            join project in context.Project on item.IdProject equals project.IdProject
                            join categoryItem in context.Category on item.IdCategory equals categoryItem.IdCategory into categoryItems
                            from category in categoryItems.DefaultIfEmpty()
                            join taskItem in context.TaskItem on item.IdTask equals taskItem.IdTaskItem into taskItems
                            from task in taskItems.DefaultIfEmpty()
                            where item.IdUser == request.AccessContext.IdBookingUser &&
                                  item.EventTypeInfo == options.TimeEventType &&
                                  !item.IsItemDeleted &&
                                  item.EventTime.HasValue &&
                                  item.IdCategory != options.WorkBreakCategoryId &&
                                  item.IdCategory != options.StopMarkCategoryId &&
                                  project.IsActive &&
                                  !project.IsDeleted &&
                                  (!request.AccessContext.RejectCrossTenantProjects || project.IdTenant == request.AccessContext.IdTenant) &&
                                  (!request.Since.HasValue || item.EventTime.Value >= request.Since.Value)
                            select new
                            {
                                Item = item,
                                Project = project,
                                Category = category,
                                Task = task
                            })
                    .ToList()
                    .Where(row => access.Value.IsAdmin || HasBookableAssignment(
                        context,
                        request.AccessContext,
                        row.Item.IdProject))
                    .GroupBy(row => new
                    {
                        row.Item.IdProject,
                        row.Project.ProjectName,
                        row.Item.IdCategory,
                        CategoryName = row.Category == null ? null : row.Category.CategoryName,
                        row.Item.IdTask,
                        TaskItemName = row.Task == null ? null : row.Task.TaskItemName,
                        row.Item.ShortTitle,
                        row.Item.Description
                    })
                    .Select(group => new TimeBookingTemplateDto
                    {
                        IdTenant = request.AccessContext.IdTenant,
                        IdUser = request.AccessContext.IdBookingUser,
                        IdProject = group.Key.IdProject,
                        ProjectName = group.Key.ProjectName,
                        IdCategory = group.Key.IdCategory,
                        CategoryName = group.Key.CategoryName,
                        IdTask = group.Key.IdTask,
                        TaskItemName = group.Key.TaskItemName,
                        ShortTitle = group.Key.ShortTitle,
                        Description = group.Key.Description,
                        LastUsedAt = group.Max(row => row.Item.EventTime.Value),
                        UsageCount = group.Count()
                    })
                    .OrderByDescending(template => template.LastUsedAt)
                    .ThenBy(template => template.ProjectName)
                    .ThenBy(template => template.ShortTitle)
                    .Take(maximumTemplateCount)
                    .ToList();

                return ServiceResult<IReadOnlyList<TimeBookingTemplateDto>>.Ok(rows);
            }
        }

        private ServiceResult<TimeBookingMutationResult> InsertSystemTimeMarker(
            InsertSystemTimeMarkerRequest request,
            SystemTimeMarkerKind markerKind)
        {
            if (request == null)
            {
                return ServiceResult<TimeBookingMutationResult>.Fail("InvalidRequest", "A system time-marker request is required.");
            }

            using (var context = CreateContext())
            using (var transaction = context.Database.BeginTransaction())
            {
                var access = ValidateAccess(context, request.AccessContext);
                if (!access.Success)
                {
                    return ServiceResult<TimeBookingMutationResult>.Fail(access.ErrorCode, access.ErrorMessage);
                }

                try
                {
                    var now = DateTimeOffset.UtcNow;
                    var options = new TimeBookingOptions();
                    var userTimeItems = LoadUserTimeItems(context, request.AccessContext, access.Value.IsAdmin, includeDeletedItems: false, options: options);
                    TimeBookingChangeSet changeSet;
                    DateTime bookingDate;

                    if (markerKind == SystemTimeMarkerKind.WorkBreak)
                    {
                        changeSet = TimeBookingAlgorithm.InsertWorkBreak(
                            userTimeItems,
                            request.AccessContext.IdBookingUser,
                            request.MarkerTime,
                            options);
                        bookingDate = ResolveBookingDate(changeSet.AffectedItem);
                    }
                    else
                    {
                        changeSet = InsertStopMark(userTimeItems, request.AccessContext.IdBookingUser, request.MarkerTime, options);
                        bookingDate = ResolveBookingDate(changeSet.AffectedItem);
                    }

                    var projectValidation = ValidateProjectAccess(
                        context,
                        request.AccessContext,
                        access.Value.IsAdmin,
                        changeSet.AffectedItem.IdProject);
                    if (!projectValidation.Success)
                    {
                        return ServiceResult<TimeBookingMutationResult>.Fail(projectValidation.ErrorCode, projectValidation.ErrorMessage);
                    }

                    PersistChangeSet(context, changeSet, now);
                    context.SaveChanges();
                    transaction.Commit();

                    return ServiceResult<TimeBookingMutationResult>.Ok(ToMutationResult(
                        request.AccessContext.IdTenant,
                        request.AccessContext.IdBookingUser,
                        bookingDate,
                        changeSet,
                        options));
                }
                catch (TimeBookingValidationException ex)
                {
                    return ServiceResult<TimeBookingMutationResult>.Fail("InvalidTimeBooking", ex.Message);
                }
            }
        }

        private static TimeBookingChangeSet InsertStopMark(
            IList<TimeItem> userTimeItems,
            Guid idUser,
            DateTimeOffset stopTime,
            TimeBookingOptions options)
        {
            var timelineItems = userTimeItems
                .Where(item => item.IdUser == idUser &&
                               item.EventTypeInfo == options.TimeEventType &&
                               !item.IsItemDeleted &&
                               item.EventTime.HasValue)
                .OrderByDescending(item => item.EventTime.Value)
                .ToList();
            if (timelineItems.Count == 0)
            {
                throw new TimeBookingValidationException("A booking series cannot start with a work break or stop mark.");
            }

            var latestItem = timelineItems[0];
            if (latestItem.EventTime.Value == stopTime)
            {
                throw new TimeBookingValidationException("The same point in time cannot be recorded twice for one user.");
            }

            if (latestItem.EventTime.Value > stopTime)
            {
                throw new TimeBookingValidationException("A stop mark must be inserted after the latest booking.");
            }

            if (options.GetMarkerKind(latestItem) == SystemTimeMarkerKind.StopMark)
            {
                throw new TimeBookingValidationException("The booking series is already stopped.");
            }

            var bookingDate = ResolveMarkerBookingDate(latestItem, stopTime, options);
            var stopItem = new TimeItem
            {
                IdTimeItem = Guid.NewGuid(),
                IdUser = idUser,
                IdProject = latestItem.IdProject,
                IdCategory = options.StopMarkCategoryId,
                EventTime = stopTime,
                BookingDate = bookingDate,
                EventTypeInfo = options.TimeEventType,
                DateCreated = DateTimeOffset.UtcNow,
                DateModified = DateTimeOffset.UtcNow,
                SyncId = Guid.NewGuid()
            };

            userTimeItems.Add(stopItem);
            var bookingDayItems = userTimeItems
                .Where(item => item.IdUser == idUser &&
                               item.BookingDate.HasValue &&
                               item.BookingDate.Value.Date == bookingDate.Date)
                .ToList();
            var result = TimeBookingAlgorithm.NormalizeBookingDay(bookingDayItems, options);
            return new TimeBookingChangeSet(result.TimelineItems, result.RemovedItems, stopItem);
        }

        private static DateTime ResolveMarkerBookingDate(TimeItem latestItem, DateTimeOffset markerTime, TimeBookingOptions options)
        {
            var bookingDate = markerTime.Date;
            if (latestItem.BookingDate.HasValue &&
                latestItem.BookingDate.Value.Date < bookingDate &&
                latestItem.EventTime.HasValue)
            {
                if (markerTime - latestItem.EventTime.Value > options.BookingDateThreshold)
                {
                    throw new TimeBookingValidationException("A booking series cannot start with a work break or stop mark.");
                }

                bookingDate = bookingDate.AddDays(-1);
            }

            return bookingDate;
        }

        private ServiceResult<AccessValidation> ValidateAccess(TaskOTimeContext context, TimeBookingAccessContextDto accessContext)
        {
            if (accessContext == null)
            {
                return ServiceResult<AccessValidation>.Fail("InvalidRequest", "A time-booking access context is required.");
            }

            if (accessContext.IdTenant == Guid.Empty ||
                accessContext.IdActingUser == Guid.Empty ||
                accessContext.IdBookingUser == Guid.Empty)
            {
                return ServiceResult<AccessValidation>.Fail("InvalidRequest", "The tenant, acting user, and booking user IDs are required.");
            }

            var actingUser = context.User.SingleOrDefault(user =>
                user.IdTenant == accessContext.IdTenant &&
                user.IdUser == accessContext.IdActingUser &&
                user.IsActive &&
                !user.IsDeleted);
            if (actingUser == null)
            {
                return ServiceResult<AccessValidation>.Fail("ActingUserNotFound", "The active acting user was not found.");
            }

            var bookingUser = accessContext.IdActingUser == accessContext.IdBookingUser
                ? actingUser
                : context.User.SingleOrDefault(user =>
                    user.IdTenant == accessContext.IdTenant &&
                    user.IdUser == accessContext.IdBookingUser &&
                    user.IsActive &&
                    !user.IsDeleted);
            if (bookingUser == null)
            {
                return ServiceResult<AccessValidation>.Fail("BookingUserNotFound", "The active booking user was not found.");
            }

            if (!actingUser.IsAdmin && actingUser.IdUser != bookingUser.IdUser)
            {
                return ServiceResult<AccessValidation>.Fail("Forbidden", "Only tenant administrators can book or query time for another user.");
            }

            return ServiceResult<AccessValidation>.Ok(new AccessValidation
            {
                ActingUser = actingUser,
                BookingUser = bookingUser,
                IsAdmin = actingUser.IsAdmin
            });
        }

        private ServiceResult<TimeBookingMutationResult> ValidateSaveRequest(
            TaskOTimeContext context,
            TimeBookingAccessContextDto accessContext,
            bool isAdmin,
            TimeBookingItemDto item,
            bool isEdit)
        {
            if (item.IdTenant != Guid.Empty && item.IdTenant != accessContext.IdTenant)
            {
                return ServiceResult<TimeBookingMutationResult>.Fail("InvalidRequest", "The time-booking tenant does not match the access context.");
            }

            if (item.IdUser != Guid.Empty && item.IdUser != accessContext.IdBookingUser)
            {
                return ServiceResult<TimeBookingMutationResult>.Fail("InvalidRequest", "The time-booking user does not match the access context.");
            }

            if (item.IdProject == Guid.Empty)
            {
                return ServiceResult<TimeBookingMutationResult>.Fail("InvalidRequest", "The time-booking project is required.");
            }

            if (item.IdCategory == Guid.Empty)
            {
                return ServiceResult<TimeBookingMutationResult>.Fail("InvalidRequest", "The time-booking category is required.");
            }

            if (!item.EventTime.HasValue)
            {
                return ServiceResult<TimeBookingMutationResult>.Fail("InvalidRequest", "The time-booking event time is required.");
            }

            if (item.EventTypeInfo != default(int) &&
                item.EventTypeInfo != (int)TimeBookingEventType.Time)
            {
                return ServiceResult<TimeBookingMutationResult>.Fail("InvalidRequest", "Only time-booking items can be saved through this service.");
            }

            if (isEdit && item.IdTimeItem == Guid.Empty)
            {
                return ServiceResult<TimeBookingMutationResult>.Fail("InvalidRequest", "The time-booking ID is required.");
            }

            var projectValidation = ValidateProjectAccess(context, accessContext, isAdmin, item.IdProject);
            if (!projectValidation.Success)
            {
                return ServiceResult<TimeBookingMutationResult>.Fail(projectValidation.ErrorCode, projectValidation.ErrorMessage);
            }

            return ServiceResult<TimeBookingMutationResult>.Ok(null);
        }

        private static ServiceResult ValidateProjectAccess(
            TaskOTimeContext context,
            TimeBookingAccessContextDto accessContext,
            bool isAdmin,
            Guid idProject)
        {
            var project = context.Project.SingleOrDefault(item => item.IdProject == idProject);
            if (project == null ||
                !project.IsActive ||
                project.IsDeleted ||
                (accessContext.RejectCrossTenantProjects && project.IdTenant != accessContext.IdTenant))
            {
                return ServiceResult.Fail("ProjectNotFound", "The active tenant project was not found.");
            }

            if (!isAdmin && !HasBookableAssignment(context, accessContext, idProject))
            {
                return ServiceResult.Fail("ProjectAssignmentMissing", "The booking user is not assigned to the project with time-booking permission.");
            }

            return ServiceResult.Ok();
        }

        private static bool HasBookableAssignment(
            TaskOTimeContext context,
            TimeBookingAccessContextDto accessContext,
            Guid idProject)
        {
            if (!accessContext.RequireProjectAssignment && !accessContext.RequireBookingPermission)
            {
                return true;
            }

            return context.ProjectUserAssignment.Any(assignment =>
                assignment.IdProject == idProject &&
                assignment.IdUser == accessContext.IdBookingUser &&
                assignment.IsActive &&
                !assignment.IsDeleted &&
                (!accessContext.RequireBookingPermission || assignment.CanBookTime));
        }

        private static TimeItem LoadAccessibleTimeItem(
            TaskOTimeContext context,
            TimeBookingAccessContextDto accessContext,
            bool isAdmin,
            Guid idTimeItem)
        {
            var query = from item in context.TimeItem
                        join project in context.Project on item.IdProject equals project.IdProject
                        where item.IdTimeItem == idTimeItem &&
                              item.IdUser == accessContext.IdBookingUser &&
                              item.EventTypeInfo == (int)TimeBookingEventType.Time &&
                              !item.IsItemDeleted &&
                              project.IsActive &&
                              !project.IsDeleted &&
                              (!accessContext.RejectCrossTenantProjects || project.IdTenant == accessContext.IdTenant)
                        select item;

            var timeItem = query.SingleOrDefault();
            if (timeItem == null)
            {
                return null;
            }

            return isAdmin || HasBookableAssignment(context, accessContext, timeItem.IdProject)
                ? timeItem
                : null;
        }

        private static List<TimeItem> LoadBookingDayItems(
            TaskOTimeContext context,
            TimeBookingAccessContextDto accessContext,
            bool isAdmin,
            DateTime bookingDate,
            bool includeDeletedItems,
            TimeBookingOptions options)
        {
            var query = from item in context.TimeItem
                        join project in context.Project on item.IdProject equals project.IdProject
                        where item.IdUser == accessContext.IdBookingUser &&
                              item.EventTypeInfo == options.TimeEventType &&
                              item.BookingDate.HasValue &&
                              DbFunctions.TruncateTime(item.BookingDate.Value) == bookingDate.Date &&
                              (includeDeletedItems || !item.IsItemDeleted) &&
                              project.IsActive &&
                              !project.IsDeleted &&
                              (!accessContext.RejectCrossTenantProjects || project.IdTenant == accessContext.IdTenant)
                        orderby item.EventTime, item.IdTimeItem
                        select item;

            var items = query.ToList();
            return isAdmin
                ? items
                : items.Where(item => HasBookableAssignment(context, accessContext, item.IdProject)).ToList();
        }

        private static List<TimeItem> LoadUserTimeItems(
            TaskOTimeContext context,
            TimeBookingAccessContextDto accessContext,
            bool isAdmin,
            bool includeDeletedItems,
            TimeBookingOptions options)
        {
            var query = from item in context.TimeItem
                        join project in context.Project on item.IdProject equals project.IdProject
                        where item.IdUser == accessContext.IdBookingUser &&
                              item.EventTypeInfo == options.TimeEventType &&
                              (includeDeletedItems || !item.IsItemDeleted) &&
                              project.IsActive &&
                              !project.IsDeleted &&
                              (!accessContext.RejectCrossTenantProjects || project.IdTenant == accessContext.IdTenant)
                        orderby item.EventTime, item.IdTimeItem
                        select item;

            var items = query.ToList();
            return isAdmin
                ? items
                : items.Where(item => HasBookableAssignment(context, accessContext, item.IdProject)).ToList();
        }

        private static TimeItem CreateTimeItem(
            TimeBookingItemDto dto,
            TimeBookingAccessContextDto accessContext,
            DateTimeOffset now,
            TimeItem existingItem,
            TimeBookingOptions options)
        {
            return new TimeItem
            {
                IdTimeItem = dto.IdTimeItem,
                IdUser = accessContext.IdBookingUser,
                IdProject = dto.IdProject,
                IdTask = dto.IdTask,
                IdCategory = dto.IdCategory,
                ShortTitle = string.IsNullOrWhiteSpace(dto.ShortTitle) ? null : dto.ShortTitle.Trim(),
                Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
                EventTime = dto.EventTime,
                BookingDate = dto.BookingDate ?? (dto.EventTime.HasValue ? dto.EventTime.Value.Date : (DateTime?)null),
                EventInfo = dto.EventInfo,
                EventTypeInfo = dto.EventTypeInfo == default(int) ? options.TimeEventType : dto.EventTypeInfo,
                IdParentItem = dto.IdParentItem,
                IsItemCompleted = dto.IsItemCompleted,
                IsItemDeleted = dto.IsItemDeleted,
                IsStartAction = dto.IsStartAction,
                IsEndAction = dto.IsEndAction,
                Value = dto.Value,
                Priority = dto.Priority,
                DateCreated = existingItem == null ? now : existingItem.DateCreated,
                DateModified = now,
                SyncId = existingItem == null || existingItem.SyncId == Guid.Empty ? Guid.NewGuid() : existingItem.SyncId,
                SyncStatus = existingItem == null ? 0 : existingItem.SyncStatus,
                ExternalId = dto.ExternalId
            };
        }

        private static void PersistChangeSet(TaskOTimeContext context, TimeBookingChangeSet changeSet, DateTimeOffset now)
        {
            foreach (var item in changeSet.TimelineItems)
            {
                item.DateModified = now;
                if (item.DateCreated == default(DateTimeOffset))
                {
                    item.DateCreated = now;
                }

                if (item.SyncId == Guid.Empty)
                {
                    item.SyncId = Guid.NewGuid();
                }

                if (context.Entry(item).State == EntityState.Detached)
                {
                    context.TimeItem.Add(item);
                }
            }

            foreach (var removedItem in changeSet.RemovedItems.Distinct(new TimeItemIdComparer()))
            {
                if (context.Entry(removedItem).State == EntityState.Detached)
                {
                    context.TimeItem.Attach(removedItem);
                }

                context.TimeItem.Remove(removedItem);
            }
        }

        private static TimeBookingMutationResult ToMutationResult(
            Guid idTenant,
            Guid idUser,
            DateTime bookingDate,
            TimeBookingChangeSet changeSet,
            TimeBookingOptions options)
        {
            return new TimeBookingMutationResult
            {
                BookingDay = ToBookingDayDto(idTenant, idUser, bookingDate, changeSet.TimelineItems, options),
                AffectedItem = changeSet.AffectedItem == null ? null : ToTimeBookingItemDto(idTenant, changeSet.AffectedItem, options),
                RemovedItems = changeSet.RemovedItems
                    .Select(item => ToTimeBookingItemDto(idTenant, item, options))
                    .ToList()
            };
        }

        private static TimeBookingDayDto ToBookingDayDto(
            Guid idTenant,
            Guid idUser,
            DateTime bookingDate,
            IEnumerable<TimeItem> items,
            TimeBookingOptions options)
        {
            var timelineItems = items
                .OrderBy(item => item.EventTime)
                .ThenBy(item => item.IdTimeItem)
                .ToList();
            var totalBookedTime = TimeSpan.Zero;
            var workBreakTime = TimeSpan.Zero;

            foreach (var item in timelineItems)
            {
                var duration = item.DurationToNext ?? TimeSpan.Zero;
                if (duration <= TimeSpan.Zero)
                {
                    continue;
                }

                if (options.GetMarkerKind(item) == SystemTimeMarkerKind.WorkBreak)
                {
                    workBreakTime = workBreakTime.Add(duration);
                }
                else
                {
                    totalBookedTime = totalBookedTime.Add(duration);
                }
            }

            return new TimeBookingDayDto
            {
                IdTenant = idTenant,
                IdUser = idUser,
                BookingDate = bookingDate.Date,
                Items = timelineItems
                    .Select(item => ToTimeBookingItemDto(idTenant, item, options))
                    .ToList(),
                TotalBookedTime = totalBookedTime,
                WorkBreakTime = workBreakTime,
                FirstBookingAt = timelineItems.Select(item => item.EventTime).FirstOrDefault(time => time.HasValue),
                LastBookingAt = timelineItems.Select(item => item.EventTime).LastOrDefault(time => time.HasValue)
            };
        }

        private static TimeBookingItemDto ToTimeBookingItemDto(Guid idTenant, TimeItem item, TimeBookingOptions options)
        {
            return new TimeBookingItemDto
            {
                IdTimeItem = item.IdTimeItem,
                IdTenant = idTenant,
                IdUser = item.IdUser,
                IdProject = item.IdProject,
                IdTask = item.IdTask,
                IdCategory = item.IdCategory,
                ShortTitle = item.ShortTitle,
                Description = item.Description,
                EventTime = item.EventTime,
                BookingDate = item.BookingDate,
                EventInfo = item.EventInfo,
                EventTypeInfo = item.EventTypeInfo,
                MarkerKind = options.GetMarkerKind(item),
                DurationToNext = item.DurationToNext,
                DurationToPrevious = item.DurationToPrevious,
                IdParentItem = item.IdParentItem,
                IsItemCompleted = item.IsItemCompleted,
                IsItemDeleted = item.IsItemDeleted,
                IsStartAction = item.IsStartAction,
                IsEndAction = item.IsEndAction,
                Value = item.Value,
                Priority = item.Priority,
                DateCreated = item.DateCreated,
                DateModified = item.DateModified,
                ExternalId = item.ExternalId
            };
        }

        private static DateTime ResolveBookingDate(TimeItem item)
        {
            if (item.BookingDate.HasValue)
            {
                return item.BookingDate.Value.Date;
            }

            if (item.EventTime.HasValue)
            {
                return item.EventTime.Value.Date;
            }

            throw new TimeBookingValidationException("A time booking must have a booking date or event time.");
        }

        private sealed class AccessValidation
        {
            public User ActingUser { get; set; }

            public User BookingUser { get; set; }

            public bool IsAdmin { get; set; }
        }

        private sealed class TimeItemIdComparer : IEqualityComparer<TimeItem>
        {
            public bool Equals(TimeItem x, TimeItem y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x == null || y == null)
                {
                    return false;
                }

                return x.IdTimeItem == y.IdTimeItem;
            }

            public int GetHashCode(TimeItem obj)
            {
                return obj == null ? 0 : obj.IdTimeItem.GetHashCode();
            }
        }
    }
}
