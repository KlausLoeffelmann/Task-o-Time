using System;
using System.Collections.Generic;
using System.Linq;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Services;
using TaskOTime.AppServer.TimeBooking;
using TaskOTime.DTOs;

namespace TaskOTime.TimeTrackingServices.Tests.Doubles
{
    public sealed class TestApplicationServices :
        IAdminMasterDataService, IUserAdministrationService, ITimeBookingService
    {
        private static readonly DateTimeOffset SeedTime =
            new DateTimeOffset(2026, 6, 15, 8, 0, 0, TimeSpan.Zero);
        private readonly List<TenantUserDto> users = new List<TenantUserDto>();
        private readonly List<ProjectMainDataDto> projects = new List<ProjectMainDataDto>();
        private readonly List<TaskListMasterDataDto> taskLists = new List<TaskListMasterDataDto>();
        private readonly List<TaskItemMasterDataDto> tasks = new List<TaskItemMasterDataDto>();
        private readonly List<CategoryMasterDataDto> categories = new List<CategoryMasterDataDto>();
        private readonly List<TagMasterDataDto> tags = new List<TagMasterDataDto>();
        private readonly List<NoteMasterDataDto> notes = new List<NoteMasterDataDto>();
        private readonly List<WebLinkMasterDataDto> webLinks = new List<WebLinkMasterDataDto>();
        private readonly List<TimeBookingItemDto> timeItems = new List<TimeBookingItemDto>();

        public TestApplicationServices()
        {
            Tenant = new TenantDto
            {
                IdTenant = Id(1), TenantName = "Contoso Nord", TenantIdentifier = "CON-N",
                Description = "Demomandant für die Desktop-Verwaltung", IsActive = true,
                DateCreated = SeedTime, DateModified = SeedTime
            };
            ActingUserId = Id(2);
            ProjectId = Id(3);
            CategoryId = Id(4);
            users.Add(new TenantUserDto
            {
                IdUser = ActingUserId, IdTenant = Tenant.IdTenant, UserIdent = "a.adler",
                FirstName = "Anne", LastName = "Adler", EMail = "anne@example.invalid",
                IsActive = true, IsAdmin = true, LastLogin = SeedTime,
                DateCreated = SeedTime, DateModified = SeedTime
            });
            projects.Add(new ProjectMainDataDto
            {
                IdProject = ProjectId, IdTenant = Tenant.IdTenant, IdUser = ActingUserId,
                ProjectName = "Zeiterfassung 2026", ProjectIdentifier = "ZT-2601",
                ProjectDescription = "Ausbau der Buchungs- und Aufgabenoberfläche.",
                IsActive = true, DateCreated = SeedTime, DateModified = SeedTime
            });
            projects.Add(new ProjectMainDataDto
            {
                IdProject = Id(15), IdTenant = Tenant.IdTenant, IdUser = ActingUserId,
                ProjectName = "Kundenportal", ProjectIdentifier = "KP-2602",
                ProjectDescription = "Arbeiten am Kundenportal.", IsActive = true,
                DateCreated = SeedTime, DateModified = SeedTime
            });
            var listId = Id(5);
            taskLists.Add(new TaskListMasterDataDto
            {
                IdTaskList = listId, IdTenant = Tenant.IdTenant, IdProject = ProjectId,
                IdUser = ActingUserId, TaskListName = "Sprint 4",
                TaskListDescription = "Aktuelle Arbeiten", DisplayOrder = 1
            });
            tasks.Add(new TaskItemMasterDataDto
            {
                IdTaskItem = Id(6), IdTenant = Tenant.IdTenant, IdProject = ProjectId,
                IdUser = ActingUserId, IdTaskList = listId,
                TaskItemName = "Buchungsansicht prüfen",
                TaskItemDescription = "Auswahl, Bearbeitung und Reihenfolge testen.",
                Priority = 2, DueDate = SeedTime.AddDays(2), IsActive = true,
                DateCreated = SeedTime, DateModified = SeedTime
            });
            categories.Add(new CategoryMasterDataDto
            {
                IdCategory = CategoryId, IdTenant = Tenant.IdTenant, IdUser = ActingUserId,
                CategoryName = "Entwicklung", CategoryDescription = "Produktentwicklung", DisplayOrder = 1
            });
            tags.Add(new TagMasterDataDto
            {
                IdTag = Id(7), IdTenant = Tenant.IdTenant, IdUser = ActingUserId,
                Tag = "wichtig", Description = "Hohe Aufmerksamkeit",
                DateCreated = SeedTime, DateModified = SeedTime
            });
            notes.Add(new NoteMasterDataDto
            {
                IdNote = Id(8), IdTenant = Tenant.IdTenant, IdUser = ActingUserId,
                IdProject = ProjectId, NoteMnemonic = "S4",
                NoteText = "Rücksprache mit dem Projektteam.",
                DateCreated = SeedTime, DateModified = SeedTime
            });
            webLinks.Add(new WebLinkMasterDataDto
            {
                IdWebLink = Id(9), IdTenant = Tenant.IdTenant, IdUser = ActingUserId,
                IdProject = ProjectId, Title = "Projekt-Wiki",
                Link = "https://example.invalid/wiki", Domain = "example.invalid",
                DateCreated = SeedTime, DateModified = SeedTime
            });
            AddSeedTime(10, 8, 30, "Tagesplanung", SystemTimeMarkerKind.Normal);
            AddSeedTime(11, 15, 0, "Stopp", SystemTimeMarkerKind.StopMark);
            AddSeedTime(12, 12, 0, "Mittagspause", SystemTimeMarkerKind.WorkBreak);
            AddSeedTime(13, 9, 0, "Projektarbeit", SystemTimeMarkerKind.Normal);
            AddSeedTime(14, 12, 30, "Projektarbeit", SystemTimeMarkerKind.Normal);
        }

        public TenantDto Tenant { get; }
        public Guid ActingUserId { get; }
        public Guid ProjectId { get; }
        public Guid CategoryId { get; }

        public ServiceResult<IReadOnlyList<ProjectMainDataDto>> GetProjects(MasterDataQueryRequest request) =>
            Query(request, projects);
        public ServiceResult<ProjectMainDataDto> GetProject(MasterDataItemRequest request) =>
            Find(request, projects, x => x.IdProject, "ProjectNotFound");
        public ServiceResult<ProjectMainDataDto> CreateProject(SaveProjectRequest request) =>
            Create(request, projects, x => x.IdProject, (x, id) => x.IdProject = id);
        public ServiceResult<ProjectMainDataDto> UpdateProject(SaveProjectRequest request) =>
            Update(request, projects, x => x.IdProject);
        public ServiceResult<MasterDataDeleteResult> DeleteProject(DeleteMasterDataRequest request) =>
            Delete(request, projects, x => x.IdProject, "Project");

        public ServiceResult<IReadOnlyList<CategoryMasterDataDto>> GetCategories(MasterDataQueryRequest request) =>
            Query(request, categories);
        public ServiceResult<CategoryMasterDataDto> GetCategory(MasterDataItemRequest request) =>
            Find(request, categories, x => x.IdCategory, "CategoryNotFound");
        public ServiceResult<CategoryMasterDataDto> CreateCategory(SaveCategoryRequest request) =>
            Create(request, categories, x => x.IdCategory, (x, id) => x.IdCategory = id);
        public ServiceResult<CategoryMasterDataDto> UpdateCategory(SaveCategoryRequest request) =>
            Update(request, categories, x => x.IdCategory);
        public ServiceResult<MasterDataDeleteResult> DeleteCategory(DeleteMasterDataRequest request) =>
            Delete(request, categories, x => x.IdCategory, "Category");

        public ServiceResult<IReadOnlyList<CategorySymbolMasterDataDto>> GetCategorySymbols(MasterDataQueryRequest request) =>
            ServiceResult<IReadOnlyList<CategorySymbolMasterDataDto>>.Ok(new List<CategorySymbolMasterDataDto>());
        public ServiceResult<CategorySymbolMasterDataDto> GetCategorySymbol(MasterDataItemRequest request) => Unsupported<CategorySymbolMasterDataDto>();
        public ServiceResult<CategorySymbolMasterDataDto> CreateCategorySymbol(SaveCategorySymbolRequest request) => Unsupported<CategorySymbolMasterDataDto>();
        public ServiceResult<CategorySymbolMasterDataDto> UpdateCategorySymbol(SaveCategorySymbolRequest request) => Unsupported<CategorySymbolMasterDataDto>();
        public ServiceResult<MasterDataDeleteResult> DeleteCategorySymbol(DeleteMasterDataRequest request) => Unsupported<MasterDataDeleteResult>();

        public ServiceResult<IReadOnlyList<TaskListMasterDataDto>> GetTaskLists(MasterDataQueryRequest request) =>
            Query(request, taskLists, x => !request.IdProject.HasValue || x.IdProject == request.IdProject.Value);
        public ServiceResult<TaskListMasterDataDto> GetTaskList(MasterDataItemRequest request) =>
            Find(request, taskLists, x => x.IdTaskList, "TaskListNotFound");
        public ServiceResult<TaskListMasterDataDto> CreateTaskList(SaveTaskListRequest request) =>
            Create(request, taskLists, x => x.IdTaskList, (x, id) => x.IdTaskList = id);
        public ServiceResult<TaskListMasterDataDto> UpdateTaskList(SaveTaskListRequest request) =>
            Update(request, taskLists, x => x.IdTaskList);
        public ServiceResult<MasterDataDeleteResult> DeleteTaskList(DeleteMasterDataRequest request) =>
            Delete(request, taskLists, x => x.IdTaskList, "TaskList");

        public ServiceResult<IReadOnlyList<TaskItemMasterDataDto>> GetTaskItems(MasterDataQueryRequest request) =>
            Query(request, tasks, x => !request.IdTaskList.HasValue || x.IdTaskList == request.IdTaskList);
        public ServiceResult<TaskItemMasterDataDto> GetTaskItem(MasterDataItemRequest request) =>
            Find(request, tasks, x => x.IdTaskItem, "TaskItemNotFound");
        public ServiceResult<TaskItemMasterDataDto> CreateTaskItem(SaveTaskItemRequest request) =>
            Create(request, tasks, x => x.IdTaskItem, (x, id) => x.IdTaskItem = id);
        public ServiceResult<TaskItemMasterDataDto> UpdateTaskItem(SaveTaskItemRequest request) =>
            Update(request, tasks, x => x.IdTaskItem);
        public ServiceResult<MasterDataDeleteResult> DeleteTaskItem(DeleteMasterDataRequest request) =>
            Delete(request, tasks, x => x.IdTaskItem, "TaskItem");

        public ServiceResult<IReadOnlyList<TagMasterDataDto>> GetTags(MasterDataQueryRequest request) => Query(request, tags);
        public ServiceResult<TagMasterDataDto> GetTag(MasterDataItemRequest request) => Find(request, tags, x => x.IdTag, "TagNotFound");
        public ServiceResult<TagMasterDataDto> CreateTag(SaveTagRequest request) => Create(request, tags, x => x.IdTag, (x, id) => x.IdTag = id);
        public ServiceResult<TagMasterDataDto> UpdateTag(SaveTagRequest request) => Update(request, tags, x => x.IdTag);
        public ServiceResult<MasterDataDeleteResult> DeleteTag(DeleteMasterDataRequest request) => Delete(request, tags, x => x.IdTag, "Tag");

        public ServiceResult<IReadOnlyList<NoteMasterDataDto>> GetNotes(MasterDataQueryRequest request) => Query(request, notes);
        public ServiceResult<NoteMasterDataDto> GetNote(MasterDataItemRequest request) => Find(request, notes, x => x.IdNote, "NoteNotFound");
        public ServiceResult<NoteMasterDataDto> CreateNote(SaveNoteRequest request) => Create(request, notes, x => x.IdNote, (x, id) => x.IdNote = id);
        public ServiceResult<NoteMasterDataDto> UpdateNote(SaveNoteRequest request) => Update(request, notes, x => x.IdNote);
        public ServiceResult<MasterDataDeleteResult> DeleteNote(DeleteMasterDataRequest request) => Delete(request, notes, x => x.IdNote, "Note");

        public ServiceResult<IReadOnlyList<WebLinkMasterDataDto>> GetWebLinks(MasterDataQueryRequest request) => Query(request, webLinks);
        public ServiceResult<WebLinkMasterDataDto> GetWebLink(MasterDataItemRequest request) => Find(request, webLinks, x => x.IdWebLink, "WebLinkNotFound");
        public ServiceResult<WebLinkMasterDataDto> CreateWebLink(SaveWebLinkRequest request) => Create(request, webLinks, x => x.IdWebLink, (x, id) => x.IdWebLink = id);
        public ServiceResult<WebLinkMasterDataDto> UpdateWebLink(SaveWebLinkRequest request) => Update(request, webLinks, x => x.IdWebLink);
        public ServiceResult<MasterDataDeleteResult> DeleteWebLink(DeleteMasterDataRequest request) => Delete(request, webLinks, x => x.IdWebLink, "WebLink");

        public ServiceResult<CreateTenantAdminResult> CreateTenantAdmin(CreateTenantAdminRequest request) =>
            ServiceResult<CreateTenantAdminResult>.Fail("UnsupportedOperation", "The test tenant already exists.");

        public ServiceResult<TenantUserDto> CreateUser(CreateUserRequest request)
        {
            if (request == null || request.IdTenant != Tenant.IdTenant)
                return ServiceResult<TenantUserDto>.Fail("InvalidRequest", "A user for the benchmark tenant is required.");
            var user = new TenantUserDto
            {
                IdUser = Id(100 + users.Count), IdTenant = request.IdTenant,
                UserIdent = request.UserIdent, FirstName = request.FirstName, LastName = request.LastName,
                EMail = request.EMail, IsAdmin = request.IsAdmin, IsActive = true,
                LastLogin = SeedTime, DateCreated = SeedTime, DateModified = SeedTime
            };
            users.Add(user);
            return ServiceResult<TenantUserDto>.Ok(user);
        }

        public ServiceResult<TenantUserDto> DeactivateUser(Guid idTenant, Guid idUser) =>
            SetUserFlags(idTenant, idUser, false, false);
        public ServiceResult<TenantUserDto> DeleteUser(Guid idTenant, Guid idUser) =>
            SetUserFlags(idTenant, idUser, false, true);
        public ServiceResult<TenantUserDto> SetUserFlags(Guid idTenant, Guid idUser, bool isActive, bool isDeleted)
        {
            var user = users.SingleOrDefault(x => x.IdTenant == idTenant && x.IdUser == idUser);
            if (user == null) return ServiceResult<TenantUserDto>.Fail("UserNotFound", "The benchmark user was not found.");
            user.IsActive = isActive;
            user.IsDeleted = isDeleted;
            user.DateModified = SeedTime.AddMinutes(1);
            return ServiceResult<TenantUserDto>.Ok(user);
        }
        public ServiceResult<IReadOnlyList<TenantUserDto>> GetTenantUsers(Guid idTenant, bool includeDeleted = false) =>
            ServiceResult<IReadOnlyList<TenantUserDto>>.Ok(users.Where(x => x.IdTenant == idTenant && (includeDeleted || !x.IsDeleted)).ToList());

        public ServiceResult<TimeBookingDayDto> GetBookingDay(GetBookingDayRequest request)
        {
            if (!ValidAccess(request?.AccessContext))
                return ServiceResult<TimeBookingDayDto>.Fail("InvalidAccess", "The benchmark booking access context is invalid.");
            return ServiceResult<TimeBookingDayDto>.Ok(Day(request.BookingDate));
        }
        public ServiceResult<TimeBookingMutationResult> AddTimeBooking(SaveTimeBookingRequest request)
        {
            if (!ValidBooking(request))
                return ServiceResult<TimeBookingMutationResult>.Fail("InvalidRequest", "A benchmark time booking is required.");
            if (request.Item.IdTimeItem == Guid.Empty) request.Item.IdTimeItem = Id(200 + timeItems.Count);
            timeItems.Add(request.Item);
            try
            {
                return Mutation(request.Item);
            }
            catch (TimeBookingValidationException ex)
            {
                timeItems.Remove(request.Item);
                return ServiceResult<TimeBookingMutationResult>.Fail("InvalidTimeBooking", ex.Message);
            }
        }
        public ServiceResult<TimeBookingMutationResult> EditTimeBooking(SaveTimeBookingRequest request)
        {
            if (!ValidBooking(request))
                return ServiceResult<TimeBookingMutationResult>.Fail("InvalidRequest", "A benchmark time booking is required.");
            var index = timeItems.FindIndex(x => x.IdTimeItem == request.Item.IdTimeItem);
            if (index < 0) return ServiceResult<TimeBookingMutationResult>.Fail("TimeItemNotFound", "The benchmark time booking was not found.");
            var previous = timeItems[index];
            timeItems[index] = request.Item;
            try
            {
                return Mutation(request.Item);
            }
            catch (TimeBookingValidationException ex)
            {
                timeItems[index] = previous;
                return ServiceResult<TimeBookingMutationResult>.Fail("InvalidTimeBooking", ex.Message);
            }
        }
        public ServiceResult<TimeBookingMutationResult> DeleteTimeBooking(DeleteTimeBookingRequest request)
        {
            if (!ValidAccess(request?.AccessContext))
                return ServiceResult<TimeBookingMutationResult>.Fail("InvalidAccess", "The benchmark booking access context is invalid.");
            var item = timeItems.SingleOrDefault(x => x.IdTimeItem == request.IdTimeItem);
            if (item == null) return ServiceResult<TimeBookingMutationResult>.Fail("TimeItemNotFound", "The benchmark time booking was not found.");
            timeItems.Remove(item);
            var day = NormalizeDay(request.BookingDate, out var removed);
            removed.Add(item);
            return ServiceResult<TimeBookingMutationResult>.Ok(new TimeBookingMutationResult
            {
                BookingDay = day, AffectedItem = item,
                RemovedItems = removed
            });
        }
        public ServiceResult<TimeBookingMutationResult> InsertWorkBreak(InsertSystemTimeMarkerRequest request) => Unsupported<TimeBookingMutationResult>();
        public ServiceResult<TimeBookingMutationResult> InsertStopMark(InsertSystemTimeMarkerRequest request) => Unsupported<TimeBookingMutationResult>();
        public ServiceResult<IReadOnlyList<TimeBookingTemplateDto>> GetRecentTimeTemplates(RecentTimeTemplatesRequest request) =>
            ServiceResult<IReadOnlyList<TimeBookingTemplateDto>>.Ok(new List<TimeBookingTemplateDto>());

        private void AddSeedTime(int id, int hour, int minute, string title, SystemTimeMarkerKind marker) =>
            timeItems.Add(new TimeBookingItemDto
            {
                IdTimeItem = Id(id), IdTenant = Tenant.IdTenant, IdUser = ActingUserId,
                IdProject = ProjectId,
                IdCategory = marker == SystemTimeMarkerKind.WorkBreak ? SystemTimeMarkerIds.WorkBreakCategoryId :
                    marker == SystemTimeMarkerKind.StopMark ? SystemTimeMarkerIds.StopMarkCategoryId : CategoryId,
                ShortTitle = title,
                Description = title, EventTime = SeedTime.Date.AddHours(hour).AddMinutes(minute),
                BookingDate = SeedTime.Date, MarkerKind = marker,
                DateCreated = SeedTime, DateModified = SeedTime
            });

        private TimeBookingDayDto Day(DateTime date) => NormalizeDay(date, out _);

        private TimeBookingDayDto NormalizeDay(DateTime date, out List<TimeBookingItemDto> removed)
        {
            var source = timeItems.Where(x => (x.BookingDate ?? x.EventTime?.Date) == date.Date).ToList();
            var options = new TimeBookingOptions();
            var timeline = source.Select(item => new TimeItem
            {
                IdTimeItem = item.IdTimeItem, IdUser = item.IdUser, IdProject = item.IdProject,
                IdCategory = item.IdCategory, IdTask = item.IdTask, EventTime = item.EventTime,
                BookingDate = item.BookingDate, EventInfo = item.EventInfo, EventTypeInfo = options.TimeEventType,
                ShortTitle = item.ShortTitle, Description = item.Description, IsItemDeleted = item.IsItemDeleted
            }).ToList();
            var normalized = TimeBookingAlgorithm.NormalizeBookingDay(timeline, options);
            var removedIds = new HashSet<Guid>(normalized.RemovedItems.Select(item => item.IdTimeItem));
            removed = source.Where(item => removedIds.Contains(item.IdTimeItem)).ToList();
            timeItems.RemoveAll(item => removedIds.Contains(item.IdTimeItem));
            var byId = source.ToDictionary(item => item.IdTimeItem);
            var items = normalized.TimelineItems.Select(item =>
            {
                var dto = byId[item.IdTimeItem];
                dto.MarkerKind = options.GetMarkerKind(item);
                dto.DurationToNext = item.DurationToNext;
                dto.DurationToPrevious = item.DurationToPrevious;
                return dto;
            }).ToList();
            return new TimeBookingDayDto
            {
                IdTenant = Tenant.IdTenant, IdUser = ActingUserId, BookingDate = date.Date,
                Items = items, FirstBookingAt = items.FirstOrDefault()?.EventTime,
                LastBookingAt = items.LastOrDefault()?.EventTime,
                TotalBookedTime = TimeSpan.FromTicks(items.Where(item => item.MarkerKind == SystemTimeMarkerKind.Normal).Sum(item => item.DurationToNext?.Ticks ?? 0)),
                WorkBreakTime = TimeSpan.FromTicks(items.Where(item => item.MarkerKind == SystemTimeMarkerKind.WorkBreak).Sum(item => item.DurationToNext?.Ticks ?? 0))
            };
        }
        private ServiceResult<TimeBookingMutationResult> Mutation(TimeBookingItemDto item)
        {
            var day = NormalizeDay(item.BookingDate ?? item.EventTime?.Date ?? SeedTime.Date, out var removed);
            return ServiceResult<TimeBookingMutationResult>.Ok(new TimeBookingMutationResult
            {
                AffectedItem = item, BookingDay = day, RemovedItems = removed
            });
        }
        private bool ValidAccess(TimeBookingAccessContextDto access) =>
            access != null && access.IdTenant == Tenant.IdTenant &&
            access.IdActingUser == ActingUserId && access.IdBookingUser == ActingUserId;

        private bool ValidBooking(SaveTimeBookingRequest request) =>
            ValidAccess(request?.AccessContext) && request.Item != null &&
            request.Item.EventTime.HasValue &&
            projects.Any(project => project.IdProject == request.Item.IdProject && project.IsActive && !project.IsDeleted);

        private ServiceResult<IReadOnlyList<T>> Query<T>(MasterDataQueryRequest request, List<T> items, Func<T, bool> predicate = null)
        {
            if (request == null || request.IdTenant != Tenant.IdTenant)
                return ServiceResult<IReadOnlyList<T>>.Fail("InvalidRequest", "A benchmark tenant query is required.");
            return ServiceResult<IReadOnlyList<T>>.Ok((predicate == null ? items : items.Where(predicate)).ToList());
        }
        private ServiceResult<T> Find<T>(MasterDataItemRequest request, List<T> items, Func<T, Guid> id, string code)
        {
            var item = request == null ? default(T) : items.SingleOrDefault(x => id(x) == request.IdItem);
            return item == null ? ServiceResult<T>.Fail(code, "The benchmark item was not found.") : ServiceResult<T>.Ok(item);
        }
        private ServiceResult<T> Create<T>(SaveMasterDataRequest<T> request, List<T> items, Func<T, Guid> id, Action<T, Guid> setId)
        {
            if (request == null || request.Item == null)
                return ServiceResult<T>.Fail("InvalidRequest", "A benchmark item is required.");
            if (id(request.Item) == Guid.Empty) setId(request.Item, Id(300 + items.Count));
            items.Add(request.Item);
            return ServiceResult<T>.Ok(request.Item);
        }
        private ServiceResult<T> Update<T>(SaveMasterDataRequest<T> request, List<T> items, Func<T, Guid> id)
        {
            if (request == null || request.Item == null)
                return ServiceResult<T>.Fail("InvalidRequest", "A benchmark item is required.");
            var index = items.FindIndex(x => id(x) == id(request.Item));
            if (index < 0) return ServiceResult<T>.Fail("ItemNotFound", "The benchmark item was not found.");
            items[index] = request.Item;
            return ServiceResult<T>.Ok(request.Item);
        }
        private ServiceResult<MasterDataDeleteResult> Delete<T>(DeleteMasterDataRequest request, List<T> items, Func<T, Guid> id, string entity)
        {
            var item = request == null ? default(T) : items.SingleOrDefault(x => id(x) == request.IdItem);
            if (item == null) return ServiceResult<MasterDataDeleteResult>.Fail("ItemNotFound", "The benchmark item was not found.");
            items.Remove(item);
            return ServiceResult<MasterDataDeleteResult>.Ok(new MasterDataDeleteResult
            {
                IdTenant = request.IdTenant, IdItem = request.IdItem, EntityName = entity,
                Deleted = true, HardDeleted = request.HardDelete, DeletedAt = SeedTime.AddMinutes(1)
            });
        }
        private static ServiceResult<T> Unsupported<T>() =>
            ServiceResult<T>.Fail("UnsupportedOperation", "This operation is unavailable in this test double.");
        private static Guid Id(int value) => new Guid(value, 0, 0, new byte[8]);
    }
}
