using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Security;
using TaskOTime.AppServer.Services;
using TaskOTime.DataLayer;
using TaskOTime.DTOs;

namespace TaskOTime.AppServer.IntegrationTests
{
    [TestClass]
    public sealed class AppServerPersistenceIntegrationTests
    {
        private static readonly Guid TenantId = GuidFromSuffix(101);
        private static readonly Guid AdminUserId = GuidFromSuffix(102);
        private static readonly Guid ProjectId = GuidFromSuffix(103);
        private static readonly Guid CategoryId = GuidFromSuffix(104);
        private static readonly Guid TaskListId = GuidFromSuffix(105);
        private static readonly Guid TaskItemId = GuidFromSuffix(106);
        private static readonly Guid FirstTimeItemId = GuidFromSuffix(107);
        private static readonly Guid SecondTimeItemId = GuidFromSuffix(108);
        private static readonly DateTimeOffset WorkDayStart = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

        [TestInitialize]
        public void ResetDatabase()
        {
            LocalDbPersistenceTestDatabase.Reset();
            SeedTenantAndAdmin();
        }

        [TestMethod]
        public void MasterDataTimeBookingAndAnalysis_RoundTripThroughEf6LocalDb()
        {
            var passwordHasher = new Pbkdf2PasswordHasher();
            var masterData = new AdminMasterDataService(LocalDbPersistenceTestDatabase.CreateContext, passwordHasher);

            var project = AssertSucceeded(masterData.CreateProject(new SaveProjectRequest
            {
                IdTenant = TenantId,
                IdActingUser = AdminUserId,
                Item = new ProjectMasterDataDto
                {
                    IdProject = ProjectId,
                    ProjectName = "  Persistence Portal  ",
                    ProjectIdentifier = " PERSIST ",
                    IsActive = true
                }
            }));
            Assert.AreEqual("Persistence Portal", project.ProjectName);
            Assert.AreEqual("PERSIST", project.ProjectIdentifier);

            var category = AssertSucceeded(masterData.CreateCategory(new SaveCategoryRequest
            {
                IdTenant = TenantId,
                IdActingUser = AdminUserId,
                Item = new CategoryMasterDataDto
                {
                    IdCategory = CategoryId,
                    IdUser = AdminUserId,
                    CategoryName = "Development",
                    DisplayOrder = 1,
                    IsPublic = true
                }
            }));
            Assert.AreEqual(CategoryId, category.IdCategory);

            var taskList = AssertSucceeded(masterData.CreateTaskList(new SaveTaskListRequest
            {
                IdTenant = TenantId,
                IdActingUser = AdminUserId,
                Item = new TaskListMasterDataDto
                {
                    IdTaskList = TaskListId,
                    IdProject = ProjectId,
                    IdUser = AdminUserId,
                    TaskListName = "Implementation",
                    DisplayOrder = 10,
                    IsPublic = true
                }
            }));
            Assert.AreEqual(TaskListId, taskList.IdTaskList);

            var taskItem = AssertSucceeded(masterData.CreateTaskItem(new SaveTaskItemRequest
            {
                IdTenant = TenantId,
                IdActingUser = AdminUserId,
                Item = new TaskItemMasterDataDto
                {
                    IdTaskItem = TaskItemId,
                    IdProject = ProjectId,
                    IdTaskList = TaskListId,
                    IdUser = AdminUserId,
                    TaskItemName = "Wire persistence tests",
                    TaskHoursBudget = 4,
                    Priority = 2
                }
            }));
            Assert.AreEqual(TaskItemId, taskItem.IdTaskItem);

            CollectionAssert.AreEqual(new[] { ProjectId }, AssertSucceeded(masterData.GetProjects(Query())).Select(item => item.IdProject).ToArray());
            CollectionAssert.AreEqual(new[] { TaskListId }, AssertSucceeded(masterData.GetTaskLists(Query())).Select(item => item.IdTaskList).ToArray());
            CollectionAssert.AreEqual(new[] { TaskItemId }, AssertSucceeded(masterData.GetTaskItems(Query())).Select(item => item.IdTaskItem).ToArray());

            using (var context = LocalDbPersistenceTestDatabase.CreateContext())
            {
                Assert.AreEqual(1, context.Project.Count(projectRow => projectRow.IdProject == ProjectId));
                Assert.AreEqual(1, context.TaskList.Count(list => list.IdTaskList == TaskListId));
                Assert.AreEqual(1, context.TaskItem.Count(task => task.IdTaskItem == TaskItemId));
                Assert.IsTrue(context.ProjectUserAssignment.Any(assignment =>
                    assignment.IdProject == ProjectId &&
                    assignment.IdUser == AdminUserId &&
                    assignment.CanBookTime &&
                    assignment.CanManageProject));
            }

            var booking = new TimeBookingService(LocalDbPersistenceTestDatabase.CreateContext, passwordHasher);
            AssertSucceeded(booking.AddTimeBooking(CreateBooking(FirstTimeItemId, WorkDayStart, "Design test seam")));
            AssertSucceeded(booking.AddTimeBooking(CreateBooking(SecondTimeItemId, WorkDayStart.AddMinutes(210), "Review persisted totals")));

            using (var context = LocalDbPersistenceTestDatabase.CreateContext())
            {
                var timeItems = context.TimeItem.OrderBy(item => item.EventTime).ToList();
                Assert.AreEqual(2, timeItems.Count);
                Assert.AreEqual(TimeSpan.FromMinutes(210).Ticks, timeItems[0].DurationTicksToNext);
                Assert.AreEqual(TaskItemId, timeItems[0].IdTask);
                Assert.AreEqual(CategoryId, timeItems[0].IdCategory);
            }

            var analysis = new AnalysisService(LocalDbPersistenceTestDatabase.CreateContext, passwordHasher);
            var userHours = AssertSucceeded(analysis.GetCurrentUserDayProjectHours(new UserAnalysisRequest
            {
                IdTenant = TenantId,
                IdActingUser = AdminUserId,
                IdUser = AdminUserId,
                ReferenceDate = WorkDayStart
            }));
            Assert.AreEqual(TimeSpan.FromMinutes(210), userHours.TotalBookedTime);
            Assert.AreEqual(3.5m, userHours.TotalBookedHours);
            Assert.AreEqual(1, userHours.Lines.Count);
            Assert.AreEqual(ProjectId, userHours.Lines[0].IdProject);
            Assert.AreEqual("Persistence Portal", userHours.Lines[0].ProjectName);

            var statement = AssertSucceeded(analysis.GetDailyStatement(new StatementRequest
            {
                IdTenant = TenantId,
                IdActingUser = AdminUserId,
                IdUser = AdminUserId,
                IdProject = ProjectId,
                ReferenceDate = WorkDayStart,
                IncludeNotes = true
            }));
            Assert.AreEqual(TimeSpan.FromMinutes(210), statement.TotalTime);
            Assert.AreEqual(3.5m, statement.TotalHours);
            Assert.AreEqual(1, statement.Lines.Count);
            Assert.AreEqual("Development", statement.Lines[0].CategoryName);
            Assert.AreEqual("Wire persistence tests", statement.Lines[0].TaskItemName);
            Assert.AreEqual("Design test seam", statement.Lines[0].Description);

            var tenantStatistics = AssertSucceeded(analysis.GetTenantAdminStatistics(new TenantAdminStatisticsRequest
            {
                IdTenant = TenantId,
                IdActingUser = AdminUserId,
                ReferenceDate = WorkDayStart
            }));
            Assert.AreEqual(1, tenantStatistics.ActiveProjectCount);
            Assert.AreEqual(1, tenantStatistics.ActiveProjectAssignmentCount);
            Assert.AreEqual(2, tenantStatistics.TimeBookingCount);
            Assert.AreEqual(3.5m, tenantStatistics.CurrentDayBookedHours);
        }

        private static void SeedTenantAndAdmin()
        {
            var now = DateTimeOffset.UtcNow;
            using (var context = LocalDbPersistenceTestDatabase.CreateContext())
            {
                context.Tenant.Add(new Tenant
                {
                    IdTenant = TenantId,
                    TenantName = "Integration tenant",
                    TenantIdentifier = "integration",
                    IsActive = true,
                    IsDeleted = false,
                    DateCreated = now,
                    DateModified = now
                });
                context.User.Add(new User
                {
                    IdUser = AdminUserId,
                    IdTenant = TenantId,
                    UserIdent = "integration-admin",
                    FirstName = "Integration",
                    LastName = "Admin",
                    EMail = "integration-admin@example.test",
                    IsAdmin = true,
                    IsActive = true,
                    IsDeleted = false,
                    MustChangePassword = false,
                    FailedLoginCount = 0,
                    LastLogin = now,
                    DateCreated = now,
                    DateModified = now,
                    SyncId = Guid.NewGuid(),
                    SyncStatus = 0
                });
                context.SaveChanges();
            }
        }

        private static MasterDataQueryRequest Query()
        {
            return new MasterDataQueryRequest
            {
                IdTenant = TenantId,
                IdActingUser = AdminUserId
            };
        }

        private static SaveTimeBookingRequest CreateBooking(Guid idTimeItem, DateTimeOffset eventTime, string description)
        {
            return new SaveTimeBookingRequest
            {
                AccessContext = new TimeBookingAccessContextDto
                {
                    IdTenant = TenantId,
                    IdActingUser = AdminUserId,
                    IdBookingUser = AdminUserId
                },
                Item = new TimeBookingItemDto
                {
                    IdTimeItem = idTimeItem,
                    IdProject = ProjectId,
                    IdTask = TaskItemId,
                    IdCategory = CategoryId,
                    EventTime = eventTime,
                    ShortTitle = description,
                    Description = description
                }
            };
        }

        private static T AssertSucceeded<T>(ServiceResult<T> result)
        {
            Assert.IsTrue(result.Success, result.ErrorCode + ": " + result.ErrorMessage);
            return result.Value;
        }

        private static Guid GuidFromSuffix(int suffix)
        {
            return new Guid(string.Format("00000000-0000-0000-0000-{0:000000000000}", suffix));
        }
    }
}
