using System;
using System.Collections.Generic;
using System.Linq;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Security;
using TaskOTime.AppServer.TimeBooking;
using TaskOTime.DataLayer;
using TaskOTime.DTOs;

namespace TaskOTime.AppServer.Services
{
    public sealed class AnalysisService : ApplicationServiceBase, IAnalysisService
    {
        public AnalysisService()
        {
        }

        public AnalysisService(Func<TaskOTimeContext> contextFactory, IPasswordHasher passwordHasher)
            : base(contextFactory, passwordHasher)
        {
        }

        public ServiceResult<ProjectHoursSummaryDto> GetCurrentUserDayProjectHours(UserAnalysisRequest request)
        {
            return GetCurrentUserProjectHours(request, AnalysisPeriod.Day);
        }

        public ServiceResult<ProjectHoursSummaryDto> GetCurrentUserWeekProjectHours(UserAnalysisRequest request)
        {
            return GetCurrentUserProjectHours(request, AnalysisPeriod.Week);
        }

        public ServiceResult<ProjectHoursSummaryDto> GetCurrentUserMonthProjectHours(UserAnalysisRequest request)
        {
            return GetCurrentUserProjectHours(request, AnalysisPeriod.Month);
        }

        public ServiceResult<ProjectHoursSummaryDto> GetProjectDayTotals(ProjectAnalysisRequest request)
        {
            return GetProjectTotals(request, AnalysisPeriod.Day);
        }

        public ServiceResult<ProjectHoursSummaryDto> GetProjectWeekTotals(ProjectAnalysisRequest request)
        {
            return GetProjectTotals(request, AnalysisPeriod.Week);
        }

        public ServiceResult<ProjectHoursSummaryDto> GetProjectMonthTotals(ProjectAnalysisRequest request)
        {
            return GetProjectTotals(request, AnalysisPeriod.Month);
        }

        public ServiceResult<ProjectHoursSummaryDto> GetTeamDayTotals(TeamAnalysisRequest request)
        {
            return GetTeamTotals(request, AnalysisPeriod.Day);
        }

        public ServiceResult<ProjectHoursSummaryDto> GetTeamWeekTotals(TeamAnalysisRequest request)
        {
            return GetTeamTotals(request, AnalysisPeriod.Week);
        }

        public ServiceResult<ProjectHoursSummaryDto> GetTeamMonthTotals(TeamAnalysisRequest request)
        {
            return GetTeamTotals(request, AnalysisPeriod.Month);
        }

        public ServiceResult<LastBookingDto> GetLastBooking(LastBookingRequest request)
        {
            if (request == null)
            {
                return ServiceResult<LastBookingDto>.Fail("InvalidRequest", "A last-booking request is required.");
            }

            using (var context = CreateContext())
            {
                var actingUserResult = ValidateActingUser(context, request.IdTenant, request.IdActingUser);
                if (!actingUserResult.Success)
                {
                    return ServiceResult<LastBookingDto>.Fail(actingUserResult.ErrorCode, actingUserResult.ErrorMessage);
                }

                var actingUser = actingUserResult.Value;
                if (request.IdUser.HasValue)
                {
                    var userResult = ValidateTenantUser(context, request.IdTenant, request.IdUser.Value);
                    if (!userResult.Success)
                    {
                        return ServiceResult<LastBookingDto>.Fail(userResult.ErrorCode, userResult.ErrorMessage);
                    }

                    if (!actingUser.IsAdmin && request.IdUser.Value != actingUser.IdUser)
                    {
                        return ServiceResult<LastBookingDto>.Fail("Forbidden", "Only tenant administrators can query the last booking for another user.");
                    }
                }

                if (request.IdProject.HasValue)
                {
                    var projectResult = ValidateProjectAccess(context, request.IdTenant, request.IdProject.Value, actingUser);
                    if (!projectResult.Success)
                    {
                        return ServiceResult<LastBookingDto>.Fail(projectResult.ErrorCode, projectResult.ErrorMessage);
                    }
                }

                var targetUserId = request.IdUser ?? (actingUser.IsAdmin ? (Guid?)null : actingUser.IdUser);
                var projectIds = ResolveProjectScope(context, request.IdTenant, actingUser, request.IdProject.HasValue ? new[] { request.IdProject.Value } : null);
                var item = LoadLastBooking(context, request.IdTenant, targetUserId, projectIds);
                var dto = new LastBookingDto
                {
                    IdTenant = request.IdTenant,
                    IdUser = targetUserId ?? (item == null ? Guid.Empty : item.IdUser),
                    Item = item == null ? null : ToTimeBookingItemDto(request.IdTenant, item, new TimeBookingOptions()),
                    BookedAt = item == null ? (DateTimeOffset?)null : item.EventTime
                };

                return ServiceResult<LastBookingDto>.Ok(dto);
            }
        }

        public ServiceResult<StatementDto> GetDailyStatement(StatementRequest request)
        {
            return GetStatement(request, AnalysisPeriod.Day);
        }

        public ServiceResult<StatementDto> GetWeeklyStatement(StatementRequest request)
        {
            return GetStatement(request, AnalysisPeriod.Week);
        }

        public ServiceResult<StatementDto> GetMonthlyStatement(StatementRequest request)
        {
            return GetStatement(request, AnalysisPeriod.Month);
        }

        public ServiceResult<TenantAdminStatisticsDto> GetTenantAdminStatistics(TenantAdminStatisticsRequest request)
        {
            if (request == null)
            {
                return ServiceResult<TenantAdminStatisticsDto>.Fail("InvalidRequest", "A tenant statistics request is required.");
            }

            using (var context = CreateContext())
            {
                var actingUserResult = ValidateActingUser(context, request.IdTenant, request.IdActingUser);
                if (!actingUserResult.Success)
                {
                    return ServiceResult<TenantAdminStatisticsDto>.Fail(actingUserResult.ErrorCode, actingUserResult.ErrorMessage);
                }

                if (!actingUserResult.Value.IsAdmin)
                {
                    return ServiceResult<TenantAdminStatisticsDto>.Fail("Forbidden", "Only tenant administrators can query tenant statistics.");
                }

                var dayRange = CreateRange(request.ReferenceDate, AnalysisPeriod.Day);
                var weekRange = CreateRange(request.ReferenceDate, AnalysisPeriod.Week);
                var monthRange = CreateRange(request.ReferenceDate, AnalysisPeriod.Month);
                var options = new TimeBookingOptions();
                var lastBooking = LoadLastBooking(context, request.IdTenant, null, null);

                var statistics = new TenantAdminStatisticsDto
                {
                    IdTenant = request.IdTenant,
                    CalculatedAt = DateTimeOffset.UtcNow,
                    ActiveUserCount = context.User.Count(user => user.IdTenant == request.IdTenant && user.IsActive && !user.IsDeleted),
                    DeletedUserCount = context.User.Count(user => user.IdTenant == request.IdTenant && user.IsDeleted),
                    ActiveProjectCount = context.Project.Count(project => project.IdTenant == request.IdTenant && project.IsActive && !project.IsDeleted),
                    DeletedProjectCount = context.Project.Count(project => project.IdTenant == request.IdTenant && project.IsDeleted),
                    ActiveProjectAssignmentCount = CountActiveProjectAssignments(context, request.IdTenant),
                    TimeBookingCount = CountNormalTimeBookings(context, request.IdTenant, options),
                    CurrentDayBookedHours = ToHours(LoadTimeRows(context, request.IdTenant, dayRange, null, null, null, null).Sum(row => row.DurationTicks)),
                    CurrentWeekBookedHours = ToHours(LoadTimeRows(context, request.IdTenant, weekRange, null, null, null, null).Sum(row => row.DurationTicks)),
                    CurrentMonthBookedHours = ToHours(LoadTimeRows(context, request.IdTenant, monthRange, null, null, null, null).Sum(row => row.DurationTicks)),
                    LastBookingAt = lastBooking == null ? (DateTimeOffset?)null : lastBooking.EventTime
                };

                return ServiceResult<TenantAdminStatisticsDto>.Ok(statistics);
            }
        }

        private ServiceResult<ProjectHoursSummaryDto> GetCurrentUserProjectHours(UserAnalysisRequest request, AnalysisPeriod period)
        {
            if (request == null)
            {
                return ServiceResult<ProjectHoursSummaryDto>.Fail("InvalidRequest", "A user-analysis request is required.");
            }

            using (var context = CreateContext())
            {
                var actingUserResult = ValidateActingUser(context, request.IdTenant, request.IdActingUser);
                if (!actingUserResult.Success)
                {
                    return ServiceResult<ProjectHoursSummaryDto>.Fail(actingUserResult.ErrorCode, actingUserResult.ErrorMessage);
                }

                var targetUserResult = ValidateTenantUser(context, request.IdTenant, request.IdUser);
                if (!targetUserResult.Success)
                {
                    return ServiceResult<ProjectHoursSummaryDto>.Fail(targetUserResult.ErrorCode, targetUserResult.ErrorMessage);
                }

                var actingUser = actingUserResult.Value;
                if (!actingUser.IsAdmin && actingUser.IdUser != request.IdUser)
                {
                    return ServiceResult<ProjectHoursSummaryDto>.Fail("Forbidden", "Only tenant administrators can query project hours for another user.");
                }

                var range = CreateRange(request.ReferenceDate, period);
                var projectIds = ResolveProjectScope(context, request.IdTenant, actingUser, null);
                var rows = LoadTimeRows(context, request.IdTenant, range, request.IdUser, null, projectIds, null);
                var summary = CreateProjectHoursSummary(request.IdTenant, request.IdUser, null, range, rows, includeUserBreakdown: false);

                return ServiceResult<ProjectHoursSummaryDto>.Ok(summary);
            }
        }

        private ServiceResult<ProjectHoursSummaryDto> GetProjectTotals(ProjectAnalysisRequest request, AnalysisPeriod period)
        {
            if (request == null)
            {
                return ServiceResult<ProjectHoursSummaryDto>.Fail("InvalidRequest", "A project-analysis request is required.");
            }

            using (var context = CreateContext())
            {
                var actingUserResult = ValidateActingUser(context, request.IdTenant, request.IdActingUser);
                if (!actingUserResult.Success)
                {
                    return ServiceResult<ProjectHoursSummaryDto>.Fail(actingUserResult.ErrorCode, actingUserResult.ErrorMessage);
                }

                var projectResult = ValidateProjectAccess(context, request.IdTenant, request.IdProject, actingUserResult.Value);
                if (!projectResult.Success)
                {
                    return ServiceResult<ProjectHoursSummaryDto>.Fail(projectResult.ErrorCode, projectResult.ErrorMessage);
                }

                var range = CreateRange(request.ReferenceDate, period);
                var rows = LoadTimeRows(context, request.IdTenant, range, null, request.IdProject, null, null);
                var summary = CreateProjectHoursSummary(request.IdTenant, null, request.IdProject, range, rows, request.IncludeUserBreakdown);

                return ServiceResult<ProjectHoursSummaryDto>.Ok(summary);
            }
        }

        private ServiceResult<ProjectHoursSummaryDto> GetTeamTotals(TeamAnalysisRequest request, AnalysisPeriod period)
        {
            if (request == null)
            {
                return ServiceResult<ProjectHoursSummaryDto>.Fail("InvalidRequest", "A team-analysis request is required.");
            }

            using (var context = CreateContext())
            {
                var actingUserResult = ValidateActingUser(context, request.IdTenant, request.IdActingUser);
                if (!actingUserResult.Success)
                {
                    return ServiceResult<ProjectHoursSummaryDto>.Fail(actingUserResult.ErrorCode, actingUserResult.ErrorMessage);
                }

                var actingUser = actingUserResult.Value;
                var projectFilterResult = ResolveProjectFilter(context, request.IdTenant, actingUser, request.IdProjectFilter);
                if (!projectFilterResult.Success)
                {
                    return ServiceResult<ProjectHoursSummaryDto>.Fail(projectFilterResult.ErrorCode, projectFilterResult.ErrorMessage);
                }

                var userFilterResult = ResolveUserFilter(context, request.IdTenant, request.IdUserFilter);
                if (!userFilterResult.Success)
                {
                    return ServiceResult<ProjectHoursSummaryDto>.Fail(userFilterResult.ErrorCode, userFilterResult.ErrorMessage);
                }

                var range = CreateRange(request.ReferenceDate, period);
                var rows = LoadTimeRows(context, request.IdTenant, range, null, null, projectFilterResult.Value, userFilterResult.Value);
                var summary = CreateProjectHoursSummary(request.IdTenant, null, null, range, rows, includeUserBreakdown: true);

                return ServiceResult<ProjectHoursSummaryDto>.Ok(summary);
            }
        }

        private ServiceResult<StatementDto> GetStatement(StatementRequest request, AnalysisPeriod period)
        {
            if (request == null)
            {
                return ServiceResult<StatementDto>.Fail("InvalidRequest", "A statement request is required.");
            }

            using (var context = CreateContext())
            {
                var actingUserResult = ValidateActingUser(context, request.IdTenant, request.IdActingUser);
                if (!actingUserResult.Success)
                {
                    return ServiceResult<StatementDto>.Fail(actingUserResult.ErrorCode, actingUserResult.ErrorMessage);
                }

                var actingUser = actingUserResult.Value;
                var targetUserId = request.IdUser ?? (actingUser.IsAdmin ? (Guid?)null : actingUser.IdUser);
                if (targetUserId.HasValue)
                {
                    var userResult = ValidateTenantUser(context, request.IdTenant, targetUserId.Value);
                    if (!userResult.Success)
                    {
                        return ServiceResult<StatementDto>.Fail(userResult.ErrorCode, userResult.ErrorMessage);
                    }

                    if (!actingUser.IsAdmin && targetUserId.Value != actingUser.IdUser)
                    {
                        return ServiceResult<StatementDto>.Fail("Forbidden", "Only tenant administrators can query statements for another user.");
                    }
                }

                if (request.IdProject.HasValue)
                {
                    var projectResult = ValidateProjectAccess(context, request.IdTenant, request.IdProject.Value, actingUser);
                    if (!projectResult.Success)
                    {
                        return ServiceResult<StatementDto>.Fail(projectResult.ErrorCode, projectResult.ErrorMessage);
                    }
                }

                var range = CreateRange(request.ReferenceDate, period);
                var projectIds = ResolveProjectScope(context, request.IdTenant, actingUser, request.IdProject.HasValue ? new[] { request.IdProject.Value } : null);
                var rows = LoadTimeRows(context, request.IdTenant, range, targetUserId, request.IdProject, projectIds, null);
                var statement = CreateStatement(request.IdTenant, targetUserId, request.IdProject, range, rows, request.IncludeNotes);

                return ServiceResult<StatementDto>.Ok(statement);
            }
        }

        private static ProjectHoursSummaryDto CreateProjectHoursSummary(
            Guid idTenant,
            Guid? idUser,
            Guid? idProject,
            AnalysisRange range,
            IReadOnlyList<TimeEntryRow> rows,
            bool includeUserBreakdown)
        {
            var lines = rows
                .GroupBy(row => new
                {
                    row.Project.IdProject,
                    row.Project.ProjectName,
                    row.Project.ProjectIdentifier,
                    IdUser = includeUserBreakdown ? (Guid?)row.User.IdUser : null,
                    UserDisplayName = includeUserBreakdown ? GetDisplayName(row.User) : null
                })
                .Select(group =>
                {
                    var durationTicks = group.Sum(row => row.DurationTicks);
                    return new ProjectHoursLineDto
                    {
                        IdProject = group.Key.IdProject,
                        ProjectName = group.Key.ProjectName,
                        ProjectIdentifier = group.Key.ProjectIdentifier,
                        IdUser = group.Key.IdUser,
                        UserDisplayName = group.Key.UserDisplayName,
                        BookedTime = TimeSpan.FromTicks(durationTicks),
                        BookedHours = ToHours(durationTicks),
                        BookingCount = group.Count()
                    };
                })
                .OrderBy(line => line.ProjectName)
                .ThenBy(line => line.UserDisplayName)
                .ToList();

            var totalTicks = lines.Sum(line => line.BookedTime.Ticks);
            return new ProjectHoursSummaryDto
            {
                IdTenant = idTenant,
                IdUser = idUser,
                IdProject = idProject,
                Range = ToRangeDto(range),
                TotalBookedTime = TimeSpan.FromTicks(totalTicks),
                TotalBookedHours = ToHours(totalTicks),
                Lines = lines
            };
        }

        private static StatementDto CreateStatement(
            Guid idTenant,
            Guid? idUser,
            Guid? idProject,
            AnalysisRange range,
            IReadOnlyList<TimeEntryRow> rows,
            bool includeNotes)
        {
            var lines = rows
                .OrderBy(row => row.Item.BookingDate)
                .ThenBy(row => row.Item.EventTime)
                .ThenBy(row => row.Project.ProjectName)
                .Select(row =>
                {
                    var durationTicks = row.DurationTicks;
                    return new StatementLineDto
                    {
                        BookingDate = row.Item.BookingDate.Value.Date,
                        IdProject = row.Project.IdProject,
                        ProjectName = row.Project.ProjectName,
                        IdCategory = row.Item.IdCategory,
                        CategoryName = row.Category == null ? null : row.Category.CategoryName,
                        IdTask = row.Item.IdTask,
                        TaskItemName = row.Task == null ? null : row.Task.TaskItemName,
                        Duration = TimeSpan.FromTicks(durationTicks),
                        Hours = ToHours(durationTicks),
                        Description = includeNotes ? row.Item.Description : null
                    };
                })
                .ToList();

            var totalTicks = lines.Sum(line => line.Duration.Ticks);
            return new StatementDto
            {
                IdTenant = idTenant,
                IdUser = idUser,
                IdProject = idProject,
                Range = ToRangeDto(range),
                TotalTime = TimeSpan.FromTicks(totalTicks),
                TotalHours = ToHours(totalTicks),
                Lines = lines
            };
        }

        private static List<TimeEntryRow> LoadTimeRows(
            TaskOTimeContext context,
            Guid idTenant,
            AnalysisRange range,
            Guid? idUser,
            Guid? idProject,
            IReadOnlyCollection<Guid> idProjectFilter,
            IReadOnlyCollection<Guid> idUserFilter)
        {
            var options = new TimeBookingOptions();
            var query = from item in context.TimeItem
                        join project in context.Project on item.IdProject equals project.IdProject
                        join user in context.User on item.IdUser equals user.IdUser
                        join categoryItem in context.Category on item.IdCategory equals categoryItem.IdCategory into categoryItems
                        from category in categoryItems.DefaultIfEmpty()
                        join taskItem in context.TaskItem on item.IdTask equals taskItem.IdTaskItem into taskItems
                        from task in taskItems.DefaultIfEmpty()
                        where project.IdTenant == idTenant &&
                              project.IsActive &&
                              !project.IsDeleted &&
                              user.IdTenant == idTenant &&
                              user.IsActive &&
                              !user.IsDeleted &&
                              item.EventTypeInfo == options.TimeEventType &&
                              !item.IsItemDeleted &&
                              item.BookingDate.HasValue &&
                              item.BookingDate.Value >= range.StartDate &&
                              item.BookingDate.Value < range.EndDate &&
                              item.DurationTicksToNext.HasValue &&
                              item.DurationTicksToNext.Value > 0 &&
                              item.IdCategory != options.WorkBreakCategoryId &&
                              item.IdCategory != options.StopMarkCategoryId
                        select new
                        {
                            Item = item,
                            Project = project,
                            User = user,
                            Category = category,
                            Task = task
                        };

            if (idUser.HasValue)
            {
                query = query.Where(row => row.Item.IdUser == idUser.Value);
            }

            if (idProject.HasValue)
            {
                query = query.Where(row => row.Item.IdProject == idProject.Value);
            }

            if (idProjectFilter != null)
            {
                var ids = idProjectFilter.ToList();
                query = query.Where(row => ids.Contains(row.Item.IdProject));
            }

            if (idUserFilter != null)
            {
                var ids = idUserFilter.ToList();
                query = query.Where(row => ids.Contains(row.Item.IdUser));
            }

            // Analysis DTOs expose booked totals but no dedicated work-break total, so marker rows are excluded.
            return query
                .ToList()
                .Select(row => new TimeEntryRow
                {
                    Item = row.Item,
                    Project = row.Project,
                    User = row.User,
                    Category = row.Category,
                    Task = row.Task,
                    DurationTicks = row.Item.DurationTicksToNext.Value
                })
                .ToList();
        }

        private static TimeItem LoadLastBooking(
            TaskOTimeContext context,
            Guid idTenant,
            Guid? idUser,
            IReadOnlyCollection<Guid> idProjectFilter)
        {
            var options = new TimeBookingOptions();
            var query = from item in context.TimeItem
                        join project in context.Project on item.IdProject equals project.IdProject
                        join user in context.User on item.IdUser equals user.IdUser
                        where project.IdTenant == idTenant &&
                              project.IsActive &&
                              !project.IsDeleted &&
                              user.IdTenant == idTenant &&
                              user.IsActive &&
                              !user.IsDeleted &&
                              item.EventTypeInfo == options.TimeEventType &&
                              !item.IsItemDeleted &&
                              item.EventTime.HasValue &&
                              item.IdCategory != options.WorkBreakCategoryId &&
                              item.IdCategory != options.StopMarkCategoryId
                        select item;

            if (idUser.HasValue)
            {
                query = query.Where(item => item.IdUser == idUser.Value);
            }

            if (idProjectFilter != null)
            {
                var ids = idProjectFilter.ToList();
                query = query.Where(item => ids.Contains(item.IdProject));
            }

            return query
                .OrderByDescending(item => item.EventTime)
                .ThenByDescending(item => item.DateModified)
                .FirstOrDefault();
        }

        private static int CountNormalTimeBookings(TaskOTimeContext context, Guid idTenant, TimeBookingOptions options)
        {
            return (from item in context.TimeItem
                    join project in context.Project on item.IdProject equals project.IdProject
                    join user in context.User on item.IdUser equals user.IdUser
                    where project.IdTenant == idTenant &&
                          project.IsActive &&
                          !project.IsDeleted &&
                          user.IdTenant == idTenant &&
                          user.IsActive &&
                          !user.IsDeleted &&
                          item.EventTypeInfo == options.TimeEventType &&
                          !item.IsItemDeleted &&
                          item.IdCategory != options.WorkBreakCategoryId &&
                          item.IdCategory != options.StopMarkCategoryId
                    select item).Count();
        }

        private static int CountActiveProjectAssignments(TaskOTimeContext context, Guid idTenant)
        {
            return (from assignment in context.ProjectUserAssignment
                    join project in context.Project on assignment.IdProject equals project.IdProject
                    join user in context.User on assignment.IdUser equals user.IdUser
                    where project.IdTenant == idTenant &&
                          project.IsActive &&
                          !project.IsDeleted &&
                          user.IdTenant == idTenant &&
                          user.IsActive &&
                          !user.IsDeleted &&
                          assignment.IsActive &&
                          !assignment.IsDeleted
                    select assignment).Count();
        }

        private static ServiceResult<User> ValidateActingUser(TaskOTimeContext context, Guid idTenant, Guid idActingUser)
        {
            if (idTenant == Guid.Empty || idActingUser == Guid.Empty)
            {
                return ServiceResult<User>.Fail("InvalidRequest", "The tenant and acting user IDs are required.");
            }

            var user = context.User.SingleOrDefault(item =>
                item.IdTenant == idTenant &&
                item.IdUser == idActingUser &&
                item.IsActive &&
                !item.IsDeleted);

            return user == null
                ? ServiceResult<User>.Fail("ActingUserNotFound", "The active acting user was not found.")
                : ServiceResult<User>.Ok(user);
        }

        private static ServiceResult<User> ValidateTenantUser(TaskOTimeContext context, Guid idTenant, Guid idUser)
        {
            if (idTenant == Guid.Empty || idUser == Guid.Empty)
            {
                return ServiceResult<User>.Fail("InvalidRequest", "The tenant and user IDs are required.");
            }

            var user = context.User.SingleOrDefault(item =>
                item.IdTenant == idTenant &&
                item.IdUser == idUser &&
                item.IsActive &&
                !item.IsDeleted);

            return user == null
                ? ServiceResult<User>.Fail("UserNotFound", "The active tenant user was not found.")
                : ServiceResult<User>.Ok(user);
        }

        private static ServiceResult<Project> ValidateProjectAccess(TaskOTimeContext context, Guid idTenant, Guid idProject, User actingUser)
        {
            if (idTenant == Guid.Empty || idProject == Guid.Empty)
            {
                return ServiceResult<Project>.Fail("InvalidRequest", "The tenant and project IDs are required.");
            }

            var project = context.Project.SingleOrDefault(item =>
                item.IdTenant == idTenant &&
                item.IdProject == idProject &&
                item.IsActive &&
                !item.IsDeleted);
            if (project == null)
            {
                return ServiceResult<Project>.Fail("ProjectNotFound", "The active tenant project was not found.");
            }

            if (!actingUser.IsAdmin && !HasActiveProjectAssignment(context, idProject, actingUser.IdUser))
            {
                return ServiceResult<Project>.Fail("Forbidden", "The acting user is not assigned to the project.");
            }

            return ServiceResult<Project>.Ok(project);
        }

        private static ServiceResult<IReadOnlyCollection<Guid>> ResolveProjectFilter(
            TaskOTimeContext context,
            Guid idTenant,
            User actingUser,
            IReadOnlyList<Guid> requestedProjectIds)
        {
            if (requestedProjectIds != null && requestedProjectIds.Any(id => id == Guid.Empty))
            {
                return ServiceResult<IReadOnlyCollection<Guid>>.Fail("InvalidRequest", "Project filters must not contain empty IDs.");
            }

            var requestedIds = requestedProjectIds == null
                ? new List<Guid>()
                : requestedProjectIds.Distinct().ToList();
            var visibleProjectIds = ResolveProjectScope(context, idTenant, actingUser, requestedIds.Count == 0 ? null : requestedIds);
            if (requestedIds.Count > 0 && visibleProjectIds.Count != requestedIds.Count)
            {
                return ServiceResult<IReadOnlyCollection<Guid>>.Fail(
                    actingUser.IsAdmin ? "ProjectNotFound" : "Forbidden",
                    actingUser.IsAdmin ? "One or more active tenant projects were not found." : "The acting user is not assigned to one or more requested projects.");
            }

            return ServiceResult<IReadOnlyCollection<Guid>>.Ok(visibleProjectIds);
        }

        private static ServiceResult<IReadOnlyCollection<Guid>> ResolveUserFilter(
            TaskOTimeContext context,
            Guid idTenant,
            IReadOnlyList<Guid> requestedUserIds)
        {
            if (requestedUserIds == null || requestedUserIds.Count == 0)
            {
                return ServiceResult<IReadOnlyCollection<Guid>>.Ok(null);
            }

            if (requestedUserIds.Any(id => id == Guid.Empty))
            {
                return ServiceResult<IReadOnlyCollection<Guid>>.Fail("InvalidRequest", "User filters must not contain empty IDs.");
            }

            var ids = requestedUserIds.Distinct().ToList();
            var foundUserCount = context.User.Count(user =>
                user.IdTenant == idTenant &&
                ids.Contains(user.IdUser) &&
                user.IsActive &&
                !user.IsDeleted);
            if (foundUserCount != ids.Count)
            {
                return ServiceResult<IReadOnlyCollection<Guid>>.Fail("UserNotFound", "One or more active tenant users were not found.");
            }

            return ServiceResult<IReadOnlyCollection<Guid>>.Ok(ids);
        }

        private static List<Guid> ResolveProjectScope(
            TaskOTimeContext context,
            Guid idTenant,
            User actingUser,
            IEnumerable<Guid> requestedProjectIds)
        {
            var requestedIds = requestedProjectIds == null ? null : requestedProjectIds.Distinct().ToList();
            var query = context.Project
                .Where(project => project.IdTenant == idTenant && project.IsActive && !project.IsDeleted)
                .Select(project => project.IdProject);
            if (requestedIds != null)
            {
                query = query.Where(id => requestedIds.Contains(id));
            }

            var projectIds = query.ToList();

            if (actingUser.IsAdmin)
            {
                return projectIds;
            }

            var assignedProjectIds = GetAssignedProjectIds(context, idTenant, actingUser.IdUser);
            return projectIds.Where(id => assignedProjectIds.Contains(id)).ToList();
        }

        private static List<Guid> GetAssignedProjectIds(TaskOTimeContext context, Guid idTenant, Guid idUser)
        {
            return (from assignment in context.ProjectUserAssignment
                    join project in context.Project on assignment.IdProject equals project.IdProject
                    where project.IdTenant == idTenant &&
                          project.IsActive &&
                          !project.IsDeleted &&
                          assignment.IdUser == idUser &&
                          assignment.IsActive &&
                          !assignment.IsDeleted
                    select project.IdProject)
                .Distinct()
                .ToList();
        }

        private static bool HasActiveProjectAssignment(TaskOTimeContext context, Guid idProject, Guid idUser)
        {
            return context.ProjectUserAssignment.Any(assignment =>
                assignment.IdProject == idProject &&
                assignment.IdUser == idUser &&
                assignment.IsActive &&
                !assignment.IsDeleted);
        }

        private static AnalysisRange CreateRange(DateTimeOffset referenceDate, AnalysisPeriod period)
        {
            var reference = referenceDate == default(DateTimeOffset) ? DateTimeOffset.UtcNow : referenceDate;
            DateTimeOffset start;
            DateTimeOffset end;
            string label;

            switch (period)
            {
                case AnalysisPeriod.Day:
                    start = new DateTimeOffset(reference.Year, reference.Month, reference.Day, 0, 0, 0, reference.Offset);
                    end = start.AddDays(1);
                    label = start.ToString("yyyy-MM-dd");
                    break;
                case AnalysisPeriod.Week:
                    var dayOffset = ((int)reference.DayOfWeek + 6) % 7;
                    start = new DateTimeOffset(reference.Year, reference.Month, reference.Day, 0, 0, 0, reference.Offset).AddDays(-dayOffset);
                    end = start.AddDays(7);
                    label = start.ToString("yyyy-MM-dd") + " - " + end.AddDays(-1).ToString("yyyy-MM-dd");
                    break;
                default:
                    start = new DateTimeOffset(reference.Year, reference.Month, 1, 0, 0, 0, reference.Offset);
                    end = start.AddMonths(1);
                    label = start.ToString("yyyy-MM");
                    break;
            }

            return new AnalysisRange
            {
                StartsAt = start,
                EndsAt = end,
                StartDate = start.Date,
                EndDate = end.Date,
                Label = label
            };
        }

        private static AnalysisTimeRangeDto ToRangeDto(AnalysisRange range)
        {
            return new AnalysisTimeRangeDto
            {
                StartsAt = range.StartsAt,
                EndsAt = range.EndsAt,
                Label = range.Label
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
                DurationToNext = item.DurationTicksToNext.HasValue ? TimeSpan.FromTicks(item.DurationTicksToNext.Value) : item.DurationToNext,
                DurationToPrevious = item.DurationTicksToPrevious.HasValue ? TimeSpan.FromTicks(item.DurationTicksToPrevious.Value) : item.DurationToPrevious,
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

        private static string GetDisplayName(User user)
        {
            var parts = new[] { user.FirstName, user.MiddleName, user.LastName }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part.Trim())
                .ToList();
            if (parts.Count > 0)
            {
                return string.Join(" ", parts);
            }

            return !string.IsNullOrWhiteSpace(user.UserIdent)
                ? user.UserIdent
                : (!string.IsNullOrWhiteSpace(user.EMail) ? user.EMail : user.IdUser.ToString());
        }

        private static decimal ToHours(long ticks)
        {
            return ticks <= 0 ? 0m : ticks / (decimal)TimeSpan.TicksPerHour;
        }

        private enum AnalysisPeriod
        {
            Day,
            Week,
            Month
        }

        private sealed class AnalysisRange
        {
            public DateTimeOffset StartsAt { get; set; }

            public DateTimeOffset EndsAt { get; set; }

            public DateTime StartDate { get; set; }

            public DateTime EndDate { get; set; }

            public string Label { get; set; }
        }

        private sealed class TimeEntryRow
        {
            public TimeItem Item { get; set; }

            public Project Project { get; set; }

            public User User { get; set; }

            public Category Category { get; set; }

            public TaskItem Task { get; set; }

            public long DurationTicks { get; set; }
        }
    }
}
