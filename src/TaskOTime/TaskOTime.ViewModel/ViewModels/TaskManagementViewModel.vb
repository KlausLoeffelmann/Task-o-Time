Imports System.Collections.ObjectModel
Imports System.Linq
Imports System.Windows.Input
Imports TaskOTime.ViewModel.Base

Namespace ViewModels
    Public Class TaskManagementViewModel
        Inherits ViewModelBase

        Private _selectedTaskList As TaskListViewModel
        Private _selectedTaskItem As TaskItemViewModel
        Private ReadOnly _startTaskCommand As DelegateCommand
        Private ReadOnly _completeTaskCommand As DelegateCommand
        Private ReadOnly _moreToDoCommand As DelegateCommand

        Public Sub New()
            TaskLists = CreateSampleTaskLists()

            _startTaskCommand = New DelegateCommand(
                Sub(parameter As Object) StartSelectedTask(),
                Function(parameter As Object) CanStartSelectedTask())
            _completeTaskCommand = New DelegateCommand(
                Sub(parameter As Object) CompleteSelectedTask(),
                Function(parameter As Object) CanCompleteSelectedTask())
            _moreToDoCommand = New DelegateCommand(
                Sub(parameter As Object) MarkSelectedTaskNeedsMore(),
                Function(parameter As Object) CanMarkSelectedTaskNeedsMore())

            SelectedTaskList = TaskLists.FirstOrDefault()
        End Sub

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

        Private Function CanStartSelectedTask() As Boolean
            Return SelectedTaskItem IsNot Nothing AndAlso
                   Not SelectedTaskItem.IsDone AndAlso
                   Not SelectedTaskItem.IsStarted
        End Function

        Private Sub StartSelectedTask()
            If Not CanStartSelectedTask() Then
                Return
            End If

            SelectedTaskItem.MarkStarted()
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
            RefreshSelectedTaskState()
        End Sub

        Private Sub RefreshSelectedTaskState()
            OnPropertyChanged(NameOf(SelectedTaskSummary))
            OnPropertyChanged(NameOf(SelectedListSummary))
            RaiseCommandStatesChanged()
        End Sub

        Private Sub RaiseCommandStatesChanged()
            If _startTaskCommand IsNot Nothing Then
                _startTaskCommand.RaiseCanExecuteChanged()
            End If

            If _completeTaskCommand IsNot Nothing Then
                _completeTaskCommand.RaiseCanExecuteChanged()
            End If

            If _moreToDoCommand IsNot Nothing Then
                _moreToDoCommand.RaiseCanExecuteChanged()
            End If
        End Sub
    End Class
End Namespace
