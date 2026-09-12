Imports System
Imports System.Collections.Specialized
Imports System.Linq
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports TaskOTime.AppServer.Models
Imports TaskOTime.AppServer.TimeBooking
Imports TaskOTime.AppServer.Services
Imports TaskOTime.DTOs
Imports TaskOTime.TimeTrackingServices.Tests.Doubles
Imports TaskOTime.ViewModel.ViewModels

Namespace TaskOTime.TimeTrackingServices.Tests
    <TestClass>
    Public Class RestoredWorkflowTests
        Private Shared ReadOnly BookingDay As New DateTime(2026, 7, 1)

        <TestMethod>
        Public Sub NonFirstProject_AddEditAndReloadRetainTheSelectedProject()
            Dim fixture As New Workspace()
            Dim vm = fixture.Times
            Dim original = vm.TimeItems
            Dim selected = vm.Projects.Last()
            Assert.AreNotEqual(vm.Projects.First().IdProject, selected.IdProject)
            vm.SelectedProject = selected
            SaveDialog(vm, BookingDay.AddHours(9), "Selected project")
            Dim id = fixture.Stored().Single().IdTimeItem
            Assert.AreEqual(selected.IdProject, fixture.Stored().Single().IdProject)
            vm.BookingDate = BookingDay.AddDays(1)
            vm.BookingDate = BookingDay
            SaveDialog(vm, BookingDay.AddHours(10), "Edited project", edit:=True)
            Assert.AreEqual(id, fixture.Stored().Single().IdTimeItem)
            Assert.AreEqual(selected.IdProject, fixture.Stored().Single().IdProject)
            Assert.AreSame(original, vm.TimeItems)
        End Sub

        <DataTestMethod>
        <DataRow(False, 90.0)>
        <DataRow(True, 90.0)>
        <DataRow(False, 1530.5)>
        <DataRow(True, 1530.5)>
        <DataRow(False, 90.0125)>
        <DataRow(True, 90.0125)>
        Public Sub TaskCompletion_PreservesFullDurationProjectAndReload(recorded As Boolean, minutes As Double)
            Dim fixture As New Workspace()
            Dim expected = TimeSpan.FromMinutes(minutes)
            Dim task = fixture.Tasks.SelectedTaskItem
            task.IdProject = fixture.Times.Projects.Last().IdProject
            Dim original = fixture.Times.TimeItems
            If recorded Then
                fixture.Tasks.StartTaskCommand.Execute(Nothing)
                fixture.NowValue = fixture.NowValue.Add(expected)
                fixture.Main.UpdateRecordingClock(fixture.NowValue)
            Else
                fixture.Main.ManualCompletionStartText = "08:00"
                fixture.Main.ManualCompletionDurationText = expected.ToString("c")
            End If
            fixture.Tasks.CompleteTaskCommand.Execute(Nothing)
            Assert.IsTrue(task.IsDone)
            Assert.AreEqual(expected, fixture.Times.BookedTime)
            Dim stored = fixture.Stored()
            Assert.AreEqual(2, stored.Count)
            Assert.AreEqual(BookingDay.AddHours(8).Add(expected), stored.Last().EventTime.Value.DateTime)
            Assert.IsTrue(stored.All(Function(item) item.IdProject = task.IdProject))
            Assert.IsTrue(stored.All(Function(item) item.BookingDate.Value = BookingDay))
            fixture.Times.BookingDate = BookingDay.AddDays(3)
            fixture.Times.BookingDate = BookingDay
            Assert.AreEqual(expected, fixture.Times.BookedTime)
            Assert.AreSame(original, fixture.Times.TimeItems)
        End Sub

        <DataTestMethod>
        <DataRow("pause")>
        <DataRow("downtime")>
        <DataRow("errand")>
        <DataRow("stop")>
        <DataRow("checkout")>
        <DataRow("next")>
        Public Sub LongRecordingBoundary_PreservesActualCrossDayInstant(operation As String)
            Dim fixture As New Workspace()
            Dim expected = TimeSpan.FromMinutes(1530.5125)
            fixture.Tasks.StartTaskCommand.Execute(Nothing)
            fixture.NowValue = fixture.NowValue.Add(expected)
            fixture.Main.SelectedDate = BookingDay.AddDays(1)
            Select Case operation
                Case "pause"
                    fixture.Times.InsertWorkBreakCommand.Execute(Nothing)
                Case "downtime"
                    fixture.Times.InsertDownTimeCommand.Execute(Nothing)
                Case "errand"
                    fixture.Times.InsertErrandCommand.Execute(Nothing)
                Case "stop"
                    fixture.Times.InsertStopMarkCommand.Execute(Nothing)
                Case "checkout"
                    fixture.Times.CheckOutCommand.Execute(Nothing)
                Case "next"
                    SaveDialog(fixture.Times, fixture.NowValue, "Next interval")
            End Select
            Assert.IsFalse(fixture.Tasks.IsTaskRecording)
            Assert.IsFalse(fixture.Tasks.SelectedTaskItem.IsDone)
            Assert.AreEqual(expected, fixture.Times.BookedTime)
            Assert.AreEqual(fixture.NowValue, fixture.Stored().Last().EventTime.Value.DateTime)
            Assert.AreEqual(2, fixture.Stored().Count)
            fixture.Times.BookingDate = BookingDay.AddDays(3)
            fixture.Times.BookingDate = BookingDay
            Assert.AreEqual(expected, fixture.Times.BookedTime)
        End Sub

        <TestMethod>
        Public Sub CheckoutFailure_RollsBackBoundaryAndLeavesRecordingRetryable()
            Dim fixture As New Workspace(True)
            Dim original = fixture.Times.TimeItems
            fixture.Tasks.StartTaskCommand.Execute(Nothing)
            Dim expected = TimeSpan.FromMinutes(1530.5125)
            fixture.NowValue = fixture.NowValue.Add(expected)
            fixture.Faults.RejectNextSave = Function(request) request.Item.MarkerKind = SystemTimeMarkerKind.StopMark
            Assert.ThrowsException(Of InvalidOperationException)(Sub() fixture.Times.CheckOutCommand.Execute(Nothing))
            Assert.AreEqual(0, fixture.Stored().Count)
            Assert.AreEqual(0, original.Count)
            Assert.IsTrue(fixture.Tasks.IsTaskRecording)
            Assert.IsFalse(fixture.Tasks.SelectedTaskItem.IsDone)
            fixture.Times.CheckOutCommand.Execute(Nothing)
            Assert.AreEqual(expected, fixture.Times.BookedTime)
            Assert.IsFalse(fixture.Tasks.IsTaskRecording)
            Assert.AreSame(original, fixture.Times.TimeItems)
        End Sub

        <TestMethod>
        Public Sub TaskSaveFailure_CheckoutRestoresRecordingAndCompletionFlagForRetry()
            Dim rejectSave = True
            Dim attempts = 0
            Dim saved = 0
            Dim fixture As Workspace = Nothing
            fixture = New Workspace(saveTask:=Sub(candidate)
                                                  attempts += 1
                                                  Assert.IsTrue(candidate.IsDone)
                                                  Assert.AreNotSame(fixture.Tasks.SelectedTaskItem, candidate)
                                                  Assert.IsFalse(fixture.Tasks.SelectedTaskItem.IsDone)
                                                  Assert.IsTrue(fixture.Tasks.SelectedTaskItem.IsStarted)
                                                  Assert.AreEqual(fixture.Tasks.SelectedTaskItem.Id, candidate.Id)
                                                  Assert.AreEqual(fixture.Tasks.SelectedTaskItem.IdTask, candidate.IdTask)
                                                  Assert.AreEqual(fixture.Tasks.SelectedTaskItem.IdProject, candidate.IdProject)
                                                  Assert.AreEqual(fixture.Tasks.SelectedTaskItem.Title, candidate.Title)
                                                  Assert.AreEqual(fixture.Tasks.SelectedTaskItem.Description, candidate.Description)
                                                  Assert.AreEqual(fixture.Tasks.SelectedTaskItem.DueText, candidate.DueText)
                                                  If rejectSave Then Throw New InvalidOperationException("Task save rejected")
                                                  saved += 1
                                              End Sub)
            Dim task = fixture.Tasks.SelectedTaskItem
            Dim original = fixture.Times.TimeItems
            fixture.Tasks.StartTaskCommand.Execute(Nothing)
            Dim startedAt = task.StartedAt
            fixture.NowValue = fixture.NowValue.AddMinutes(90)
            fixture.Main.UpdateRecordingClock(fixture.NowValue)
            fixture.Tasks.CompleteRecordingOnNextTimeEntry = True

            Assert.ThrowsException(Of InvalidOperationException)(Sub() fixture.Times.CheckOutCommand.Execute(Nothing))
            Assert.IsFalse(task.IsDone)
            Assert.IsTrue(task.IsStarted)
            Assert.IsFalse(task.NeedsMore)
            Assert.AreEqual(startedAt, task.StartedAt)
            Assert.AreEqual(TimeSpan.FromMinutes(90), task.RecordingElapsed)
            Assert.AreSame(task, fixture.Tasks.CurrentRecordingTask)
            Assert.IsTrue(fixture.Tasks.IsTaskRecording)
            Assert.IsTrue(fixture.Tasks.CompleteRecordingOnNextTimeEntry)
            Assert.IsTrue(fixture.Tasks.CompleteTaskCommand.CanExecute(Nothing))
            Assert.IsFalse(fixture.Tasks.StartTaskCommand.CanExecute(Nothing))
            Assert.AreEqual(0, fixture.Stored().Count)
            Assert.AreEqual(0, original.Count)
            Assert.AreEqual(0, saved)

            rejectSave = False
            fixture.Times.CheckOutCommand.Execute(Nothing)
            Assert.IsTrue(task.IsDone)
            Assert.IsNull(task.StartedAt)
            Assert.IsNull(fixture.Tasks.CurrentRecordingTask)
            Assert.IsFalse(fixture.Tasks.CompleteRecordingOnNextTimeEntry)
            Assert.AreEqual(2, fixture.Stored().Count)
            Assert.AreEqual(2, fixture.Stored().Select(Function(item) item.IdTimeItem).Distinct().Count())
            Assert.AreEqual(TimeSpan.FromMinutes(90), fixture.Times.BookedTime)
            Assert.AreEqual(2, attempts)
            Assert.AreEqual(1, saved)
            Assert.AreSame(original, fixture.Times.TimeItems)
        End Sub

        <DataTestMethod>
        <DataRow(False)>
        <DataRow(True)>
        Public Sub TaskSaveFailure_NormalCompletionRestoresBookingsAndTaskState(recorded As Boolean)
            Dim rejectSave = True
            Dim saved = 0
            Dim fixture As New Workspace(saveTask:=Sub(candidate)
                                                      Assert.IsTrue(candidate.IsDone)
                                                      If rejectSave Then Throw New InvalidOperationException("Task save rejected")
                                                      saved += 1
                                                  End Sub)
            Dim task = fixture.Tasks.SelectedTaskItem
            task.MarkNeedsMore()
            If recorded Then
                fixture.Tasks.StartTaskCommand.Execute(Nothing)
                fixture.NowValue = fixture.NowValue.AddMinutes(90)
                fixture.Main.UpdateRecordingClock(fixture.NowValue)
            Else
                fixture.Main.ManualCompletionStartText = "10:00"
                fixture.Main.ManualCompletionDurationText = "01:30"
            End If
            Dim startedAt = task.StartedAt
            Dim elapsed = task.RecordingElapsed
            Dim original = fixture.Times.TimeItems
            fixture.Tasks.CompleteRecordingOnNextTimeEntry = recorded
            Assert.ThrowsException(Of InvalidOperationException)(Sub() fixture.Tasks.CompleteTaskCommand.Execute(Nothing))
            Assert.IsFalse(task.IsDone)
            Assert.AreEqual(recorded, task.IsStarted)
            Assert.AreEqual(Not recorded, task.NeedsMore)
            Assert.AreEqual(startedAt, task.StartedAt)
            Assert.AreEqual(elapsed, task.RecordingElapsed)
            Assert.AreEqual(recorded, fixture.Tasks.IsTaskRecording)
            Assert.AreEqual(recorded, fixture.Tasks.CompleteRecordingOnNextTimeEntry)
            Assert.IsTrue(fixture.Tasks.CompleteTaskCommand.CanExecute(Nothing))
            Assert.AreEqual(Not recorded, fixture.Tasks.StartTaskCommand.CanExecute(Nothing))
            Assert.AreEqual(0, fixture.Stored().Count)
            Assert.AreEqual(0, original.Count)
            Assert.AreEqual(0, saved)
            rejectSave = False
            fixture.Tasks.CompleteTaskCommand.Execute(Nothing)
            Assert.IsTrue(task.IsDone)
            Assert.IsFalse(task.NeedsMore)
            Assert.IsFalse(fixture.Tasks.IsTaskRecording)
            Assert.IsFalse(fixture.Tasks.CompleteRecordingOnNextTimeEntry)
            Assert.AreEqual(2, fixture.Stored().Count)
            Assert.AreEqual(TimeSpan.FromMinutes(90), fixture.Times.BookedTime)
            Assert.AreEqual(1, saved)
            Assert.AreSame(original, fixture.Times.TimeItems)
        End Sub

        <TestMethod>
        Public Sub TaskSaveFailure_RestoresThePreviouslyPersistedTimeline()
            Dim fixture As New Workspace(saveTask:=Sub(candidate)
                                                      Throw New InvalidOperationException("Task save rejected")
                                                  End Sub)
            SaveDialog(fixture.Times, BookingDay.AddHours(8), "Previous work")
            fixture.NowValue = BookingDay.AddHours(9)
            fixture.Times.CheckOutCommand.Execute(Nothing)
            Dim originalIds = fixture.Stored().Select(Function(item) item.IdTimeItem).ToArray()
            fixture.Main.ManualCompletionStartText = "10:00"
            fixture.Main.ManualCompletionDurationText = "01:30"
            Assert.ThrowsException(Of InvalidOperationException)(Sub() fixture.Tasks.CompleteTaskCommand.Execute(Nothing))
            CollectionAssert.AreEqual(originalIds, fixture.Stored().Select(Function(item) item.IdTimeItem).ToArray())
            Assert.AreEqual(TimeSpan.FromHours(1), fixture.Times.BookedTime)
            Assert.IsFalse(fixture.Tasks.SelectedTaskItem.IsDone)
            Assert.IsTrue(fixture.Tasks.CompleteTaskCommand.CanExecute(Nothing))
        End Sub

        <TestMethod>
        Public Sub CompletionFailure_RestoresStopRemovedByServiceNormalization()
            Dim fixture As New Workspace(True)
            SaveDialog(fixture.Times, BookingDay.AddHours(8), "Earlier work")
            fixture.NowValue = BookingDay.AddHours(9)
            fixture.Times.CheckOutCommand.Execute(Nothing)
            Dim originalIds = fixture.Stored().Select(Function(item) item.IdTimeItem).ToArray()
            fixture.NowValue = BookingDay.AddHours(10)
            fixture.Tasks.StartTaskCommand.Execute(Nothing)
            fixture.NowValue = fixture.NowValue.AddMinutes(1530.5)
            fixture.Faults.RejectNextSave = Function(request) request.Item.MarkerKind = SystemTimeMarkerKind.StopMark
            Assert.ThrowsException(Of InvalidOperationException)(Sub() fixture.Tasks.CompleteTaskCommand.Execute(Nothing))
            CollectionAssert.AreEqual(originalIds, fixture.Stored().Select(Function(item) item.IdTimeItem).ToArray())
            Assert.AreEqual(TimeSpan.FromHours(1), fixture.Times.BookedTime)
            Assert.IsTrue(fixture.Tasks.IsTaskRecording)
            Assert.IsFalse(fixture.Tasks.SelectedTaskItem.IsDone)
            fixture.Times.BookingDate = BookingDay.AddDays(3)
            fixture.Times.BookingDate = BookingDay
            Assert.AreEqual(TimeSpan.FromHours(1), fixture.Times.BookedTime)
        End Sub

        <TestMethod>
        Public Sub NonPositiveRecordingDuration_DoesNotCreateSyntheticMinute()
            Dim fixture As New Workspace()
            fixture.Tasks.StartTaskCommand.Execute(Nothing)
            Assert.ThrowsException(Of InvalidOperationException)(Sub() fixture.Tasks.CompleteTaskCommand.Execute(Nothing))
            Assert.AreEqual(0, fixture.Stored().Count)
            Assert.IsTrue(fixture.Tasks.IsTaskRecording)
            Assert.IsFalse(fixture.Tasks.SelectedTaskItem.IsDone)
        End Sub

        <TestMethod>
        Public Sub CrossDayStopEdit_DoesNotRebaseItsTimestampToTheBookingDate()
            Dim fixture As New Workspace()
            Dim expected = TimeSpan.FromMinutes(1530.5125)
            fixture.Main.ManualCompletionStartText = "08:00"
            fixture.Main.ManualCompletionDurationText = expected.ToString("c")
            fixture.Tasks.CompleteTaskCommand.Execute(Nothing)
            fixture.Times.SelectedEntry = fixture.Times.TimeItems.Last()
            Dim endTime = BookingDay.AddHours(8).Add(expected)
            SaveDialog(fixture.Times, endTime, "Edited cross-day stop", edit:=True)
            Assert.AreEqual(endTime, fixture.Stored().Last().EventTime.Value.DateTime)
            Assert.AreEqual(expected, fixture.Times.BookedTime)
        End Sub

        <TestMethod>
        Public Sub Completion_ReusesTheExactExistingBoundaryWithoutShiftingIt()
            Dim fixture As New Workspace()
            Dim endTime = fixture.NowValue.AddMinutes(90).AddMilliseconds(750)
            SaveDialog(fixture.Times, endTime, "Existing next interval")
            Dim boundaryId = fixture.Stored().Single().IdTimeItem
            fixture.Tasks.StartTaskCommand.Execute(Nothing)
            fixture.NowValue = endTime
            fixture.Tasks.CompleteTaskCommand.Execute(Nothing)
            Assert.AreEqual(2, fixture.Stored().Count)
            Assert.AreEqual(boundaryId, fixture.Stored().Last().IdTimeItem)
            Assert.AreEqual(endTime, fixture.Stored().Last().EventTime.Value.DateTime)
            Assert.AreEqual(TimeSpan.FromMinutes(90).Add(TimeSpan.FromMilliseconds(750)), fixture.Times.BookedTime)
        End Sub

        <TestMethod>
        Public Sub RepeatedTaskInterruptions_PreserveTheEarlierPauseAndResumedInterval()
            Dim fixture As New Workspace()
            fixture.Tasks.StartTaskCommand.Execute(Nothing)
            fixture.NowValue = BookingDay.AddHours(8).AddMinutes(30)
            fixture.Times.InsertWorkBreakCommand.Execute(Nothing)
            fixture.NowValue = BookingDay.AddHours(9)
            fixture.Tasks.StartTaskCommand.Execute(Nothing)
            fixture.NowValue = BookingDay.AddHours(10)
            fixture.Times.InsertWorkBreakCommand.Execute(Nothing)
            Assert.AreEqual(4, fixture.Stored().Count)
            Assert.AreEqual(TimeSpan.FromMinutes(90), fixture.Times.BookedTime)
            Assert.AreEqual(TimeSpan.FromMinutes(30), fixture.Times.WorkBreakTime)
        End Sub

        <TestMethod>
        Public Sub BookingAddEditDelete_PersistsAndKeepsOriginalCollection()
            Dim fixture As New Workspace()
            Dim vm = fixture.Times
            Dim original = vm.TimeItems
            Dim changes As New List(Of NotifyCollectionChangedAction)
            AddHandler original.CollectionChanged, Sub(sender, args) changes.Add(args.Action)
            SaveDialog(vm, BookingDay.AddHours(9), "Entwicklung")
            Dim id = vm.SelectedEntry.IdTimeItem
            Assert.AreEqual("Entwicklung", fixture.Stored().Single().ShortTitle)
            Assert.AreEqual(fixture.Services.ProjectId, fixture.Stored().Single().IdProject)
            SaveDialog(vm, BookingDay.AddHours(8), "Überarbeitet", edit:=True)
            Assert.AreEqual(id, vm.SelectedEntry.IdTimeItem)
            Assert.AreEqual(BookingDay.AddHours(8), fixture.Stored().Single().EventTime.Value.DateTime)
            Assert.AreEqual("Überarbeitet", fixture.Stored().Single().ShortTitle)
            Assert.AreSame(original, vm.TimeItems)
            Assert.IsTrue(changes.Contains(NotifyCollectionChangedAction.Add))
            vm.DeleteCommand.Execute(Nothing)
            Assert.AreEqual(0, fixture.Stored().Count)
            Assert.AreSame(original, vm.SelectedDayEntries)
        End Sub

        <TestMethod>
        Public Sub BookingDateReload_UsesServiceWithoutReplacingCollection()
            Dim fixture As New Workspace()
            Dim original = fixture.Times.TimeItems
            SaveDialog(fixture.Times, BookingDay.AddHours(8), "Arbeit")
            fixture.Times.BookingDate = BookingDay.AddDays(1)
            Assert.AreEqual(0, original.Count)
            fixture.Times.BookingDate = BookingDay
            Assert.AreEqual(1, original.Count)
            Assert.AreSame(original, fixture.Times.TimeItems)
        End Sub

        <TestMethod>
        Public Sub WorkBreakDownTimeAndStop_AreRecordedInOrderWithDurations()
            Dim fixture As New Workspace()
            Dim vm = fixture.Times
            SaveDialog(vm, BookingDay.AddHours(8), "Arbeit")
            fixture.NowValue = BookingDay.AddHours(9)
            vm.InsertWorkBreakCommand.Execute(Nothing)
            fixture.NowValue = BookingDay.AddHours(9).AddMinutes(15)
            vm.InsertDownTimeCommand.Execute(Nothing)
            vm.InsertStopMarkCommand.Execute(Nothing)
            Assert.AreEqual(4, fixture.Stored().Count)
            CollectionAssert.AreEqual(
                New TimeEntryMarkerKind() {TimeEntryMarkerKind.Normal, TimeEntryMarkerKind.WorkBreak, TimeEntryMarkerKind.DownTime, TimeEntryMarkerKind.StopMark},
                vm.TimeItems.Select(Function(item) item.MarkerKind).ToArray())
            Assert.AreEqual(TimeSpan.FromHours(1), vm.BookedTime)
            Assert.AreEqual(TimeSpan.FromMinutes(15), vm.WorkBreakTime)
            fixture.NowValue = BookingDay.AddHours(10)
            vm.CheckOutCommand.Execute(Nothing)
            Assert.AreEqual(4, vm.TimeItems.Count)
            Assert.AreEqual(BookingDay.AddHours(10), vm.LastBookingAt.Value)
            Assert.IsTrue(fixture.Stored().All(Function(item) item.BookingDate.Value = BookingDay))
            vm.BookingDate = BookingDay.AddDays(1)
            vm.BookingDate = BookingDay
            Assert.IsTrue(vm.TimeItems.Any(Function(item) item.MarkerKind = TimeEntryMarkerKind.DownTime))
        End Sub

        <TestMethod>
        Public Sub TaskStartDone_RecordsShortWorkIntervalAndCompletesTask()
            Dim fixture As New Workspace()
            Dim selected = fixture.Tasks.SelectedTaskItem
            fixture.Tasks.StartTaskCommand.Execute(Nothing)
            Assert.IsTrue(fixture.Main.IsTaskRecording)
            Assert.AreEqual(fixture.NowValue, selected.StartedAt.Value)
            fixture.NowValue = fixture.NowValue.AddMinutes(30)
            fixture.Main.UpdateRecordingClock(fixture.NowValue)
            Assert.AreEqual(TimeSpan.FromMinutes(30), selected.RecordingElapsed)
            fixture.Tasks.CompleteTaskCommand.Execute(Nothing)
            Assert.IsTrue(selected.IsDone)
            Assert.IsFalse(fixture.Main.IsTaskRecording)
            Assert.AreEqual(TimeSpan.FromMinutes(30), fixture.Times.BookedTime)
            Assert.AreEqual(2, fixture.Stored().Count)
            Assert.IsTrue(fixture.Stored().All(Function(item) item.IdTask.GetValueOrDefault() = selected.IdTask))
        End Sub

        <TestMethod>
        Public Sub ManualTaskDone_UsesEnteredShortDuration()
            Dim fixture As New Workspace()
            fixture.Main.ManualCompletionStartText = "10:00"
            fixture.Main.ManualCompletionDurationText = "00:25"
            fixture.Tasks.CompleteTaskCommand.Execute(Nothing)
            Assert.AreEqual(BookingDay.AddHours(10), fixture.Times.FirstBookingAt.Value)
            Assert.AreEqual(TimeSpan.FromMinutes(25), fixture.Times.BookedTime)
            Assert.IsTrue(fixture.Tasks.SelectedTaskItem.IsDone)
        End Sub

        <TestMethod>
        Public Sub InvalidManualDuration_DoesNotCompleteOrPersistTask()
            Dim fixture As New Workspace()
            fixture.Main.ManualCompletionDurationText = "keine Dauer"
            Assert.ThrowsException(Of InvalidOperationException)(Sub() fixture.Tasks.CompleteTaskCommand.Execute(Nothing))
            Assert.IsFalse(fixture.Tasks.SelectedTaskItem.IsDone)
            Assert.AreEqual(0, fixture.Stored().Count)
        End Sub

        <TestMethod>
        Public Sub NewTimeEntry_CanFinishRunningTask()
            Dim fixture As New Workspace()
            fixture.Tasks.StartTaskCommand.Execute(Nothing)
            SaveDialog(fixture.Times, BookingDay.AddHours(9), "Nächste Tätigkeit", complete:=True)
            Assert.IsFalse(fixture.Tasks.IsTaskRecording)
            Assert.IsTrue(fixture.Tasks.SelectedTaskItem.IsDone)
            Assert.AreEqual(2, fixture.Stored().Count)
            Assert.AreEqual(TimeSpan.FromHours(1), fixture.Times.BookedTime)
            Assert.AreEqual(fixture.Tasks.SelectedTaskItem.IdTask, fixture.Stored()(0).IdTask.Value)
            Assert.AreEqual(BookingDay.AddHours(8), fixture.Stored()(0).EventTime.Value.DateTime)
            Assert.AreEqual("Nächste Tätigkeit", fixture.Stored()(1).ShortTitle)
        End Sub

        <DataTestMethod>
        <DataRow("pause")>
        <DataRow("downtime")>
        <DataRow("errand")>
        <DataRow("stop")>
        <DataRow("checkout")>
        <DataRow("next")>
        Public Sub BoundaryBooking_FinalizesRunningIntervalWithoutDuplicateMarker(operation As String)
            Dim fixture As New Workspace()
            fixture.Tasks.StartTaskCommand.Execute(Nothing)
            fixture.NowValue = BookingDay.AddHours(8).AddMinutes(30)
            Select Case operation
                Case "pause"
                    fixture.Times.InsertWorkBreakCommand.Execute(Nothing)
                Case "downtime"
                    fixture.Times.InsertDownTimeCommand.Execute(Nothing)
                Case "errand"
                    fixture.Times.InsertErrandCommand.Execute(Nothing)
                Case "stop"
                    fixture.Times.InsertStopMarkCommand.Execute(Nothing)
                Case "checkout"
                    fixture.Times.CheckOutCommand.Execute(Nothing)
                Case "next"
                    SaveDialog(fixture.Times, fixture.NowValue, "Nächste Tätigkeit")
            End Select
            Assert.IsFalse(fixture.Tasks.IsTaskRecording)
            Assert.IsFalse(fixture.Tasks.SelectedTaskItem.IsDone)
            Assert.AreEqual(2, fixture.Stored().Count)
            Assert.AreEqual(fixture.Tasks.SelectedTaskItem.IdTask, fixture.Stored()(0).IdTask.Value)
            Assert.AreEqual(BookingDay.AddHours(8), fixture.Stored()(0).EventTime.Value.DateTime)
            Assert.AreEqual(1, fixture.Stored().Where(Function(item) item.EventTime.Value.DateTime = fixture.NowValue).Count())
            Assert.AreEqual(TimeSpan.FromMinutes(30), fixture.Times.BookedTime)
        End Sub

        <TestMethod>
        Public Sub Errand_UsesStopCategoryWithoutStoppingTimelineOrCountingAsWork()
            Dim fixture As New Workspace()
            SaveDialog(fixture.Times, BookingDay.AddHours(8), "Arbeit")
            fixture.NowValue = BookingDay.AddHours(9)
            fixture.Times.InsertErrandCommand.Execute(Nothing)
            fixture.NowValue = BookingDay.AddHours(10)
            fixture.Times.CheckOutCommand.Execute(Nothing)

            Dim stored = fixture.Stored()
            Assert.AreEqual(3, stored.Count)
            Assert.AreEqual(SystemTimeMarkerKind.Errand, stored(1).MarkerKind)
            Assert.AreEqual(TimeBookingOptions.ErrandEventInfo, stored(1).EventInfo)
            Assert.AreEqual(SystemTimeMarkerIds.StopMarkCategoryId, stored(1).IdCategory)
            Assert.AreEqual(TimeSpan.FromHours(1), stored(1).DurationToNext)
            Assert.AreEqual(TimeSpan.FromHours(1), fixture.Times.BookedTime)
            Assert.AreEqual(TimeSpan.FromHours(1), fixture.Services.GetBookingDay(
                New GetBookingDayRequest With {.AccessContext = fixture.Access, .BookingDate = BookingDay}).Value.TotalBookedTime)
        End Sub

        <TestMethod>
        Public Sub ErrandCommand_UsesCurrentAndFutureSelectedBookingDates()
            Dim fixture As New Workspace()
            For Each selectedDay In {BookingDay, BookingDay.AddDays(30)}
                fixture.Times.BookingDate = selectedDay
                fixture.NowValue = BookingDay.AddYears(1).AddHours(9)
                fixture.Times.InsertErrandCommand.Execute(Nothing)
                Dim stored = fixture.Stored(selectedDay).Single()
                Assert.AreEqual(selectedDay.AddHours(9), stored.EventTime.Value.DateTime)
                Assert.AreEqual(selectedDay, stored.BookingDate.Value)
            Next
        End Sub

        <TestMethod>
        Public Sub DownTime_SurvivesProductionNormalizationAndDoesNotCountAsWork()
            Dim fixture As New Workspace()
            SaveDialog(fixture.Times, BookingDay.AddHours(8), "Arbeit")
            fixture.NowValue = BookingDay.AddHours(9)
            fixture.Times.InsertDownTimeCommand.Execute(Nothing)
            fixture.NowValue = BookingDay.AddHours(10)
            fixture.Times.CheckOutCommand.Execute(Nothing)
            Assert.AreEqual(3, fixture.Stored().Count)
            Assert.AreEqual(TimeSpan.FromHours(1), fixture.Times.BookedTime)
            Dim day = fixture.Services.GetBookingDay(New GetBookingDayRequest With {
                .AccessContext = fixture.Access, .BookingDate = BookingDay
            }).Value
            Assert.AreEqual(TimeSpan.FromHours(1), day.TotalBookedTime)
            Dim options As New TimeBookingOptions()
            Dim items = day.Items.Select(Function(item) New TimeItem With {
                .IdTimeItem = item.IdTimeItem, .IdProject = item.IdProject,
                .IdUser = item.IdUser, .IdCategory = item.IdCategory,
                .EventTime = item.EventTime, .BookingDate = BookingDay,
                .EventTypeInfo = options.TimeEventType, .EventInfo = item.EventInfo
            }).ToList()
            Dim normalized = TimeBookingAlgorithm.NormalizeBookingDay(items, options)
            Assert.AreEqual(3, normalized.TimelineItems.Count)
            Assert.AreEqual(0, normalized.RemovedItems.Count)
            Assert.AreEqual(SystemTimeMarkerKind.DownTime, options.GetMarkerKind(normalized.TimelineItems(1)))
            Assert.AreEqual(TimeSpan.FromHours(1), normalized.TimelineItems(0).DurationToNext.Value)
            fixture.Times.BookingDate = BookingDay.AddDays(1)
            fixture.Times.BookingDate = BookingDay
            Assert.AreEqual(TimeEntryMarkerKind.DownTime, fixture.Times.TimeItems(1).MarkerKind)
            Assert.AreEqual(TimeSpan.FromHours(1), fixture.Times.BookedTime)
        End Sub

        <TestMethod>
        Public Sub AuthoritativeMutation_RemovesNormalizedStopFromDisplayedCollection()
            Dim fixture As New Workspace()
            Dim source = fixture.Times.TimeItems
            SaveDialog(fixture.Times, BookingDay.AddHours(8), "Arbeit")
            fixture.Times.InsertStopMarkCommand.Execute(Nothing)
            Dim stopId = fixture.Times.SelectedEntry.IdTimeItem
            SaveDialog(fixture.Times, BookingDay.AddHours(9), "Weitere Arbeit")
            Assert.AreEqual(2, fixture.Stored().Count)
            Assert.AreEqual(2, source.Count)
            Assert.IsFalse(source.Any(Function(item) item.IdTimeItem = stopId))
            CollectionAssert.AreEqual(
                fixture.Stored().Select(Function(item) item.IdTimeItem).ToArray(),
                source.Select(Function(item) item.IdTimeItem).ToArray())
            Assert.AreSame(source, fixture.Times.TimeItems)
        End Sub

        <TestMethod>
        Public Sub EditingLoadedNormalBooking_PreservesItsCategory()
            Dim fixture As New Workspace()
            Dim category = fixture.Services.CreateCategory(New SaveCategoryRequest With {
                .IdTenant = fixture.Services.Tenant.IdTenant, .IdActingUser = fixture.Services.ActingUserId,
                .Item = New CategoryMasterDataDto With {
                    .IdTenant = fixture.Services.Tenant.IdTenant, .IdUser = fixture.Services.ActingUserId,
                    .CategoryName = "Support"
                }
            }).Value
            Dim result = fixture.Services.AddTimeBooking(New SaveTimeBookingRequest With {
                .AccessContext = fixture.Access,
                .Item = New TimeBookingItemDto With {
                    .IdTimeItem = Guid.NewGuid(), .IdTenant = fixture.Services.Tenant.IdTenant,
                    .IdUser = fixture.Services.ActingUserId, .IdProject = fixture.Services.ProjectId,
                    .IdCategory = category.IdCategory, .ShortTitle = "Support",
                    .EventTime = New DateTimeOffset(BookingDay.AddHours(8)), .BookingDate = BookingDay
                }
            })
            Assert.IsTrue(result.Success)
            fixture.Times.RefreshCategories(fixture.Services.GetCategories(New MasterDataQueryRequest With {
                .IdTenant = fixture.Services.Tenant.IdTenant, .IdActingUser = fixture.Services.ActingUserId
            }).Value)
            fixture.Times.BookingDate = BookingDay.AddDays(1)
            fixture.Times.BookingDate = BookingDay
            Dim request As TimeEntryEditRequestEventArgs = Nothing
            AddHandler fixture.Times.TimeEntryEditRequested, Sub(sender, args) request = args
            fixture.Times.EditCommand.Execute(Nothing)
            Assert.IsNotNull(request)
            Assert.AreEqual(category.IdCategory, fixture.Times.SelectedCategory.IdCategory)
            request.SaveAction.Invoke(BookingDay.AddHours(9), "Support korrigiert", "Beschreibung", False)
            Assert.AreEqual(category.IdCategory, fixture.Stored().Single().IdCategory)
            Assert.AreEqual("Support korrigiert", fixture.Stored().Single().ShortTitle)
        End Sub

        <TestMethod>
        Public Sub NewNormalBooking_UsesSelectedCategoryAndRefreshKeepsItSelected()
            Dim fixture As New Workspace()
            Dim category = fixture.Services.CreateCategory(New SaveCategoryRequest With {
                .IdTenant = fixture.Services.Tenant.IdTenant, .IdActingUser = fixture.Services.ActingUserId,
                .Item = New CategoryMasterDataDto With {
                    .IdTenant = fixture.Services.Tenant.IdTenant, .IdUser = fixture.Services.ActingUserId,
                    .CategoryName = "Kundendienst"
                }
            }).Value
            Dim query = New MasterDataQueryRequest With {
                .IdTenant = fixture.Services.Tenant.IdTenant, .IdActingUser = fixture.Services.ActingUserId
            }

            fixture.Times.RefreshCategories(fixture.Services.GetCategories(query).Value)
            fixture.Times.SelectedCategory = fixture.Times.Categories.Single(Function(item) item.IdCategory = category.IdCategory)
            SaveDialog(fixture.Times, BookingDay.AddHours(8), "Kundendienst")
            fixture.Times.RefreshCategories(fixture.Services.GetCategories(query).Value)

            Assert.AreEqual(category.IdCategory, fixture.Stored().Single().IdCategory)
            Assert.AreEqual(category.IdCategory, fixture.Times.SelectedCategory.IdCategory)
            Assert.IsFalse(fixture.Times.Categories.Any(Function(item) item.IdCategory = SystemTimeMarkerIds.StopMarkCategoryId))
            Assert.IsFalse(fixture.Times.Categories.Any(Function(item) item.IdCategory = SystemTimeMarkerIds.WorkBreakCategoryId))
        End Sub

        <TestMethod>
        Public Sub TaskBoundary_ReusesBookingAtTaskStartWithoutShiftingTheInterval()
            Dim fixture As New Workspace()
            SaveDialog(fixture.Times, BookingDay.AddHours(8), "Arbeit")
            Dim originalId = fixture.Times.SelectedEntry.IdTimeItem
            fixture.Tasks.StartTaskCommand.Execute(Nothing)
            SaveDialog(fixture.Times, BookingDay.AddHours(9), "Nächste Tätigkeit", complete:=True)
            Assert.AreEqual(2, fixture.Stored().Count)
            Assert.AreEqual(originalId, fixture.Stored()(0).IdTimeItem)
            Assert.AreEqual(fixture.Tasks.SelectedTaskItem.IdTask, fixture.Stored()(0).IdTask.Value)
            Assert.AreEqual(TimeSpan.FromHours(1), fixture.Times.BookedTime)
        End Sub

        <TestMethod>
        Public Sub LoginSuccessLogoutAndFailure_DoNotRetainSession()
            Dim services As New TestApplicationServices()
            Dim user = services.GetTenantUsers(services.Tenant.IdTenant).Value.Single()
            Dim login As New LoginViewModel(New TestAuthenticationService(user, "Test-password-42"))
            Assert.IsTrue(login.Login("a.adler", "Test-password-42", user.IdTenant))
            Assert.AreEqual(user.IdUser, login.Session.IdUser)
            login.Logout()
            Assert.IsNull(login.Session)
            Assert.IsTrue(login.Login(user.EMail, "Test-password-42"))
            Assert.IsFalse(login.Login("a.adler", "incorrect"))
            Assert.IsNull(login.Session)
            StringAssert.Contains(login.ErrorMessage, "InvalidCredentials")
            Assert.IsFalse(login.Login("someone-else", "Test-password-42"))
            Assert.IsNull(login.Session)
        End Sub

        <TestMethod>
        Public Sub LoginRepeatedFailures_LockOutUntilClockAdvances()
            Dim services As New TestApplicationServices()
            Dim user = services.GetTenantUsers(services.Tenant.IdTenant).Value.Single()
            Dim nowValue As New DateTimeOffset(BookingDay, TimeSpan.Zero)
            Dim login As New LoginViewModel(New TestAuthenticationService(user, "Test-password-42", Function() nowValue))
            For attempt = 1 To 5
                Assert.IsFalse(login.Login(user.UserIdent, "incorrect"))
            Next
            Assert.IsFalse(login.Login(user.UserIdent, "Test-password-42"))
            StringAssert.Contains(login.ErrorMessage, "UserLockedOut")
            nowValue = nowValue.AddMinutes(16)
            Assert.IsTrue(login.Login(user.UserIdent, "Test-password-42"))
        End Sub

        <TestMethod>
        Public Sub TemporaryPasswordChange_AuthenticatesOnlyAfterSuccessfulChange()
            Dim services As New TestApplicationServices()
            Dim user = services.GetTenantUsers(services.Tenant.IdTenant).Value.Single()
            user.MustChangePassword = True
            user.PreliminaryPasswordExpiresAt = New DateTimeOffset(BookingDay.AddDays(-1), TimeSpan.Zero)
            Dim login As New LoginViewModel(New TestAuthenticationService(user, "Test-password-42"))
            Assert.IsFalse(login.Login(user.UserIdent, "Test-password-42"))
            Assert.IsTrue(login.MustChangePassword)
            Assert.IsNull(login.Session)
            Assert.IsTrue(login.ChangeTemporaryPassword("Test-password-42", "Replacement-password-43"))
            Assert.IsNotNull(login.Session)
            login.Logout()
            Assert.IsFalse(login.Login(user.UserIdent, "Test-password-42"))
            Assert.IsTrue(login.Login(user.UserIdent, "Replacement-password-43"))
        End Sub

        Private Shared Sub SaveDialog(vm As TimeCollectionViewModel, time As DateTime, title As String,
                                      Optional edit As Boolean = False, Optional complete As Boolean = False)
            Dim request As TimeEntryEditRequestEventArgs = Nothing
            Dim handler As EventHandler(Of TimeEntryEditRequestEventArgs) = Sub(sender, args) request = args
            AddHandler vm.TimeEntryEditRequested, handler
            If edit Then
                vm.EditCommand.Execute(Nothing)
            Else
                vm.AddCommand.Execute(Nothing)
            End If
            RemoveHandler vm.TimeEntryEditRequested, handler
            Assert.IsNotNull(request)
            request.SaveAction.Invoke(time, title, "Beschreibung", complete)
        End Sub

        Private NotInheritable Class Workspace
            Public ReadOnly Services As New TestApplicationServices()
            Public ReadOnly Times As TimeCollectionViewModel
            Public ReadOnly Tasks As TaskManagementViewModel
            Public ReadOnly Main As VmMain
            Public ReadOnly Access As TimeBookingAccessContextDto
            Public ReadOnly Faults As FailingBookingService
            Public NowValue As DateTime = BookingDay.AddHours(8)

            Public Sub New(Optional injectFailures As Boolean = False, Optional saveTask As Action(Of TaskItemViewModel) = Nothing)
                Access = New TimeBookingAccessContextDto With {
                    .IdTenant = Services.Tenant.IdTenant, .IdActingUser = Services.ActingUserId, .IdBookingUser = Services.ActingUserId
                }
                Dim projects = Services.GetProjects(New MasterDataQueryRequest With {
                    .IdTenant = Services.Tenant.IdTenant, .IdActingUser = Services.ActingUserId
                }).Value
                Dim categories = Services.GetCategories(New MasterDataQueryRequest With {
                    .IdTenant = Services.Tenant.IdTenant, .IdActingUser = Services.ActingUserId
                }).Value
                Faults = New FailingBookingService(Services)
                Dim bookingService As ITimeBookingService = If(injectFailures, DirectCast(Faults, ITimeBookingService), Services)
                Times = New TimeCollectionViewModel(
                    BookingDay, bookingService, Access, projects, Services.CategoryId, Function() NowValue, categories)
                Dim task = New TaskItemViewModel("Aufgabe", "Beschreibung", "") With {
                    .IdProject = Services.ProjectId, .IdTask = Guid.NewGuid()
                }
                Tasks = New TaskManagementViewModel({New TaskListViewModel("Liste", "", {task})}, Function() NowValue, saveTask)
                Main = New VmMain(Times, Tasks)
            End Sub

            Public Function Stored(Optional bookingDate As DateTime? = Nothing) As IReadOnlyList(Of TimeBookingItemDto)
                Return Services.GetBookingDay(New GetBookingDayRequest With {
                    .AccessContext = Access, .BookingDate = If(bookingDate, BookingDay)
                }).Value.Items
            End Function
        End Class
    End Class
End Namespace
