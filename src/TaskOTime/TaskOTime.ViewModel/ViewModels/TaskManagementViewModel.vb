Imports System
Imports System.ComponentModel
Imports System.Collections.ObjectModel
Imports System.Linq
Imports System.Windows.Input
Imports TaskOTime.ViewModel.Base

Namespace ViewModels
    Public Class TaskManagementViewModel
        Inherits ViewModelBase

        Private _selectedTaskList As TaskListViewModel
        Private _selectedTaskItem As TaskItemViewModel
        Private _currentRecordingTask As TaskItemViewModel
        Private _completeRecordingOnNextTimeEntry As Boolean
        Private ReadOnly _newTaskCommand As DelegateCommand
        Private ReadOnly _editTaskCommand As DelegateCommand
        Private ReadOnly _newTaskListCommand As DelegateCommand
        Private ReadOnly _editTaskListCommand As DelegateCommand
        Private ReadOnly _startTaskCommand As DelegateCommand
        Private ReadOnly _completeTaskCommand As DelegateCommand
        Private ReadOnly _moreToDoCommand As DelegateCommand

        Public Sub New()
            TaskLists = CreateSampleTaskLists()

            _newTaskCommand = New DelegateCommand(Sub(parameter) AddTask())
            _editTaskCommand = New DelegateCommand(Sub(parameter) EditSelectedTask(), Function(parameter) SelectedTaskItem IsNot Nothing)
            _newTaskListCommand = New DelegateCommand(Sub(parameter) RequestNewTaskList())
            _editTaskListCommand = New DelegateCommand(Sub(parameter) RequestEditTaskList(), Function(parameter) SelectedTaskList IsNot Nothing)
            _startTaskCommand = New DelegateCommand(Sub(parameter) StartSelectedTask(), Function(parameter) CanStartSelectedTask())
            _completeTaskCommand = New DelegateCommand(Sub(parameter) CompleteSelectedTask(), Function(parameter) CanCompleteSelectedTask())
            _moreToDoCommand = New DelegateCommand(Sub(parameter) MarkSelectedTaskNeedsMore(), Function(parameter) CanMarkSelectedTaskNeedsMore())

            SelectedTaskList = TaskLists.FirstOrDefault()
        End Sub

        Public Event TaskListEditRequested As EventHandler(Of TaskListEditRequestEventArgs)

        Public ReadOnly Property TaskLists As ObservableCollection(Of TaskListViewModel)

        Public Property SelectedTaskList As TaskListViewModel
            Get
                Return _selectedTaskList
            End Get
            Set(value As TaskListViewModel)
                If SetProperty(_selectedTaskList, value, NameOf(SelectedTaskList)) Then
                    If value Is Nothing Then
                        SelectedTaskItem = Nothing
                    Else
                        SelectedTaskItem = value.Tasks.FirstOrDefault(Function(taskItem) taskItem.IsOpen)
                        If SelectedTaskItem Is Nothing Then
                            SelectedTaskItem = value.Tasks.FirstOrDefault()
                        End If
                    End If

                    OnPropertyChanged(NameOf(SelectedListSummary))
                    RaiseCommandStatesChanged()
                End If
            End Set
        End Property

        Public Property SelectedTaskItem As TaskItemViewModel
            Get
                Return _selectedTaskItem
            End Get
            Set(value As TaskItemViewModel)
                If SetProperty(_selectedTaskItem, value, NameOf(SelectedTaskItem)) Then
                    OnPropertyChanged(NameOf(SelectedTaskSummary))
                    RaiseCommandStatesChanged()
                End If
            End Set
        End Property

        Public Property CompleteRecordingOnNextTimeEntry As Boolean
            Get
                Return _completeRecordingOnNextTimeEntry
            End Get
            Set(value As Boolean)
                SetProperty(_completeRecordingOnNextTimeEntry, value, NameOf(CompleteRecordingOnNextTimeEntry))
            End Set
        End Property

        Public ReadOnly Property CurrentRecordingTask As TaskItemViewModel
            Get
                Return _currentRecordingTask
            End Get
        End Property

        Public ReadOnly Property IsTaskRecording As Boolean
            Get
                Return CurrentRecordingTask IsNot Nothing AndAlso CurrentRecordingTask.IsStarted
            End Get
        End Property

        Public ReadOnly Property RecordingStatusText As String
            Get
                If Not IsTaskRecording Then
                    Return String.Empty
                End If

                Return CurrentRecordingTask.RecordingStatusText
            End Get
        End Property

        Public ReadOnly Property NewTaskCommand As ICommand
            Get
                Return _newTaskCommand
            End Get
        End Property

        Public ReadOnly Property EditTaskCommand As ICommand
            Get
                Return _editTaskCommand
            End Get
        End Property

        Public ReadOnly Property NewTaskListCommand As ICommand
            Get
                Return _newTaskListCommand
            End Get
        End Property

        Public ReadOnly Property EditTaskListCommand As ICommand
            Get
                Return _editTaskListCommand
            End Get
        End Property

        Public ReadOnly Property StartTaskCommand As ICommand
            Get
                Return _startTaskCommand
            End Get
        End Property

        Public ReadOnly Property CompleteTaskCommand As ICommand
            Get
                Return _completeTaskCommand
            End Get
        End Property

        Public ReadOnly Property MoreToDoCommand As ICommand
            Get
                Return _moreToDoCommand
            End Get
        End Property

        Public ReadOnly Property TaskPanelTitle As String
            Get
                Return "Aufgaben"
            End Get
        End Property

        Public ReadOnly Property TaskPanelSummary As String
            Get
                Return "Listen oben, Aufgaben unten – vorbereitet für spätere AppServer-Daten."
            End Get
        End Property

        Public ReadOnly Property SelectedListSummary As String
            Get
                If SelectedTaskList Is Nothing Then
                    Return "Keine Aufgabenliste ausgewählt."
                End If

                Return SelectedTaskList.ProgressSummary
            End Get
        End Property

        Public ReadOnly Property SelectedTaskSummary As String
            Get
                If SelectedTaskItem Is Nothing Then
                    Return "Keine Aufgabe ausgewählt."
                End If

                Return SelectedTaskItem.ActionHint
            End Get
        End Property

        Private Shared Function CreateSampleTaskLists() As ObservableCollection(Of TaskListViewModel)
            Return New ObservableCollection(Of TaskListViewModel) From {
                New TaskListViewModel(
                    "Mein Tag",
                    "Fokus für heute",
                    {
                        New TaskItemViewModel("Zeitbuchungen prüfen", "Tageszeiten kurz mit dem Kalender abgleichen.", "Heute"),
                        New TaskItemViewModel("Rückfragen klären", "Offene Punkte aus der Morgenrunde nachfassen.", "Heute"),
                        New TaskItemViewModel("Tagesabschluss vorbereiten", "Notizen und offene Blöcke für den Feierabend sammeln.", "17:00")
                    }),
                New TaskListViewModel(
                    "Projektarbeit",
                    "Aktive Liefergegenstände",
                    {
                        New TaskItemViewModel("UI-Schnitt abstimmen", "Aufgabenpanel mit Zeiterfassung verzahnen.", "Diese Woche"),
                        New TaskItemViewModel("Service-Vertrag skizzieren", "Felder für spätere AppServer-Tasks festhalten.", "Später")
                    }),
                New TaskListViewModel(
                    "Nachverfolgung",
                    "Wartet auf Antwort",
                    {
                        New TaskItemViewModel("Freigabe einholen", "Status beim Fachbereich nachfragen.", "Morgen"),
                        New TaskItemViewModel("Dokumentation ergänzen", "Kurze Bediennotiz für Aufgabenstatus vormerken.", "Diese Woche")
                    })
            }
        End Function

        Private Sub AddTask()
            If SelectedTaskList Is Nothing Then
                Return
            End If

            Dim taskItem = New TaskItemViewModel("Neue Aufgabe", "Beschreibung ergänzen.", "Heute")
            AddHandler taskItem.PropertyChanged, AddressOf OnRecordingTaskPropertyChanged
            SelectedTaskList.Tasks.Add(taskItem)
            SelectedTaskItem = taskItem
            RefreshSelectedTaskState()
        End Sub

        Private Sub EditSelectedTask()
            If SelectedTaskItem Is Nothing Then
                Return
            End If

            SelectedTaskItem.Title = "Bearbeitet: " & SelectedTaskItem.Title
            SelectedTaskItem.Description = "Aufgabe wurde über die vorbereitete Bearbeiten-Aktion geändert."
            RefreshSelectedTaskState()
        End Sub

        Private Sub RequestNewTaskList()
            RaiseEvent TaskListEditRequested(
                Me,
                New TaskListEditRequestEventArgs(
                    "Neue Aufgabenliste",
                    "Beschreibung ergänzen",
                    Sub(title, subtitle)
                        Dim taskList = New TaskListViewModel(title, subtitle, Enumerable.Empty(Of TaskItemViewModel)())
                        TaskLists.Add(taskList)
                        SelectedTaskList = taskList
                    End Sub))
        End Sub

        Private Sub RequestEditTaskList()
            If SelectedTaskList Is Nothing Then
                Return
            End If

            RaiseEvent TaskListEditRequested(
                Me,
                New TaskListEditRequestEventArgs(
                    SelectedTaskList.Title,
                    SelectedTaskList.Subtitle,
                    Sub(title, subtitle)
                        SelectedTaskList.Title = title
                        SelectedTaskList.Subtitle = subtitle
                    End Sub))
        End Sub

        Private Function CanStartSelectedTask() As Boolean
            Return SelectedTaskItem IsNot Nothing AndAlso
                   Not SelectedTaskItem.IsDone AndAlso
                   Not SelectedTaskItem.IsStarted
        End Function

        Private Sub StartSelectedTask()
            If Not CanStartSelectedTask() Then
                Return
            End If

            If CurrentRecordingTask IsNot Nothing AndAlso CurrentRecordingTask IsNot SelectedTaskItem Then
                CurrentRecordingTask.StopRecordingWithoutFinishing()
                ClearCurrentRecording()
            End If

            SelectedTaskItem.MarkStarted()
            _currentRecordingTask = SelectedTaskItem
            AddHandler _currentRecordingTask.PropertyChanged, AddressOf OnRecordingTaskPropertyChanged
            OnPropertyChanged(NameOf(CurrentRecordingTask))
            OnPropertyChanged(NameOf(IsTaskRecording))
            OnPropertyChanged(NameOf(RecordingStatusText))
            RefreshSelectedTaskState()
        End Sub

        Private Function CanCompleteSelectedTask() As Boolean
            Return SelectedTaskItem IsNot Nothing AndAlso Not SelectedTaskItem.IsDone
        End Function

        Private Sub CompleteSelectedTask()
            If Not CanCompleteSelectedTask() Then
                Return
            End If

            SelectedTaskItem.MarkDone()
            ClearCurrentRecording()
            RefreshSelectedTaskState()
        End Sub

        Private Function CanMarkSelectedTaskNeedsMore() As Boolean
            Return SelectedTaskItem IsNot Nothing AndAlso Not SelectedTaskItem.IsDone
        End Function

        Private Sub MarkSelectedTaskNeedsMore()
            If Not CanMarkSelectedTaskNeedsMore() Then
                Return
            End If

            SelectedTaskItem.MarkNeedsMore()
            ClearCurrentRecording()
            RefreshSelectedTaskState()
        End Sub

        Public Sub StopRecordingAfterTimeEntry(completeTask As Boolean)
            If CurrentRecordingTask Is Nothing Then
                Return
            End If

            If completeTask Then
                CurrentRecordingTask.MarkDone()
            Else
                CurrentRecordingTask.StopRecordingWithoutFinishing()
            End If

            CompleteRecordingOnNextTimeEntry = False
            ClearCurrentRecording()
            RefreshSelectedTaskState()
        End Sub

        Public Sub UpdateRecordingClock(nowValue As DateTime)
            If CurrentRecordingTask Is Nothing Then
                Return
            End If

            CurrentRecordingTask.UpdateRecordingElapsed(nowValue)
            OnPropertyChanged(NameOf(RecordingStatusText))
        End Sub

        Private Sub ClearCurrentRecording()
            If _currentRecordingTask IsNot Nothing Then
                RemoveHandler _currentRecordingTask.PropertyChanged, AddressOf OnRecordingTaskPropertyChanged
            End If

            _currentRecordingTask = Nothing
            OnPropertyChanged(NameOf(CurrentRecordingTask))
            OnPropertyChanged(NameOf(IsTaskRecording))
            OnPropertyChanged(NameOf(RecordingStatusText))
        End Sub

        Private Sub OnRecordingTaskPropertyChanged(sender As Object, e As PropertyChangedEventArgs)
            If e.PropertyName = NameOf(TaskItemViewModel.IsStarted) OrElse
               e.PropertyName = NameOf(TaskItemViewModel.RecordingStatusText) Then
                OnPropertyChanged(NameOf(IsTaskRecording))
                OnPropertyChanged(NameOf(RecordingStatusText))
            End If
        End Sub

        Private Sub RefreshSelectedTaskState()
            OnPropertyChanged(NameOf(SelectedTaskSummary))
            OnPropertyChanged(NameOf(SelectedListSummary))
            RaiseCommandStatesChanged()
        End Sub

        Private Sub RaiseCommandStatesChanged()
            _newTaskCommand.RaiseCanExecuteChanged()
            _editTaskCommand.RaiseCanExecuteChanged()
            _newTaskListCommand.RaiseCanExecuteChanged()
            _editTaskListCommand.RaiseCanExecuteChanged()
            _startTaskCommand.RaiseCanExecuteChanged()
            _completeTaskCommand.RaiseCanExecuteChanged()
            _moreToDoCommand.RaiseCanExecuteChanged()
        End Sub
    End Class
End Namespace
