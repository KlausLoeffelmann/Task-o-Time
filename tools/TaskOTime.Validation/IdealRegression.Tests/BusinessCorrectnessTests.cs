using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TaskOTime.AppServer.Models;
using TaskOTime.TimeTrackingServices.Tests.Doubles;
using TaskOTime.ViewModel.ViewModels;

#if VALIDATION_MAIN_DATA_API && VALIDATION_MASTER_DATA_API
#error Select exactly one validation data API.
#elif VALIDATION_MAIN_DATA_API
using ValidationDataQuery = TaskOTime.AppServer.Models.MainDataQueryRequest;
#elif VALIDATION_MASTER_DATA_API
using ValidationDataQuery = TaskOTime.AppServer.Models.MasterDataQueryRequest;
#else
#error ValidationDataApi must explicitly resolve to MasterData or MainData.
#endif

namespace TaskOTime.Validation
{
    // These assert correctness, never the legacy defect. Baseline failures are
    // evidence for stage comparison, not a reason to invert the ideal assertions.
    [TestClass]
    public sealed class BusinessCorrectnessTests
    {
        [TestMethod]
        public void NewBookingPersistsTheSelectedNonFirstProject()
        {
            var fixture = new Workspace();
            var selected = fixture.Times.Projects.Last();
            Assert.AreNotEqual(fixture.Times.Projects.First().IdProject, selected.IdProject);
            fixture.Times.SelectedProject = selected;
            TimeEntryEditRequestEventArgs request = null;
            EventHandler<TimeEntryEditRequestEventArgs> capture = (_, args) => request = args;
            fixture.Times.TimeEntryEditRequested += capture;
            fixture.Times.AddCommand.Execute(null);
            fixture.Times.TimeEntryEditRequested -= capture;
            Assert.IsNotNull(request);
            request.SaveAction(fixture.Now, "Project selection probe", "", false);
            Assert.AreEqual(selected.IdProject, fixture.Services.GetBookingDay(new GetBookingDayRequest
            {
                AccessContext = fixture.Access, BookingDate = Workspace.Day
            }).Value.Items.Single().IdProject);
        }

        [DataTestMethod]
        [DataRow(false, 90.0)]
        [DataRow(true, 90.0)]
        [DataRow(false, 1530.5)]
        [DataRow(true, 1530.5)]
        public void TaskCompletionRetainsTheFullElapsedInterval(bool recorded, double minutes)
        {
            var fixture = new Workspace();
            var expected = TimeSpan.FromMinutes(minutes);
            if (recorded)
            {
                fixture.Tasks.StartTaskCommand.Execute(null);
                fixture.Now += expected;
                fixture.Main.UpdateRecordingClock(fixture.Now);
            }
            else
            {
                fixture.Main.ManualCompletionStartText = "08:00";
                fixture.Main.ManualCompletionDurationText = expected.ToString("c");
            }
            fixture.Tasks.CompleteTaskCommand.Execute(null);
            var total = TimeSpan.Zero;
            for (var date = Workspace.Day; date <= Workspace.Day.AddHours(8).Add(expected).Date; date = date.AddDays(1))
            {
                total += fixture.Services.GetBookingDay(new GetBookingDayRequest
                {
                    AccessContext = fixture.Access, BookingDate = date
                }).Value.TotalBookedTime;
            }
            Assert.IsTrue(fixture.Tasks.SelectedTaskItem.IsDone);
            Assert.AreEqual(expected, total,
                "Completion must retain hours, days and fractional minutes, not TimeSpan.Minutes.");
        }

        private sealed class Workspace
        {
            internal static readonly DateTime Day = new DateTime(2026, 7, 1);
            internal readonly TestApplicationServices Services = new TestApplicationServices();
            internal readonly TimeBookingAccessContextDto Access;
            internal readonly TimeCollectionViewModel Times;
            internal readonly TaskManagementViewModel Tasks;
            internal readonly VmMain Main;
            internal DateTime Now = Day.AddHours(8);

            internal Workspace()
            {
                Access = new TimeBookingAccessContextDto
                {
                    IdTenant = Services.Tenant.IdTenant,
                    IdActingUser = Services.ActingUserId,
                    IdBookingUser = Services.ActingUserId
                };
                var query = new ValidationDataQuery { IdTenant = Access.IdTenant, IdActingUser = Access.IdActingUser };
                Times = new TimeCollectionViewModel(Day, Services, Access,
                    Services.GetProjects(query).Value, Services.CategoryId, () => Now, Services.GetCategories(query).Value);
                var task = new TaskItemViewModel("Validation task", "", "")
                {
                    IdTask = Guid.NewGuid(), IdProject = Services.ProjectId
                };
                Tasks = new TaskManagementViewModel(
                    new[] { new TaskListViewModel("Validation list", "", new[] { task }) }, () => Now);
                Main = new VmMain(Times, Tasks);
            }
        }
    }
}
