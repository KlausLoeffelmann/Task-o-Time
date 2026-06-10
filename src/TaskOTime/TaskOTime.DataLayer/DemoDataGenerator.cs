using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using TaskOTime.DTOs;

namespace TaskOTime.DataLayer
{
    public sealed class DemoDataGenerator
    {
        public void CreateDemoData(DemoDataOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options.Validate();

            using (var context = TaskOTimeContextFactory.Create())
            {
                var handles = options.Users.Select(u => u.Handle).ToArray();
                if (context.User.Any(u => handles.Contains(u.UserIdent)))
                {
                    throw new InvalidOperationException("One or more configured demo users already exist. Use --new before creating a fresh demo dataset.");
                }

                using (var transaction = context.Database.BeginTransaction())
                {
                    var now = DateTimeOffset.Now;
                    var users = CreateUsers(context, options.Users, now);
                    var projects = CreateProjects(context, users, options.Projects, now);
                    var symbols = CreateSymbols(context, projects);
                    var categories = CreateCategories(context, users, symbols, options.Categories);
                    var lists = CreateTaskLists(context, users, projects, symbols, options.Lists);
                    var tags = CreateTags(context, users, options.Tasks.Total);
                    var tasks = CreateTasks(context, users, projects, lists, symbols, tags, options.Tasks, now);
                    CreateTimeItems(context, users, projects, categories, tasks, options.TimeItems, now);
                    CreateNotes(context, users, projects, tasks, tags, now);
                    CreateWebLinks(context, users, projects, tags, now);
                    CreateSharableProjects(context, users, projects, categories, now);
                    CreateTenantLeads(context, users, options.Tenants, now);
                    CreateLogItems(context, users, now);

                    context.SaveChanges();
                    transaction.Commit();
                }
            }
        }

        private static List<User> CreateUsers(TaskOTimeContext context, IEnumerable<DemoUserOptions> userOptions, DateTimeOffset now)
        {
            var users = new List<User>();
            var index = 0;
            foreach (var userOption in userOptions)
            {
                var user = new User
                {
                    IdUser = Guid.NewGuid(),
                    UserIdent = userOption.Handle,
                    FirstName = userOption.FirstName,
                    LastName = userOption.LastName,
                    EMail = userOption.Email,
                    IsAdmin = index == 0,
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

        private static List<Project> CreateProjects(TaskOTimeContext context, IReadOnlyList<User> users, int count, DateTimeOffset now)
        {
            var projects = new List<Project>();
            for (var i = 0; i < count; i++)
            {
                var user = users[i % users.Count];
                var project = new Project
                {
                    IdProject = Guid.NewGuid(),
                    IdUser = user.IdUser,
                    ProjectName = "Demo Project " + (i + 1),
                    ProjectType = i % 3,
                    ProjectNumber = 1000 + i,
                    ProjectDescription = "Generated demo project " + (i + 1),
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

        private static List<Category> CreateCategories(TaskOTimeContext context, IReadOnlyList<User> users, IReadOnlyList<CategorySymbol> symbols, int count)
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
                    CategoryName = "Category " + (i + 1),
                    DisplayOrder = i,
                    IsPublic = i % 4 == 0
                };

                context.Category.Add(category);
                categories.Add(category);
            }

            return categories;
        }

        private static List<TaskList> CreateTaskLists(TaskOTimeContext context, IReadOnlyList<User> users, IReadOnlyList<Project> projects, IReadOnlyList<CategorySymbol> symbols, int count)
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
                    TaskListName = "List " + (i + 1),
                    DisplayOrder = i,
                    IsPublic = i % 2 == 0
                };

                context.TaskList.Add(list);
                lists.Add(list);
            }

            return lists;
        }

        private static List<Tag> CreateTags(TaskOTimeContext context, IReadOnlyList<User> users, int taskCount)
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
                    Tag1 = "demo-tag-" + (i + 1),
                    Description = "Generated demo tag " + (i + 1),
                    DateCreated = DateTimeOffset.Now,
                    DateModified = DateTimeOffset.Now
                };

                context.Tag.Add(tag);
                tags.Add(tag);
            }

            return tags;
        }

        private static List<TaskItem> CreateTasks(TaskOTimeContext context, IReadOnlyList<User> users, IReadOnlyList<Project> projects, IReadOnlyList<TaskList> lists, IReadOnlyList<CategorySymbol> symbols, IReadOnlyList<Tag> tags, DemoTaskOptions options, DateTimeOffset now)
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
                    TaskItemName = (isCompleted ? "Closed" : "Open") + " Demo Task " + (i + 1),
                    TaskItemDescription = "Generated task for demo data.",
                    QuickInfo = "Demo task " + (i + 1),
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

        private static void CreateTimeItems(TaskOTimeContext context, IReadOnlyList<User> users, IReadOnlyList<Project> projects, IReadOnlyList<Category> categories, IReadOnlyList<TaskItem> tasks, int count, DateTimeOffset now)
        {
            for (var i = 0; i < count; i++)
            {
                var task = tasks[i % tasks.Count];
                var duration = TimeSpan.FromMinutes(15 + (i % 8) * 15);
                var item = new TimeItem
                {
                    IdTimeItem = Guid.NewGuid(),
                    IdUser = task.IdUser,
                    IdProject = task.IdProject,
                    IdTask = task.IdTaskItem,
                    IdCategory = categories[i % categories.Count].IdCategory,
                    ShortTitle = "Time " + (i + 1),
                    Description = "Generated demo time item.",
                    EventTime = now.AddHours(-i),
                    BookingDate = DateTime.Today.AddDays(-(i % 30)),
                    EventInfo = "Demo booking",
                    EventTypeInfo = i % 3,
                    DurationToNext = duration,
                    DurationTicksToNext = duration.Ticks,
                    Scope = 0,
                    IsItemCompleted = i % 4 != 0,
                    IsItemDeleted = false,
                    IsStartAction = i % 2 == 0,
                    IsEndAction = i % 2 != 0,
                    Value = (decimal)duration.TotalHours,
                    Priority = i % 4,
                    MachineID = Environment.MachineName,
                    DateItemFinished = i % 4 != 0 ? now.AddHours(-i).Add(duration) : (DateTimeOffset?)null,
                    DateCreated = now.AddDays(-(i % 30)),
                    DateModified = now,
                    SyncId = Guid.NewGuid(),
                    SyncStatus = 0,
                    ExternalId = "demo-time-" + i
                };

                context.TimeItem.Add(item);
            }
        }

        private static void CreateNotes(TaskOTimeContext context, IReadOnlyList<User> users, IReadOnlyList<Project> projects, IReadOnlyList<TaskItem> tasks, IReadOnlyList<Tag> tags, DateTimeOffset now)
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
                    Note1 = "Generated note " + (i + 1),
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

        private static void CreateWebLinks(TaskOTimeContext context, IReadOnlyList<User> users, IReadOnlyList<Project> projects, IReadOnlyList<Tag> tags, DateTimeOffset now)
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
                    Description = "Generated demo link " + (i + 1),
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
                var sharedUser = users[(i + 1) % users.Count];
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

        private static void CreateTenantLeads(TaskOTimeContext context, IReadOnlyList<User> users, int count, DateTimeOffset now)
        {
            for (var i = 0; i < count; i++)
            {
                context.TenantLead.Add(new TenantLead
                {
                    IdTenantLead = Guid.NewGuid(),
                    IdAssignedToUser = users[i % users.Count].IdUser,
                    TenantLeadName = "Demo Tenant " + (i + 1),
                    FirstName = "Tenant",
                    LastName = "Lead " + (i + 1),
                    EMail = "tenant" + (i + 1) + "@example.invalid",
                    IsConverted = false,
                    IsDeleted = false,
                    DateCreated = now,
                    DateModified = now
                });
            }
        }

        private static void CreateLogItems(TaskOTimeContext context, IReadOnlyList<User> users, DateTimeOffset now)
        {
            for (var i = 0; i < users.Count * 3; i++)
            {
                context.LogItem.Add(new LogItem
                {
                    IdLogItem = Guid.NewGuid(),
                    IdUser = users[i % users.Count].IdUser,
                    Category = i % 3,
                    Message = "Generated demo activity " + (i + 1),
                    LocalLogTime = now.AddMinutes(-i * 15)
                });
            }
        }
    }
}
