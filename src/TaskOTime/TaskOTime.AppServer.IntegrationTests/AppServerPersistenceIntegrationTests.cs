using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Security;
using TaskOTime.AppServer.Services;
using TaskOTime.AppServer.TimeBooking;
using TaskOTime.DataLayer;
using TaskOTime.DTOs;
using TaskOTime.ViewModel;
using TaskOTime.ViewModel.ViewModels;

namespace TaskOTime.AppServer.IntegrationTests
{
    [TestClass]
    [TestCategory("IsolatedSql")]
    public sealed class AppServerPersistenceIntegrationTests
    {
        private LocalDbPersistenceTestDatabase database;
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
        public void CreateOwnedDatabase()
        {
            database = new LocalDbPersistenceTestDatabase();
            try
            {
                database.Create();
                SeedTenantAndAdmin();
            }
            catch
            {
                database.Dispose();
                throw;
            }
        }

        [TestCleanup]
        public void CleanupOwnedDatabase() => database?.Dispose();

        [DataTestMethod]
        [DataRow(90.0125)]
        [DataRow(1440.0)]
        [DataRow(1530.5125)]
        public void SelectedProjectAndCrossDayInterval_ReloadExactlyAndRejectInvalidMutation(double minutes)
        {
            var hasher = new Pbkdf2PasswordHasher();
            SeedProjectCategoryTaskForBookings(hasher);
            using (var context = database.CreateContext())
            {
                SystemTimeMarkerSeed.EnsureCategories(context, AdminUserId);
                context.SaveChanges();
            }
            var secondProjectId = GuidFromSuffix(501);
            var admin = new AdminMasterDataService(database.CreateContext, hasher);
            AssertSucceeded(admin.CreateProject(new SaveProjectRequest
            {
                IdTenant = TenantId, IdActingUser = AdminUserId,
                Item = new ProjectMainDataDto
                {
                    IdProject = secondProjectId, ProjectName = "Second project", IsActive = true
                }
            }));
            var service = new TimeBookingService(database.CreateContext, hasher);
            var duration = TimeSpan.FromMinutes(minutes);
            var firstId = GuidFromSuffix(502);
            var stopId = GuidFromSuffix(503);
            var first = CreateBooking(firstId, WorkDayStart, "Selected project interval");
            first.Item.IdProject = secondProjectId;
            first.Item.IdTask = null;
            first.Item.BookingDate = WorkDayStart.Date;
            AssertSucceeded(service.AddTimeBooking(first));
            var stop = CreateBooking(stopId, WorkDayStart.Add(duration), "Exact completion");
            stop.Item.IdProject = secondProjectId;
            stop.Item.IdTask = null;
            stop.Item.BookingDate = WorkDayStart.Date;
            stop.Item.MarkerKind = SystemTimeMarkerKind.StopMark;
            stop.Item.IdCategory = SystemTimeMarkerIds.StopMarkCategoryId;
            var mutation = AssertSucceeded(service.AddTimeBooking(stop));
            Assert.AreEqual(duration, mutation.BookingDay.TotalBookedTime);
            Assert.AreEqual(duration, mutation.AffectedItem.DurationToPrevious);

            var dayRequest = new GetBookingDayRequest { AccessContext = CreateAccessContext(), BookingDate = WorkDayStart.Date };
            var reloaded = AssertSucceeded(service.GetBookingDay(dayRequest));
            Assert.AreEqual(duration, reloaded.TotalBookedTime);
            Assert.AreEqual(WorkDayStart.Add(duration), reloaded.LastBookingAt);
            Assert.IsTrue(reloaded.Items.All(item => item.IdProject == secondProjectId && item.BookingDate == WorkDayStart.Date));
            AssertPersistedTimeline(new[] { firstId, stopId }, new long?[] { duration.Ticks, null });
            var analysis = AssertSucceeded(new AnalysisService(database.CreateContext, hasher)
                .GetCurrentUserDayProjectHours(new UserAnalysisRequest
                {
                    IdTenant = TenantId, IdActingUser = AdminUserId, IdUser = AdminUserId, ReferenceDate = WorkDayStart
                }));
            Assert.AreEqual(duration, analysis.TotalBookedTime);
            Assert.AreEqual(secondProjectId, analysis.Lines.Single().IdProject);

            var duplicate = CreateBooking(GuidFromSuffix(504), stop.Item.EventTime.Value, "Rejected duplicate");
            duplicate.Item.IdProject = secondProjectId;
            duplicate.Item.IdTask = null;
            duplicate.Item.BookingDate = WorkDayStart.Date;
            Assert.IsFalse(service.AddTimeBooking(duplicate).Success);
            Assert.AreEqual(2, AssertSucceeded(service.GetBookingDay(dayRequest)).Items.Count);
            Assert.AreEqual(duration, AssertSucceeded(service.GetBookingDay(dayRequest)).TotalBookedTime);
            AssertPersistedTimeline(new[] { firstId, stopId }, new long?[] { duration.Ticks, null });
        }

        [TestMethod]
        public void TenantEditor_SaveAndReactivate_ReloadsAuthoritativeTenantFromSql()
        {
            var hasher = new Pbkdf2PasswordHasher();
            var admin = new AdminMainDataService(database.CreateContext, hasher);
            var users = new UserAdministrationService(database.CreateContext, hasher);
            var bookings = new TimeBookingService(database.CreateContext, hasher);
            var query = new GetTenantRequest { IdTenant = TenantId, IdActingUser = AdminUserId };
            using (var context = database.CreateContext())
            {
                var entity = context.Tenant.Single();
                entity.Description = "Keep description";
                entity.ExternalId = "Keep external id";
                context.SaveChanges();
            }
            var original = AssertSucceeded(admin.GetTenant(query));
            var workspace = new ServiceWorkspace(original, AdminUserId, admin, users, bookings);
            var interaction = new TenantTestInteraction();
            var vm = new TenantUserViewModel(workspace, interaction)
            {
                TenantName = "  Saved tenant  ", TenantActive = false
            };
            vm.SaveTenantCommand.Execute(null);
            Assert.AreEqual("Tenant saved.", interaction.Message);
            Assert.AreEqual("Integration tenant", original.TenantName, "The edit must not mutate the original DTO.");
            Assert.AreSame(vm.SelectedTenant, workspace.Tenant);
            Assert.AreEqual("Saved tenant", vm.TenantName);
            Assert.IsFalse(workspace.Tenant.IsActive);

            using (var context = database.CreateContext())
            {
                var persisted = context.Tenant.Single();
                Assert.AreEqual("Saved tenant", persisted.TenantName);
                Assert.IsFalse(persisted.IsActive);
                Assert.AreEqual("integration", persisted.TenantIdentifier);
                Assert.AreEqual("Keep description", persisted.Description);
                Assert.AreEqual("Keep external id", persisted.ExternalId);
                Assert.AreEqual(original.DateCreated, persisted.DateCreated);
                Assert.IsTrue(persisted.DateModified >= original.DateModified);
                Assert.IsFalse(persisted.IsDeleted);
            }
            Assert.IsFalse(AssertSucceeded(admin.GetTenant(query)).IsActive);
            Assert.IsTrue(vm.SaveTenantCommand.CanExecute(null), "The same active admin can reactivate the tenant.");
            vm.TenantActive = true;
            vm.SaveTenantCommand.Execute(null);
            var reloaded = new ServiceWorkspace(AssertSucceeded(admin.GetTenant(query)), AdminUserId, admin, users, bookings);
            var reopenedEditor = new TenantUserViewModel(reloaded, interaction);
            Assert.AreEqual("Saved tenant", reopenedEditor.TenantName);
            Assert.IsTrue(reopenedEditor.TenantActive);
            Assert.AreEqual(TenantId, reopenedEditor.SelectedTenant.IdTenant);
        }

        [DataTestMethod]
        [DataRow("missing-user", "NotAuthorized")]
        [DataRow("regular-user", "NotAuthorized")]
        [DataRow("inactive-user", "NotAuthorized")]
        [DataRow("deleted-user", "NotAuthorized")]
        [DataRow("foreign-tenant", "NotAuthorized")]
        [DataRow("deleted-tenant", "TenantNotFound")]
        [DataRow("empty-user", "InvalidRequest")]
        [DataRow("missing-tenant", "TenantNotFound")]
        public void TenantUpdate_RejectsUnauthorizedRequestsWithoutChangingSql(string scenario, string expectedError)
        {
            var request = new UpdateTenantRequest
            {
                IdTenant = TenantId, IdActingUser = AdminUserId, TenantName = "Forbidden update", IsActive = false
            };
            using (var context = database.CreateContext())
            {
                var user = context.User.Single();
                switch (scenario)
                {
                    case "missing-user": request.IdActingUser = Guid.NewGuid(); break;
                    case "regular-user": user.IsAdmin = false; break;
                    case "inactive-user": user.IsActive = false; break;
                    case "deleted-user": user.IsDeleted = true; break;
                    case "deleted-tenant": context.Tenant.Single().IsDeleted = true; break;
                    case "empty-user": request.IdActingUser = Guid.Empty; break;
                    case "missing-tenant": request.IdTenant = Guid.NewGuid(); break;
                    case "foreign-tenant":
                        request.IdTenant = Guid.NewGuid();
                        context.Tenant.Add(new Tenant
                        {
                            IdTenant = request.IdTenant, TenantName = "Other tenant", IsActive = true,
                            DateCreated = WorkDayStart, DateModified = WorkDayStart
                        });
                        break;
                }
                context.SaveChanges();
            }
            var service = new AdminMainDataService(database.CreateContext, new Pbkdf2PasswordHasher());
            var result = service.UpdateTenant(request);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(expectedError, result.ErrorCode);
            var readResult = service.GetTenant(new GetTenantRequest
            {
                IdTenant = request.IdTenant, IdActingUser = request.IdActingUser
            });
            Assert.IsFalse(readResult.Success);
            Assert.AreEqual(expectedError, readResult.ErrorCode);
            using (var context = database.CreateContext())
            {
                Assert.AreEqual("Integration tenant", context.Tenant.Single(t => t.IdTenant == TenantId).TenantName);
                Assert.IsTrue(context.Tenant.All(t => t.IsActive));
                Assert.IsFalse(context.Tenant.Any(t => t.TenantName == "Forbidden update"));
            }
        }

        [TestMethod]
        public void TenantUpdate_ValidatesNameAndPreservesDatabaseOnFailure()
        {
            var service = new AdminMainDataService(database.CreateContext, new Pbkdf2PasswordHasher());
            Assert.AreEqual("InvalidRequest", service.UpdateTenant(null).ErrorCode);
            Assert.AreEqual("InvalidRequest", service.GetTenant(null).ErrorCode);
            foreach (var name in new[] { null, "", "   ", new string('x', 201) })
            {
                var result = service.UpdateTenant(new UpdateTenantRequest
                {
                    IdTenant = TenantId, IdActingUser = AdminUserId, TenantName = name, IsActive = false
                });
                Assert.IsFalse(result.Success);
                Assert.AreEqual("InvalidRequest", result.ErrorCode);
            }
            using (var context = database.CreateContext())
            {
                context.Tenant.Add(new Tenant
                {
                    IdTenant = Guid.NewGuid(), TenantName = "Already exists", IsActive = true,
                    DateCreated = WorkDayStart, DateModified = WorkDayStart
                });
                context.SaveChanges();
            }
            Assert.AreEqual("TenantExists", service.UpdateTenant(new UpdateTenantRequest
            {
                IdTenant = TenantId, IdActingUser = AdminUserId, TenantName = " Already exists ", IsActive = false
            }).ErrorCode);
            using (var context = database.CreateContext())
            {
                var tenant = context.Tenant.Single(t => t.IdTenant == TenantId);
                Assert.AreEqual("Integration tenant", tenant.TenantName);
                Assert.IsTrue(tenant.IsActive);
            }
            var boundaryName = new string('x', 200);
            Assert.AreEqual(boundaryName, AssertSucceeded(service.UpdateTenant(new UpdateTenantRequest
            {
                IdTenant = TenantId, IdActingUser = AdminUserId, TenantName = boundaryName, IsActive = true
            })).TenantName);
        }

        private sealed class TenantTestInteraction : IMaintenanceInteraction
        {
            public string Message { get; private set; }
            public void Notify(string message, string title) => Message = message;
            public bool Confirm(string message, string title) => true;
        }

        [TestMethod]
        public void MainDataTimeBookingAndAnalysis_RoundTripThroughEf6LocalDb()
        {
            var passwordHasher = new Pbkdf2PasswordHasher();
            var mainData = new AdminMainDataService(database.CreateContext, passwordHasher);

            var project = AssertSucceeded(mainData.CreateProject(new SaveProjectRequest
            {
                IdTenant = TenantId,
                IdActingUser = AdminUserId,
                Item = new ProjectMainDataDto
                {
                    IdProject = ProjectId,
                    ProjectName = "  Persistence Portal  ",
                    ProjectIdentifier = " PERSIST ",
                    IsActive = true
                }
            }));
            Assert.AreEqual("Persistence Portal", project.ProjectName);
            Assert.AreEqual("PERSIST", project.ProjectIdentifier);

            var category = AssertSucceeded(mainData.CreateCategory(new SaveCategoryRequest
            {
                IdTenant = TenantId,
                IdActingUser = AdminUserId,
                Item = new CategoryMainDataDto
                {
                    IdCategory = CategoryId,
                    IdUser = AdminUserId,
                    CategoryName = "Development",
                    DisplayOrder = 1,
                    IsPublic = true
                }
            }));
            Assert.AreEqual(CategoryId, category.IdCategory);

            var taskList = AssertSucceeded(mainData.CreateTaskList(new SaveTaskListRequest
            {
                IdTenant = TenantId,
                IdActingUser = AdminUserId,
                Item = new TaskListMainDataDto
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

            var taskItem = AssertSucceeded(mainData.CreateTaskItem(new SaveTaskItemRequest
            {
                IdTenant = TenantId,
                IdActingUser = AdminUserId,
                Item = new TaskItemMainDataDto
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

            CollectionAssert.AreEqual(new[] { ProjectId }, AssertSucceeded(mainData.GetProjects(Query())).Select(item => item.IdProject).ToArray());
            CollectionAssert.AreEqual(new[] { TaskListId }, AssertSucceeded(mainData.GetTaskLists(Query())).Select(item => item.IdTaskList).ToArray());
            CollectionAssert.AreEqual(new[] { TaskItemId }, AssertSucceeded(mainData.GetTaskItems(Query())).Select(item => item.IdTaskItem).ToArray());

            using (var context = database.CreateContext())
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

            var booking = new TimeBookingService(database.CreateContext, passwordHasher);
            AssertSucceeded(booking.AddTimeBooking(CreateBooking(FirstTimeItemId, WorkDayStart, "Design test seam")));
            AssertSucceeded(booking.AddTimeBooking(CreateBooking(SecondTimeItemId, WorkDayStart.AddMinutes(210), "Review persisted totals")));

            using (var context = database.CreateContext())
            {
                var timeItems = context.TimeItem.OrderBy(item => item.EventTime).ToList();
                Assert.AreEqual(2, timeItems.Count);
                Assert.AreEqual(TimeSpan.FromMinutes(210).Ticks, timeItems[0].DurationTicksToNext);
                Assert.AreEqual(TaskItemId, timeItems[0].IdTask);
                Assert.AreEqual(CategoryId, timeItems[0].IdCategory);
            }

            var analysis = new AnalysisService(database.CreateContext, passwordHasher);
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

        [TestMethod]
        public void TimeBooking_RetrospectiveInsertEditAndDelete_PersistReorderedTimelineAndDeltas()
        {
            var passwordHasher = new Pbkdf2PasswordHasher();
            SeedProjectCategoryTaskForBookings(passwordHasher);

            var booking = new TimeBookingService(database.CreateContext, passwordHasher);
            var firstId = GuidFromSuffix(201);
            var secondId = GuidFromSuffix(202);
            var retrospectiveId = GuidFromSuffix(203);

            AssertSucceeded(booking.AddTimeBooking(CreateBooking(firstId, WorkDayStart, "Morgens begonnen")));
            AssertSucceeded(booking.AddTimeBooking(CreateBooking(secondId, WorkDayStart.AddHours(3), "Mittags weiter")));
            AssertPersistedTimeline(
                new[] { firstId, secondId },
                new[] { TimeSpan.FromHours(3).Ticks, (long?)null });

            AssertSucceeded(booking.AddTimeBooking(CreateBooking(retrospectiveId, WorkDayStart.AddHours(1), "Rückwirkend ergänzt")));
            AssertPersistedTimeline(
                new[] { firstId, retrospectiveId, secondId },
                new[] { TimeSpan.FromHours(1).Ticks, TimeSpan.FromHours(2).Ticks, (long?)null });

            AssertSucceeded(booking.EditTimeBooking(CreateBooking(retrospectiveId, WorkDayStart.AddHours(2), "Rückwirkend verschoben")));
            AssertPersistedTimeline(
                new[] { firstId, retrospectiveId, secondId },
                new[] { TimeSpan.FromHours(2).Ticks, TimeSpan.FromHours(1).Ticks, (long?)null });

            AssertSucceeded(booking.DeleteTimeBooking(new DeleteTimeBookingRequest
            {
                AccessContext = CreateAccessContext(),
                IdTimeItem = retrospectiveId,
                BookingDate = WorkDayStart.Date
            }));
            AssertPersistedTimeline(
                new[] { firstId, secondId },
                new[] { TimeSpan.FromHours(3).Ticks, (long?)null });
        }

        private void SeedTenantAndAdmin()
        {
            var now = DateTimeOffset.UtcNow;
            using (var context = database.CreateContext())
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

        private static MainDataQueryRequest Query()
        {
            return new MainDataQueryRequest
            {
                IdTenant = TenantId,
                IdActingUser = AdminUserId
            };
        }

        private void SeedProjectCategoryTaskForBookings(Pbkdf2PasswordHasher passwordHasher)
        {
            var mainData = new AdminMainDataService(database.CreateContext, passwordHasher);

            AssertSucceeded(mainData.CreateProject(new SaveProjectRequest
            {
                IdTenant = TenantId,
                IdActingUser = AdminUserId,
                Item = new ProjectMainDataDto
                {
                    IdProject = ProjectId,
                    ProjectName = "Timeline Project",
                    ProjectIdentifier = "TIME",
                    IsActive = true
                }
            }));

            AssertSucceeded(mainData.CreateCategory(new SaveCategoryRequest
            {
                IdTenant = TenantId,
                IdActingUser = AdminUserId,
                Item = new CategoryMainDataDto
                {
                    IdCategory = CategoryId,
                    IdUser = AdminUserId,
                    CategoryName = "Timeline",
                    IsPublic = true
                }
            }));

            AssertSucceeded(mainData.CreateTaskList(new SaveTaskListRequest
            {
                IdTenant = TenantId,
                IdActingUser = AdminUserId,
                Item = new TaskListMainDataDto
                {
                    IdTaskList = TaskListId,
                    IdProject = ProjectId,
                    IdUser = AdminUserId,
                    TaskListName = "Timeline List",
                    IsPublic = true
                }
            }));

            AssertSucceeded(mainData.CreateTaskItem(new SaveTaskItemRequest
            {
                IdTenant = TenantId,
                IdActingUser = AdminUserId,
                Item = new TaskItemMainDataDto
                {
                    IdTaskItem = TaskItemId,
                    IdProject = ProjectId,
                    IdTaskList = TaskListId,
                    IdUser = AdminUserId,
                    TaskItemName = "Timeline Task"
                }
            }));
        }

        private static TimeBookingAccessContextDto CreateAccessContext()
        {
            return new TimeBookingAccessContextDto
            {
                IdTenant = TenantId,
                IdActingUser = AdminUserId,
                IdBookingUser = AdminUserId
            };
        }

        private static SaveTimeBookingRequest CreateBooking(Guid idTimeItem, DateTimeOffset eventTime, string description)
        {
            return new SaveTimeBookingRequest
            {
                AccessContext = CreateAccessContext(),
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

        private void AssertPersistedTimeline(Guid[] expectedIds, long?[] expectedTicksToNext)
        {
            using (var context = database.CreateContext())
            {
                var timeItems = context.TimeItem
                    .Where(item => item.IdUser == AdminUserId && item.BookingDate == WorkDayStart.Date)
                    .OrderBy(item => item.EventTime)
                    .ToList();

                CollectionAssert.AreEqual(expectedIds, timeItems.Select(item => item.IdTimeItem).ToArray());
                CollectionAssert.AreEqual(expectedTicksToNext, timeItems.Select(item => item.DurationTicksToNext).ToArray());

                for (var index = 0; index < timeItems.Count; index++)
                {
                    var previous = index == 0 ? (Guid?)null : timeItems[index - 1].IdTimeItem;
                    var next = index == timeItems.Count - 1 ? (Guid?)null : timeItems[index + 1].IdTimeItem;
                    Assert.AreEqual(previous, timeItems[index].IdPreviousItem, "Unexpected previous link at index " + index + ".");
                    Assert.AreEqual(next, timeItems[index].IdNextItem, "Unexpected next link at index " + index + ".");

                    if (expectedTicksToNext[index].HasValue)
                    {
                        var duration = TimeSpan.FromTicks(expectedTicksToNext[index].Value);
                        Assert.AreEqual(duration < TimeSpan.FromDays(1) ? duration : (TimeSpan?)null, timeItems[index].DurationToNext);
                    }
                    else
                    {
                        Assert.IsNull(timeItems[index].DurationToNext);
                    }
                }
            }
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
