using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Security;
using TaskOTime.AppServer.Services;
using TaskOTime.AppServer.TimeBooking;
using TaskOTime.DataLayer;
using TaskOTime.DTOs;

namespace TaskOTime.AppServer.Tests
{
    [TestClass]
    public class AppServerServiceTests
    {
        private static readonly Guid TenantId = GuidFromSuffix(1);
        private static readonly Guid AdminUserId = GuidFromSuffix(2);
        private static readonly Guid RegularUserId = GuidFromSuffix(3);
        private static readonly Guid OtherUserId = GuidFromSuffix(4);
        private static readonly Guid ProjectId = GuidFromSuffix(5);
        private static readonly Guid CategoryId = GuidFromSuffix(6);
        private static readonly Guid TimeItemId = GuidFromSuffix(7);
        private static readonly DateTimeOffset WorkDayStart = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

        [TestMethod]
        public void AdminMasterData_GetProjects_RejectsNonAdminActingUser()
        {
            var context = CreateTenantContext();
            var service = new AdminMasterDataService(() => context, new Pbkdf2PasswordHasher());

            var result = service.GetProjects(new MasterDataQueryRequest
            {
                IdTenant = TenantId,
                IdActingUser = RegularUserId
            });

            Assert.IsFalse(result.Success);
            Assert.AreEqual("NotAuthorized", result.ErrorCode);
        }

        [TestMethod]
        public void AdminMasterData_CreateAndUpdateProject_PersistsProjectAndOwnerAssignment()
        {
            var context = CreateTenantContext();
            var service = new AdminMasterDataService(() => context, new Pbkdf2PasswordHasher());

            var createResult = service.CreateProject(new SaveProjectRequest
            {
                IdTenant = TenantId,
                IdActingUser = AdminUserId,
                Item = new ProjectMasterDataDto
                {
                    IdProject = ProjectId,
                    ProjectName = "  Alpha migration  ",
                    ProjectIdentifier = " ALPHA ",
                    IsActive = true
                }
            });

            Assert.IsTrue(createResult.Success, createResult.ErrorMessage);
            Assert.AreEqual("Alpha migration", createResult.Value.ProjectName);
            Assert.AreEqual(ProjectId, createResult.Value.IdProject);
            Assert.AreEqual("ALPHA", context.Project.Single().ProjectIdentifier);
            var ownerAssignment = context.ProjectUserAssignment.Single();
            Assert.AreEqual(ProjectId, ownerAssignment.IdProject);
            Assert.AreEqual(AdminUserId, ownerAssignment.IdUser);
            Assert.IsTrue(ownerAssignment.CanBookTime);
            Assert.IsTrue(ownerAssignment.CanManageProject);

            var updateResult = service.UpdateProject(new SaveProjectRequest
            {
                IdTenant = TenantId,
                IdActingUser = AdminUserId,
                Item = new ProjectMasterDataDto
                {
                    IdProject = ProjectId,
                    IdUser = AdminUserId,
                    ProjectName = "Beta launch",
                    ProjectIdentifier = "BETA",
                    IsActive = true
                }
            });

            Assert.IsTrue(updateResult.Success, updateResult.ErrorMessage);
            Assert.AreEqual("Beta launch", context.Project.Single().ProjectName);
            Assert.AreEqual("BETA", updateResult.Value.ProjectIdentifier);
            Assert.AreEqual(1, context.ProjectUserAssignment.Count());
        }

        [TestMethod]
        public void TimeBooking_AddTimeBooking_PersistsBookingAndRequiresBookableAssignment()
        {
            var context = CreateTenantContext();
            AddProjectWithAssignment(context, RegularUserId, canBookTime: true);
            var service = new TimeBookingService(() => context, new Pbkdf2PasswordHasher());

            var result = service.AddTimeBooking(new SaveTimeBookingRequest
            {
                AccessContext = CreateAccessContext(RegularUserId, RegularUserId),
                Item = new TimeBookingItemDto
                {
                    IdTimeItem = TimeItemId,
                    IdProject = ProjectId,
                    IdCategory = CategoryId,
                    EventTime = WorkDayStart,
                    ShortTitle = "Implement service tests"
                }
            });

            Assert.IsTrue(result.Success, result.ErrorMessage);
            var persisted = context.TimeItem.Single();
            Assert.AreEqual(TimeItemId, persisted.IdTimeItem);
            Assert.AreEqual(RegularUserId, persisted.IdUser);
            Assert.AreEqual(WorkDayStart.Date, persisted.BookingDate);
            Assert.AreEqual("Implement service tests", persisted.ShortTitle);
            Assert.AreEqual(1, result.Value.BookingDay.Items.Count);

            var forbiddenContext = CreateTenantContext();
            AddProjectWithAssignment(forbiddenContext, RegularUserId, canBookTime: false);
            var forbiddenService = new TimeBookingService(() => forbiddenContext, new Pbkdf2PasswordHasher());

            var forbiddenResult = forbiddenService.AddTimeBooking(new SaveTimeBookingRequest
            {
                AccessContext = CreateAccessContext(RegularUserId, RegularUserId),
                Item = new TimeBookingItemDto
                {
                    IdTimeItem = GuidFromSuffix(8),
                    IdProject = ProjectId,
                    IdCategory = CategoryId,
                    EventTime = WorkDayStart
                }
            });

            Assert.IsFalse(forbiddenResult.Success);
            Assert.AreEqual("ProjectAssignmentMissing", forbiddenResult.ErrorCode);
            Assert.AreEqual(0, forbiddenContext.TimeItem.Count());
        }

        [TestMethod]
        public void Analysis_CurrentUserDayProjectHours_AggregatesDeterministicDurations()
        {
            var context = CreateTenantContext();
            AddProjectWithAssignment(context, RegularUserId, canBookTime: true);
            context.Category.Add(new Category
            {
                IdCategory = CategoryId,
                IdUser = RegularUserId,
                CategoryName = "Development"
            });
            context.TimeItem.Add(CreateTimeItem(10, TimeSpan.FromHours(2)));
            context.TimeItem.Add(CreateTimeItem(11, TimeSpan.FromMinutes(90)));
            context.TimeItem.Add(new TimeItem
            {
                IdTimeItem = GuidFromSuffix(12),
                IdUser = RegularUserId,
                IdProject = ProjectId,
                IdCategory = LegacySystemTimeMarkerIds.WorkBreak,
                EventTypeInfo = (int)TimeBookingEventType.Time,
                BookingDate = WorkDayStart.Date,
                EventTime = WorkDayStart.AddHours(4),
                DurationTicksToNext = TimeSpan.FromHours(1).Ticks
            });
            var service = new AnalysisService(() => context, new Pbkdf2PasswordHasher());

            var result = service.GetCurrentUserDayProjectHours(new UserAnalysisRequest
            {
                IdTenant = TenantId,
                IdActingUser = RegularUserId,
                IdUser = RegularUserId,
                ReferenceDate = WorkDayStart
            });

            Assert.IsTrue(result.Success, result.ErrorMessage);
            Assert.AreEqual(TimeSpan.FromMinutes(210), result.Value.TotalBookedTime);
            Assert.AreEqual(3.5m, result.Value.TotalBookedHours);
            var line = result.Value.Lines.Single();
            Assert.AreEqual(ProjectId, line.IdProject);
            Assert.AreEqual("Client portal", line.ProjectName);
            Assert.AreEqual(TimeSpan.FromMinutes(210), line.BookedTime);
            Assert.AreEqual(2, line.BookingCount);
        }

        [TestMethod]
        public void TimeBooking_InsertSystemMarkers_SeedsStableMarkerCategories()
        {
            var context = CreateTenantContext();
            AddProjectWithAssignment(context, RegularUserId, canBookTime: true);
            context.Category.Add(new Category
            {
                IdCategory = CategoryId,
                IdUser = RegularUserId,
                CategoryName = "Development"
            });
            context.TimeItem.Add(CreateTimeItem(10, TimeSpan.FromMinutes(30)));
            var service = new TimeBookingService(() => context, new Pbkdf2PasswordHasher());
            var accessContext = CreateAccessContext(RegularUserId, RegularUserId);

            var breakResult = service.InsertWorkBreak(new InsertSystemTimeMarkerRequest
            {
                AccessContext = accessContext,
                MarkerTime = WorkDayStart.AddMinutes(45)
            });
            var stopResult = service.InsertStopMark(new InsertSystemTimeMarkerRequest
            {
                AccessContext = accessContext,
                MarkerTime = WorkDayStart.AddMinutes(60)
            });

            Assert.IsTrue(breakResult.Success, breakResult.ErrorMessage);
            Assert.IsTrue(stopResult.Success, stopResult.ErrorMessage);
            Assert.AreEqual(SystemTimeMarkerIds.WorkBreakCategoryId, breakResult.Value.AffectedItem.IdCategory);
            Assert.AreEqual(SystemTimeMarkerKind.WorkBreak, breakResult.Value.AffectedItem.MarkerKind);
            Assert.AreEqual(SystemTimeMarkerIds.StopMarkCategoryId, stopResult.Value.AffectedItem.IdCategory);
            Assert.AreEqual(SystemTimeMarkerKind.StopMark, stopResult.Value.AffectedItem.MarkerKind);
            Assert.IsTrue(SystemTimeMarkerSeed.HasSeededLookupItems(context));
            Assert.IsTrue(SystemTimeMarkerSeed.HasSeededCategories(context));
            Assert.AreEqual(1, context.Category.Count(category => category.IdCategory == SystemTimeMarkerIds.WorkBreakCategoryId));
            Assert.AreEqual(1, context.Category.Count(category => category.IdCategory == SystemTimeMarkerIds.StopMarkCategoryId));
        }

        private static TaskOTimeContext CreateTenantContext()
        {
            var context = new TaskOTimeContext();
            var now = DateTimeOffset.UtcNow;
            context.Tenant.Add(new Tenant
            {
                IdTenant = TenantId,
                TenantName = "Test tenant",
                IsActive = true,
                DateCreated = now,
                DateModified = now
            });
            context.User.Add(CreateUser(AdminUserId, isAdmin: true));
            context.User.Add(CreateUser(RegularUserId, isAdmin: false));
            context.User.Add(CreateUser(OtherUserId, isAdmin: false));
            return context;
        }

        private static User CreateUser(Guid idUser, bool isAdmin)
        {
            var now = DateTimeOffset.UtcNow;
            return new User
            {
                IdUser = idUser,
                IdTenant = TenantId,
                UserIdent = idUser == AdminUserId ? "admin" : "user",
                FirstName = idUser == RegularUserId ? "Riley" : null,
                LastName = idUser == RegularUserId ? "Tester" : null,
                EMail = "user@example.test",
                IsAdmin = isAdmin,
                IsActive = true,
                DateCreated = now,
                DateModified = now,
                LastLogin = now,
                SyncId = Guid.NewGuid()
            };
        }

        private static void AddProjectWithAssignment(TaskOTimeContext context, Guid idUser, bool canBookTime)
        {
            var now = DateTimeOffset.UtcNow;
            context.Project.Add(new Project
            {
                IdProject = ProjectId,
                IdTenant = TenantId,
                IdUser = AdminUserId,
                ProjectName = "Client portal",
                ProjectIdentifier = "PORTAL",
                IsActive = true,
                DateCreated = now,
                DateModified = now,
                SyncId = Guid.NewGuid()
            });
            context.ProjectUserAssignment.Add(new ProjectUserAssignment
            {
                IdProjectUserAssignment = Guid.NewGuid(),
                IdProject = ProjectId,
                IdUser = idUser,
                IdAssignedByUser = AdminUserId,
                CanBookTime = canBookTime,
                CanManageProject = true,
                CanManageTasks = true,
                IsActive = true,
                DateAssigned = now,
                DateCreated = now,
                DateModified = now
            });
        }

        private static TimeBookingAccessContextDto CreateAccessContext(Guid actingUserId, Guid bookingUserId)
        {
            return new TimeBookingAccessContextDto
            {
                IdTenant = TenantId,
                IdActingUser = actingUserId,
                IdBookingUser = bookingUserId
            };
        }

        private static TimeItem CreateTimeItem(int suffix, TimeSpan duration)
        {
            return new TimeItem
            {
                IdTimeItem = GuidFromSuffix(suffix),
                IdUser = RegularUserId,
                IdProject = ProjectId,
                IdCategory = CategoryId,
                EventTypeInfo = (int)TimeBookingEventType.Time,
                BookingDate = WorkDayStart.Date,
                EventTime = WorkDayStart.AddMinutes(suffix),
                DurationTicksToNext = duration.Ticks,
                DurationToNext = duration,
                DateCreated = WorkDayStart,
                DateModified = WorkDayStart,
                SyncId = Guid.NewGuid()
            };
        }

        private static Guid GuidFromSuffix(int suffix)
        {
            return new Guid(string.Format("00000000-0000-0000-0000-{0:000000000000}", suffix));
        }
    }
}
