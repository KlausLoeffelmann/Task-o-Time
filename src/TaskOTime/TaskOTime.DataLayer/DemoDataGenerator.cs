using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Security.Cryptography;
using TaskOTime.DTOs;

namespace TaskOTime.DataLayer
{
    public sealed class DemoDataGenerator
    {
        private readonly Func<TaskOTimeContext> contextFactory;
        private readonly Func<DateTimeOffset> clock;

        public DemoDataGenerator()
            : this(TaskOTimeContextFactory.Create, () => DateTimeOffset.Now)
        {
        }

        public DemoDataGenerator(Func<TaskOTimeContext> contextFactory, Func<DateTimeOffset> clock)
        {
            this.contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public DemoDataSmokeReport CreateDemoData(DemoDataOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options.Validate();

            using (var context = contextFactory())
            {
                var handles = options.Users.Select(u => u.Handle).ToArray();
                if (context.User.Any(u => handles.Contains(u.UserIdent)))
                {
                    throw new InvalidOperationException("One or more configured demo users already exist. Use --new before creating a fresh demo dataset.");
                }

                using (var transaction = context.Database.BeginTransaction())
                {
                    var now = clock();
                    var referenceDate = now.Date;
                    var catalog = new DemoTextCatalog(options.Text);
                    SystemTimeMarkerSeed.EnsureLookupItems(context);
                    var tenants = CreateTenants(context, options.Tenants, now, catalog);
                    var users = CreateUsers(context, tenants, options.Users, now);
                    var projects = CreateProjects(context, tenants, users, options.Projects, now, catalog);
                    var assignments = CreateProjectUserAssignments(context, users, projects, options.ProjectAssignments, now);
                    var symbols = CreateSymbols(context, projects);
                    var categories = CreateCategories(context, users, symbols, options.Categories, catalog);
                    SystemTimeMarkerSeed.EnsureCategories(context, SelectSystemMarkerOwner(users).IdUser, symbols.First().IdCategorySymbol);
                    var lists = CreateTaskLists(context, users, projects, symbols, options.Lists, catalog);
                    var tags = CreateTags(context, users, options.Tasks.Total, catalog);
                    var tasks = CreateTasks(context, users, projects, lists, symbols, tags, options.Tasks, now, catalog);
                    var timeItemLinks = CreateTimeItems(context, users, projects, assignments, categories, tasks, options.TimeItems, options.TimeItemDates, referenceDate, now, catalog);
                    CreateNotes(context, users, projects, tasks, tags, now, catalog);
                    CreateWebLinks(context, users, projects, tags, now, catalog);
                    CreateSharableProjects(context, users, projects, categories, now);
                    CreateTenantLeads(context, users, options.Tenants, now, catalog);
                    CreateLogItems(context, users, now, catalog);

                    context.SaveChanges();
                    ApplyTimeItemLinks(timeItemLinks);
                    context.SaveChanges();
                    var report = DemoDataSmokeReport.Collect(context, referenceDate);
                    report.Validate(options.TimeItemDates, referenceDate);
                    transaction.Commit();
                    return report;
                }
            }
        }

        private static List<Tenant> CreateTenants(TaskOTimeContext context, int count, DateTimeOffset now, DemoTextCatalog catalog)
        {
            var tenants = new List<Tenant>();
            for (var i = 0; i < count; i++)
            {
                var tenant = new Tenant
                {
                    IdTenant = Guid.NewGuid(),
                    TenantName = catalog.TenantName(i),
                    TenantIdentifier = "DEMO-TENANT-" + (i + 1).ToString("000"),
                    Description = catalog.TenantDescription(i),
                    IsActive = true,
                    IsDeleted = false,
                    DateCreated = now,
                    DateModified = now,
                    ExternalId = "demo-tenant-" + i
                };

                context.Tenant.Add(tenant);
                tenants.Add(tenant);
            }

            return tenants;
        }

        private static List<User> CreateUsers(TaskOTimeContext context, IReadOnlyList<Tenant> tenants, IEnumerable<DemoUserOptions> userOptions, DateTimeOffset now)
        {
            var users = new List<User>();
            var index = 0;
            foreach (var userOption in userOptions)
            {
                var tenantNumber = userOption.TenantIndex ?? ((index % tenants.Count) + 1);
                var tenant = tenants[tenantNumber - 1];
                var salt = CreateSalt();
                var user = new User
                {
                    IdUser = Guid.NewGuid(),
                    IdTenant = tenant.IdTenant,
                    UserIdent = userOption.Handle,
                    FirstName = userOption.FirstName,
                    LastName = userOption.LastName,
                    EMail = userOption.Email,
                    IsAdmin = userOption.IsAdmin,
                    IsActive = true,
                    IsDeleted = false,
                    MustChangePassword = true,
                    PasswordSalt = salt,
                    PasswordHash = HashPassword(userOption.Password, salt),
                    PasswordChangedAt = null,
                    PreliminaryPasswordExpiresAt = now.AddDays(14),
                    FailedLoginCount = 0,
                    LockoutUntil = null,
                    EmojiIndex = index,
                    MaxProjects = 50,
                    LastLogin = now,
                    DateCreated = now,
                    DateModified = now,
                    SyncId = Guid.NewGuid(),
                    SyncStatus = 0,
                    ExternalId = "demo-user-" + index
                };

                context.User.Add(user);
                users.Add(user);
                index++;
            }

            return users;
        }

        private static User SelectSystemMarkerOwner(IReadOnlyList<User> users)
        {
            return users
                .OrderByDescending(user => user.IsAdmin)
                .ThenBy(user => user.UserIdent, StringComparer.OrdinalIgnoreCase)
                .First();
        }

        private static List<Project> CreateProjects(TaskOTimeContext context, IReadOnlyList<Tenant> tenants, IReadOnlyList<User> users, int count, DateTimeOffset now, DemoTextCatalog catalog)
        {
            var projects = new List<Project>();
            for (var i = 0; i < count; i++)
            {
                var tenant = tenants[i % tenants.Count];
                var tenantUsers = users.Where(u => u.IdTenant == tenant.IdTenant)
                    .OrderBy(u => u.UserIdent, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var user = tenantUsers.FirstOrDefault(u => u.IsAdmin) ?? tenantUsers.First();
                var project = new Project
                {
                    IdProject = Guid.NewGuid(),
                    IdTenant = tenant.IdTenant,
                    IdUser = user.IdUser,
                    ProjectName = catalog.ProjectName(i),
                    ProjectType = i % 3,
                    ProjectNumber = 1000 + i,
                    ProjectDescription = catalog.ProjectDescription(i),
                    ProjectIdentifier = "DEMO-" + (i + 1).ToString("000"),
                    ProjectSymbolChar = "P",
                    ProjectSymbolColor = i % 12,
                    IsActive = true,
                    IsDeleted = false,
                    Scope = 0,
                    IsSystem = false,
                    DateCreated = now,
                    DateModified = now,
                    SyncId = Guid.NewGuid(),
                    SyncStatus = 0,
                    ExternalId = "demo-project-" + i
                };

                context.Project.Add(project);
                projects.Add(project);
            }

            return projects;
        }

        private static List<ProjectUserAssignment> CreateProjectUserAssignments(TaskOTimeContext context, IReadOnlyList<User> users, IReadOnlyList<Project> projects, DemoProjectAssignmentOptions options, DateTimeOffset now)
        {
            var assignments = new List<ProjectUserAssignment>();
            var random = new Random(options.Seed);
            var overrides = (options.Projects ?? new List<DemoProjectAssignmentOverride>())
                .ToDictionary(p => p.ProjectNumber, p => p.Users, EqualityComparer<int>.Default);
            var displayOrder = 0;
            for (var projectIndex = 0; projectIndex < projects.Count; projectIndex++)
            {
                var project = projects[projectIndex];
                var tenantUsers = users.Where(u => u.IdTenant == project.IdTenant)
                    .OrderBy(u => u.UserIdent, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                var assignedUsers = SelectAssignedUsers(project, projectIndex, tenantUsers, options, overrides, random);

                foreach (var user in assignedUsers)
                {
                    var assignment = new ProjectUserAssignment
                    {
                        IdProjectUserAssignment = Guid.NewGuid(),
                        IdProject = project.IdProject,
                        IdUser = user.IdUser,
                        IdAssignedByUser = project.IdUser,
                        AssignmentRole = user.IsAdmin ? 1 : 0,
                        CanBookTime = true,
                        CanManageTasks = user.IsAdmin,
                        CanManageProject = user.IdUser == project.IdUser || user.IsAdmin,
                        DisplayOrder = displayOrder,
                        IsActive = true,
                        IsDeleted = false,
                        DateAssigned = now,
                        DateCreated = now,
                        DateModified = now,
                        ExternalId = "demo-project-assignment-" + (displayOrder + 1)
                    };

                    context.ProjectUserAssignment.Add(assignment);
                    assignments.Add(assignment);
                    displayOrder++;
                }
            }

            return assignments;
        }

        private static IReadOnlyList<User> SelectAssignedUsers(Project project, int projectIndex, IReadOnlyList<User> tenantUsers, DemoProjectAssignmentOptions options, IDictionary<int, List<string>> overrides, Random random)
        {
            if (tenantUsers.Count == 0)
            {
                throw new InvalidOperationException("Project " + project.ProjectName + " has no users in its tenant.");
            }

            var assignedUsers = new List<User>();
            AddAssignmentUser(assignedUsers, tenantUsers.First(u => u.IdUser == project.IdUser));
            if (options.IncludeAdmins)
            {
                foreach (var admin in tenantUsers.Where(u => u.IsAdmin))
                {
                    AddAssignmentUser(assignedUsers, admin);
                }
            }

            var projectNumber = projectIndex + 1;
            if (overrides.TryGetValue(projectNumber, out var handles))
            {
                foreach (var handle in handles)
                {
                    var user = tenantUsers.FirstOrDefault(u => string.Equals(u.UserIdent, handle, StringComparison.OrdinalIgnoreCase));
                    if (user == null)
                    {
                        throw new InvalidOperationException("Project " + projectNumber + " assignment references a user from another tenant: " + handle);
                    }

                    AddAssignmentUser(assignedUsers, user);
                }
            }
            else if (options.IsAllTenantUsers)
            {
                foreach (var user in tenantUsers)
                {
                    AddAssignmentUser(assignedUsers, user);
                }
            }
            else if (options.IsPerProject)
            {
                return assignedUsers;
            }
            else
            {
                var candidates = options.IsRandom ? Shuffle(tenantUsers, random) : Rotate(tenantUsers, projectIndex);
                foreach (var user in candidates.Take(options.UsersPerProject))
                {
                    AddAssignmentUser(assignedUsers, user);
                }
            }

            return assignedUsers;
        }

        private static IReadOnlyList<User> Rotate(IReadOnlyList<User> users, int offset)
        {
            return users.Skip(offset % users.Count).Concat(users.Take(offset % users.Count)).ToList();
        }

        private static IReadOnlyList<User> Shuffle(IReadOnlyList<User> users, Random random)
        {
            return users.Select(user => new { User = user, Sort = random.Next() })
                .OrderBy(item => item.Sort)
                .Select(item => item.User)
                .ToList();
        }

        private static void AddAssignmentUser(ICollection<User> assignedUsers, User user)
        {
            if (!assignedUsers.Any(u => u.IdUser == user.IdUser))
            {
                assignedUsers.Add(user);
            }
        }

        private static List<CategorySymbol> CreateSymbols(TaskOTimeContext context, IReadOnlyList<Project> projects)
        {
            var symbols = new List<CategorySymbol>();
            for (var i = 0; i < projects.Count; i++)
            {
                var symbol = new CategorySymbol
                {
                    IdCategorySymbol = Guid.NewGuid(),
                    IdProject = projects[i].IdProject,
                    SymbolName = "Project Symbol " + (i + 1),
                    SymbolChar = ((char)('A' + (i % 26))).ToString(),
                    SymbolColor = i % 12
                };

                context.CategorySymbol.Add(symbol);
                symbols.Add(symbol);
            }

            return symbols;
        }

        private static List<Category> CreateCategories(TaskOTimeContext context, IReadOnlyList<User> users, IReadOnlyList<CategorySymbol> symbols, int count, DemoTextCatalog catalog)
        {
            var categories = new List<Category>();
            for (var i = 0; i < count; i++)
            {
                var user = users[i % users.Count];
                var category = new Category
                {
                    IdCategory = Guid.NewGuid(),
                    IdUser = user.IdUser,
                    IdSymbol = symbols[i % symbols.Count].IdCategorySymbol,
                    CategoryName = catalog.TagName(i),
                    DisplayOrder = i,
                    IsPublic = i % 4 == 0
                };

                context.Category.Add(category);
                categories.Add(category);
            }

            return categories;
        }

        private static List<TaskList> CreateTaskLists(TaskOTimeContext context, IReadOnlyList<User> users, IReadOnlyList<Project> projects, IReadOnlyList<CategorySymbol> symbols, int count, DemoTextCatalog catalog)
        {
            var lists = new List<TaskList>();
            for (var i = 0; i < count; i++)
            {
                var project = projects[i % projects.Count];
                var list = new TaskList
                {
                    IdTaskList = Guid.NewGuid(),
                    IdProject = project.IdProject,
                    IdUser = project.IdUser,
                    IdSymbol = symbols[i % symbols.Count].IdCategorySymbol,
                    TaskListName = catalog.TaskListName(i),
                    DisplayOrder = i,
                    IsPublic = i % 2 == 0
                };

                context.TaskList.Add(list);
                lists.Add(list);
            }

            return lists;
        }

        private static List<Tag> CreateTags(TaskOTimeContext context, IReadOnlyList<User> users, int taskCount, DemoTextCatalog catalog)
        {
            var tagCount = Math.Max(6, Math.Min(24, taskCount / 5));
            var tags = new List<Tag>();
            for (var i = 0; i < tagCount; i++)
            {
                var user = users[i % users.Count];
                var tag = new Tag
                {
                    IdTag = Guid.NewGuid(),
                    IdUser = user.IdUser,
                    Tag1 = catalog.TagName(i),
                    Description = catalog.TagDescription(i),
                    DateCreated = DateTimeOffset.Now,
                    DateModified = DateTimeOffset.Now
                };

                context.Tag.Add(tag);
                tags.Add(tag);
            }

            return tags;
        }

        private static List<TaskItem> CreateTasks(TaskOTimeContext context, IReadOnlyList<User> users, IReadOnlyList<Project> projects, IReadOnlyList<TaskList> lists, IReadOnlyList<CategorySymbol> symbols, IReadOnlyList<Tag> tags, DemoTaskOptions options, DateTimeOffset now, DemoTextCatalog catalog)
        {
            var tasks = new List<TaskItem>();
            for (var i = 0; i < options.Total; i++)
            {
                var list = lists[i % lists.Count];
                var isCompleted = i >= options.Open;
                var task = new TaskItem
                {
                    IdTaskItem = Guid.NewGuid(),
                    IdUser = list.IdUser,
                    IdProject = list.IdProject,
                    IdTaskList = list.IdTaskList,
                    IdSymbol = symbols[i % symbols.Count].IdCategorySymbol,
                    TaskItemName = catalog.TaskName(i, isCompleted),
                    TaskItemDescription = catalog.TaskDescription(i),
                    QuickInfo = catalog.TaskQuickInfo(i),
                    DueDate = now.AddDays((i % 14) - 3),
                    TaskHoursBudget = 1 + (i % 8),
                    Priority = i % 4,
                    IsPrivateTask = i % 9 == 0,
                    Scope = 0,
                    IsActive = !isCompleted,
                    IsCompleted = isCompleted,
                    DateCompleted = isCompleted ? now.AddDays(-(i % 10)) : (DateTimeOffset?)null,
                    IsDeleted = false,
                    IsForeign = false,
                    DateCreated = now.AddDays(-(i % 20)),
                    DateModified = now,
                    SyncId = Guid.NewGuid(),
                    SyncStatus = 0,
                    ExternalId = "demo-task-" + i
                };

                task.Tag.Add(tags[i % tags.Count]);
                if (i % 3 == 0)
                {
                    task.Tag.Add(tags[(i + 1) % tags.Count]);
                }

                context.TaskItem.Add(task);
                tasks.Add(task);
            }

            return tasks;
        }

        private static List<TimeItemLink> CreateTimeItems(
            TaskOTimeContext context,
            IReadOnlyList<User> users,
            IReadOnlyList<Project> projects,
            IReadOnlyList<ProjectUserAssignment> assignments,
            IReadOnlyList<Category> categories,
            IReadOnlyList<TaskItem> tasks,
            int count,
            DemoTimeItemDateOptions dateOptions,
            DateTime referenceDate,
            DateTimeOffset now,
            DemoTextCatalog catalog)
        {
            var projectById = projects.ToDictionary(project => project.IdProject);
            var userById = users.ToDictionary(user => user.IdUser);
            var categoriesByUser = categories
                .GroupBy(category => category.IdUser)
                .ToDictionary(group => group.Key, group => group.OrderBy(category => category.DisplayOrder).ToList());
            var tasksByProject = tasks
                .GroupBy(task => task.IdProject)
                .ToDictionary(group => group.Key, group => group.OrderBy(task => task.DateCreated).ThenBy(task => task.TaskItemName).ToList());
            var bookableAssignments = assignments
                .Where(assignment => assignment.CanBookTime && assignment.IsActive && !assignment.IsDeleted)
                .OrderBy(assignment => projectById[assignment.IdProject].ProjectNumber)
                .ThenBy(assignment => userById[assignment.IdUser].UserIdent, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (bookableAssignments.Count == 0)
            {
                throw new InvalidOperationException("Demo time bookings require at least one active project assignment with booking permission.");
            }

            var links = new List<TimeItemLink>();
            var bookingDates = dateOptions.GetBookingDates(referenceDate);
            var latestTimeItemsByUserAndDate = new Dictionary<Tuple<Guid, DateTime>, TimeItem>();
            var eventSlotsByUserAndDate = new Dictionary<Tuple<Guid, DateTime>, int>();
            for (var i = 0; i < count; i++)
            {
                var bookingDateIndex = i % bookingDates.Count;
                var bookingDateOccurrence = i / bookingDates.Count;
                var assignmentGroup = bookingDateOccurrence / DemoTimeItemDateOptions.MinimumTimeItemsPerDate;
                var assignment = bookableAssignments[(bookingDateIndex + assignmentGroup) % bookableAssignments.Count];
                var project = projectById[assignment.IdProject];
                var user = userById[assignment.IdUser];
                var cycle = i / bookableAssignments.Count;
                var bookingDate = bookingDates[bookingDateIndex];
                var projectTasks = tasksByProject.TryGetValue(project.IdProject, out var matchingTasks) && matchingTasks.Count > 0
                    ? matchingTasks
                    : tasks.ToList();
                var task = projectTasks[cycle % projectTasks.Count];
                var userCategories = categoriesByUser.TryGetValue(user.IdUser, out var matchingCategories) && matchingCategories.Count > 0
                    ? matchingCategories
                    : categories.ToList();
                var category = userCategories[(cycle + i) % userCategories.Count];
                var eventSlotKey = Tuple.Create(user.IdUser, bookingDate);
                eventSlotsByUserAndDate.TryGetValue(eventSlotKey, out var eventSlot);
                eventSlotsByUserAndDate[eventSlotKey] = eventSlot + 1;
                var eventTime = new DateTimeOffset(bookingDate, now.Offset).AddHours(8);
                TimeItem previousTimeItem = null;
                TimeSpan? durationFromPrevious = null;
                if (latestTimeItemsByUserAndDate.TryGetValue(eventSlotKey, out previousTimeItem))
                {
                    durationFromPrevious = GetBookingInterval(bookingDate, eventSlot - 1);
                    eventTime = previousTimeItem.EventTime.Value.Add(durationFromPrevious.Value);
                    if (eventTime.Date != bookingDate || eventTime.TimeOfDay > TimeSpan.FromHours(20))
                    {
                        throw new InvalidOperationException(
                            "The configured demo bookings cannot be spaced within a realistic 08:00-20:00 booking day. Reduce TimeItems or increase timeItemDates.days.");
                    }
                }

                var item = new TimeItem
                {
                    IdTimeItem = Guid.NewGuid(),
                    IdUser = user.IdUser,
                    IdProject = project.IdProject,
                    IdTask = task.IdTaskItem,
                    IdCategory = category.IdCategory,
                    ShortTitle = catalog.TimeShortTitle(i),
                    Description = catalog.TimeDescription(i),
                    EventTime = eventTime,
                    BookingDate = bookingDate,
                    EventInfo = catalog.EventInfo(i),
                    EventTypeInfo = 1,
                    Scope = 0,
                    IsItemCompleted = true,
                    IsItemDeleted = false,
                    IsStartAction = i % 2 == 0,
                    IsEndAction = i % 2 != 0,
                    Priority = i % 4,
                    MachineID = Environment.MachineName,
                    DateCreated = now.AddDays(-(cycle % 7)),
                    DateModified = now,
                    SyncId = Guid.NewGuid(),
                    SyncStatus = 0,
                    ExternalId = "demo-time-" + i
                };

                if (previousTimeItem != null)
                {
                    var duration = durationFromPrevious.Value;
                    previousTimeItem.DurationToNext = duration;
                    previousTimeItem.DurationTicksToNext = duration.Ticks;
                    previousTimeItem.Value = (decimal)duration.TotalHours;
                    previousTimeItem.DateItemFinished = eventTime;
                    item.DurationToPrevious = duration;
                    item.DurationTicksToPrevious = duration.Ticks;
                    links.Add(new TimeItemLink(previousTimeItem, item));
                }

                context.TimeItem.Add(item);
                latestTimeItemsByUserAndDate[eventSlotKey] = item;
            }

            return links;
        }

        private static void ApplyTimeItemLinks(IEnumerable<TimeItemLink> links)
        {
            foreach (var link in links)
            {
                link.Previous.IdNextItem = link.Next.IdTimeItem;
                link.Next.IdPreviousItem = link.Previous.IdTimeItem;
            }
        }

        private sealed class TimeItemLink
        {
            public TimeItemLink(TimeItem previous, TimeItem next)
            {
                Previous = previous;
                Next = next;
            }

            public TimeItem Previous { get; }
            public TimeItem Next { get; }
        }

        private static TimeSpan GetBookingInterval(DateTime bookingDate, int intervalIndex)
        {
            var intervalSteps = (bookingDate.Day + intervalIndex) % 5;
            return TimeSpan.FromMinutes(30 + intervalSteps * 15);
        }

        private static void CreateNotes(TaskOTimeContext context, IReadOnlyList<User> users, IReadOnlyList<Project> projects, IReadOnlyList<TaskItem> tasks, IReadOnlyList<Tag> tags, DateTimeOffset now, DemoTextCatalog catalog)
        {
            var count = Math.Max(6, tasks.Count / 4);
            for (var i = 0; i < count; i++)
            {
                var task = tasks[i % tasks.Count];
                var note = new Note
                {
                    IdNote = Guid.NewGuid(),
                    IdUser = task.IdUser,
                    IdProject = task.IdProject,
                    IdTask = task.IdTaskItem,
                    NoteMnemonic = "NOTE-" + (i + 1).ToString("000"),
                    Note1 = catalog.NoteText(i),
                    DateCreated = now.AddDays(-(i % 10)),
                    DateModified = now,
                    SyncId = Guid.NewGuid(),
                    SyncStatus = 0,
                    ExternalId = "demo-note-" + i
                };
                note.Tag.Add(tags[i % tags.Count]);
                context.Note.Add(note);
            }
        }

        private static void CreateWebLinks(TaskOTimeContext context, IReadOnlyList<User> users, IReadOnlyList<Project> projects, IReadOnlyList<Tag> tags, DateTimeOffset now, DemoTextCatalog catalog)
        {
            var count = Math.Max(4, projects.Count * 2);
            for (var i = 0; i < count; i++)
            {
                var link = new WebLink
                {
                    IdWebLink = Guid.NewGuid(),
                    IdUser = users[i % users.Count].IdUser,
                    IdProject = projects[i % projects.Count].IdProject,
                    Link = "https://example.invalid/taskotime/demo/" + (i + 1),
                    Domain = "example.invalid",
                    Description = catalog.WebLinkDescription(i),
                    DateCreated = now.AddDays(-(i % 12)),
                    DateModified = now,
                    SyncId = Guid.NewGuid(),
                    SyncStatus = 0,
                    ExternalId = "demo-link-" + i
                };
                link.Tag.Add(tags[i % tags.Count]);
                context.WebLink.Add(link);
            }
        }

        private static void CreateSharableProjects(TaskOTimeContext context, IReadOnlyList<User> users, IReadOnlyList<Project> projects, IReadOnlyList<Category> categories, DateTimeOffset now)
        {
            if (users.Count < 2)
            {
                return;
            }

            for (var i = 0; i < projects.Count; i++)
            {
                var tenantUsers = users.Where(u => u.IdTenant == projects[i].IdTenant && u.IdUser != projects[i].IdUser).ToList();
                if (tenantUsers.Count == 0)
                {
                    continue;
                }

                var sharedUser = tenantUsers[i % tenantUsers.Count];
                var sharable = new SharableProject
                {
                    IdSharableProject = Guid.NewGuid(),
                    IdProject = projects[i].IdProject,
                    IdUser = sharedUser.IdUser,
                    IdDefaultCategory = categories[i % categories.Count].IdCategory,
                    ProjectSymbolColor = i % 12,
                    IsValid = true,
                    IsActive = true,
                    IsDeleted = false,
                    CurrentRelationStatus = 1,
                    AllowOthersToSeeMyTasksAndTimes = true,
                    AllowOthersToAssignTasksToMe = true,
                    AllowOthersToDoMyReporting = false,
                    AllowCoOwnerToDoMyReporting = false,
                    AllowCoOwnerToEditMyTimes = false,
                    AllowGeoFencingForTimeRecordings = false,
                    AllowGeoFencingForTasks = false,
                    IsTasksCoOwner = false,
                    IsCoOwner = false,
                    DateCreated = now,
                    DateModified = now,
                    SyncId = Guid.NewGuid(),
                    SyncStatus = 0,
                    ExternalId = "demo-share-" + i
                };

                context.SharableProject.Add(sharable);
            }
        }

        private static void CreateTenantLeads(TaskOTimeContext context, IReadOnlyList<User> users, int count, DateTimeOffset now, DemoTextCatalog catalog)
        {
            for (var i = 0; i < count; i++)
            {
                context.TenantLead.Add(new TenantLead
                {
                    IdTenantLead = Guid.NewGuid(),
                    IdAssignedToUser = users[i % users.Count].IdUser,
                    TenantLeadName = catalog.TenantLeadName(i),
                    FirstName = catalog.TenantLeadFirstName(i),
                    LastName = catalog.TenantLeadLastName(i),
                    EMail = "tenant" + (i + 1) + "@example.invalid",
                    Notes = catalog.TenantLeadNotes(i),
                    IsConverted = false,
                    IsDeleted = false,
                    DateCreated = now,
                    DateModified = now
                });
            }
        }

        private static void CreateLogItems(TaskOTimeContext context, IReadOnlyList<User> users, DateTimeOffset now, DemoTextCatalog catalog)
        {
            for (var i = 0; i < users.Count * 3; i++)
            {
                context.LogItem.Add(new LogItem
                {
                    IdLogItem = Guid.NewGuid(),
                    IdUser = users[i % users.Count].IdUser,
                    Category = i % 3,
                    Message = catalog.LogMessage(i),
                    LocalLogTime = now.AddMinutes(-i * 15)
                });
            }
        }

        private static string CreateSalt()
        {
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            return Convert.ToBase64String(bytes);
        }

        private static string HashPassword(string password, string salt)
        {
            var saltBytes = Convert.FromBase64String(salt);
            using (var deriveBytes = new Rfc2898DeriveBytes(password, saltBytes, 10000))
            {
                return Convert.ToBase64String(deriveBytes.GetBytes(32));
            }
        }
    }
}
