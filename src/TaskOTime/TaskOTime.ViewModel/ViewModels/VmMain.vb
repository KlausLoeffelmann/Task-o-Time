Imports System
Imports System.Collections.ObjectModel
Imports System.ComponentModel
Imports System.Windows.Input
Imports TaskOTime.ViewModel.Base

Namespace ViewModels
    Public Class VmMain
        Inherits ViewModelBase

        Private _selectedDate As DateTime
        Private _isWideLayout As Boolean = True
        Private _manualCompletionStartText As String = DateTime.Now.ToString("HH:mm")
        Private _manualCompletionDurationText As String = "00:30"

        Public Sub New(Optional suppliedTimeCollection As TimeCollectionViewModel = Nothing,
                       Optional suppliedTaskManagement As TaskManagementViewModel = Nothing)
            _selectedDate = DateTime.Today
            Options = New AppOptionsViewModel()
            Me.TimeCollection = If(suppliedTimeCollection, New TimeCollectionViewModel(_selectedDate))
            _selectedDate = Me.TimeCollection.BookingDate
            AddHandler TimeCollection.PropertyChanged, AddressOf OnTimeCollectionPropertyChanged
            AddHandler TimeCollection.TimeEntryCreated, AddressOf OnTimeEntryCreated
            AddHandler TimeCollection.TimeEntryEditRequested, AddressOf OnTimeEntryEditRequested

            Me.TaskManagement = If(suppliedTaskManagement, New TaskManagementViewModel())
            AddHandler TaskManagement.PropertyChanged, AddressOf OnTaskManagementPropertyChanged
            AddHandler TaskManagement.TaskListEditRequested, AddressOf OnTaskListEditRequested
            AddHandler TaskManagement.TaskCompletionRequested, AddressOf OnTaskCompletionRequested

            TodayCommand = New DelegateCommand(AddressOf SelectToday)
            YesterdayCommand = New DelegateCommand(AddressOf SelectYesterday)
            OptionsCommand = New DelegateCommand(AddressOf ShowOptionsDialog)
            ExportSelectedDayCommand = New DelegateCommand(AddressOf ShowExportSelectedDayDialog)
            ExportPeriodCommand = New DelegateCommand(AddressOf ShowExportPeriodDialog)
            ManageProjectsCommand = New DelegateCommand(AddressOf ShowProjectsDialog)
            ManageTaskListsCommand = New DelegateCommand(AddressOf ShowTaskListsDialog)
            ManageTasksCommand = New DelegateCommand(AddressOf ShowTasksDialog)
            ManageUsersAdminCommand = New DelegateCommand(AddressOf ShowUsersAdminDialog)
            ShowDailyStatementCommand = New DelegateCommand(AddressOf ShowDailyStatementDialog)
            ShowWeeklyStatementCommand = New DelegateCommand(AddressOf ShowWeeklyStatementDialog)
            ShowMonthlyStatementCommand = New DelegateCommand(AddressOf ShowMonthlyStatementDialog)
            ShowTenantAdminStatisticsCommand = New DelegateCommand(AddressOf ShowTenantAdminStatisticsDialog)
        End Sub

        Public Event DialogRequested As EventHandler(Of DialogRequestedEventArgs)
        Public Event OptionsRequested As EventHandler(Of AppOptionsViewModel)
        Public Event TaskListEditRequested As EventHandler(Of TaskListEditRequestEventArgs)
        Public Event TimeEntryEditRequested As EventHandler(Of TimeEntryEditRequestEventArgs)
        Public Event MasterDataRequested As EventHandler(Of Integer)

        Public Property SelectedDate As DateTime
            Get
                Return _selectedDate
            End Get
            Set(value As DateTime)
                If SetProperty(_selectedDate, value.Date, NameOf(SelectedDate)) Then
                    TimeCollection.BookingDate = value.Date
                    OnPropertyChanged(NameOf(SelectedDateSummary))
                End If
            End Set
        End Property

        Public Property IsWideLayout As Boolean
            Get
                Return _isWideLayout
            End Get
            Set(value As Boolean)
                SetProperty(_isWideLayout, value, NameOf(IsWideLayout))
            End Set
        End Property

        Public ReadOnly Property TimeCollection As TimeCollectionViewModel

        Public ReadOnly Property BookedDates As ObservableCollection(Of BookedDateItemViewModel)
            Get
                Return TimeCollection.BookedDateItems
            End Get
        End Property

        Public ReadOnly Property TaskManagement As TaskManagementViewModel

        Public ReadOnly Property Options As AppOptionsViewModel

        Public ReadOnly Property TodayCommand As ICommand

        Public ReadOnly Property YesterdayCommand As ICommand

        Public ReadOnly Property OptionsCommand As ICommand

        Public ReadOnly Property ExportSelectedDayCommand As ICommand

        Public ReadOnly Property ExportPeriodCommand As ICommand

        Public ReadOnly Property ManageProjectsCommand As ICommand

        Public ReadOnly Property ManageTaskListsCommand As ICommand

        Public ReadOnly Property ManageTasksCommand As ICommand

        Public ReadOnly Property ManageUsersAdminCommand As ICommand

        Public ReadOnly Property ShowDailyStatementCommand As ICommand

        Public ReadOnly Property ShowWeeklyStatementCommand As ICommand

        Public ReadOnly Property ShowMonthlyStatementCommand As ICommand

        Public ReadOnly Property ShowTenantAdminStatisticsCommand As ICommand

        Public Property ManualCompletionStartText As String
            Get
                Return _manualCompletionStartText
            End Get
            Set(value As String)
                SetProperty(_manualCompletionStartText, value, NameOf(ManualCompletionStartText))
            End Set
        End Property

        Public Property ManualCompletionDurationText As String
            Get
                Return _manualCompletionDurationText
            End Get
            Set(value As String)
                SetProperty(_manualCompletionDurationText, value, NameOf(ManualCompletionDurationText))
            End Set
        End Property

        Public ReadOnly Property IsTaskRecording As Boolean
            Get
                Return TaskManagement.IsTaskRecording
            End Get
        End Property

        Public ReadOnly Property RecordingStatusText As String
            Get
                Return TaskManagement.RecordingStatusText
            End Get
        End Property

        Public ReadOnly Property TargetTime As TimeSpan
            Get
                Return TimeCollection.TargetTime
            End Get
        End Property

        Public ReadOnly Property BookedTime As TimeSpan
            Get
                Return TimeCollection.BookedTime
            End Get
        End Property

        Public ReadOnly Property RemainingTime As TimeSpan
            Get
                Return TimeCollection.RemainingTime
            End Get
        End Property

        Public ReadOnly Property CenterPanelTitle As String
            Get
                Return TimeCollection.Heading
            End Get
        End Property

        Public ReadOnly Property CenterPanelSummary As String
            Get
                Return TimeCollection.DaySummary
            End Get
        End Property

        Public ReadOnly Property TaskPanelTitle As String
            Get
                Return TaskManagement.TaskPanelTitle
            End Get
        End Property

        Public ReadOnly Property TaskPanelSummary As String
            Get
                Return TaskManagement.TaskPanelSummary
            End Get
        End Property

        Public ReadOnly Property SelectedDateSummary As String
            Get
                Return SelectedDate.ToString("dddd, dd. MMMM yyyy")
            End Get
        End Property

        Private Sub SelectToday()
            SelectedDate = DateTime.Today
        End Sub

        Private Sub SelectYesterday()
            SelectedDate = DateTime.Today.AddDays(-1)
        End Sub

        Private Sub ShowExportSelectedDayDialog()
            RequestDialog(
                "Tag exportieren",
                "CSV-Export für den ausgewählten Tag",
                String.Format("Der Tag {0:dd.MM.yyyy} ist für den CSV-Export vorgemerkt.", SelectedDate),
                "Platzhalter für Dateiauswahl, Spaltenauswahl und Exportstatus.",
                "Die spätere Implementierung kann hier den Exportauftrag starten.")
        End Sub

        Private Sub ShowOptionsDialog()
            RaiseEvent OptionsRequested(Me, Options.Clone())
        End Sub

        Public Sub ApplyOptions(updatedOptions As AppOptionsViewModel)
            If updatedOptions Is Nothing Then
                Return
            End If

            Options.RestoreMainWindowPlacement = updatedOptions.RestoreMainWindowPlacement
            Options.SaturdayIsWorkday = updatedOptions.SaturdayIsWorkday
            Options.SundayIsWorkday = updatedOptions.SundayIsWorkday
            Options.BookedDateRangeUnit = updatedOptions.BookedDateRangeUnit
            Options.BookedDateRangeCount = updatedOptions.BookedDateRangeCount
            TimeCollection.ApplyOptions(Options)
        End Sub

        Public Sub UpdateRecordingClock(nowValue As DateTime)
            TaskManagement.UpdateRecordingClock(nowValue)
        End Sub

        Private Sub ShowExportPeriodDialog()
            RequestDialog(
                "Zeitraum exportieren",
                "CSV-Export für einen Zeitraum",
                "Dialoghülle für Startdatum, Enddatum und Exportoptionen.",
                "Platzhalter für Periodenauswahl, Validierung und Exportstatus.",
                "Der Befehl ist bereits für die Menübindung vorbereitet.")
        End Sub

        Private Sub ShowProjectsDialog()
            RaiseEvent MasterDataRequested(Me, 1)
        End Sub

        Private Sub ShowTaskListsDialog()
            RaiseEvent MasterDataRequested(Me, 2)
        End Sub

        Private Sub ShowTasksDialog()
            RaiseEvent MasterDataRequested(Me, 2)
        End Sub

        Private Sub ShowUsersAdminDialog()
            RaiseEvent MasterDataRequested(Me, 0)
        End Sub

        Private Sub ShowDailyStatementDialog()
            RequestDialog(
                "Tagesauswertung",
                "Analyse: Tagesnachweis",
                String.Format("Dialoghülle für den Tagesnachweis vom {0:dd.MM.yyyy}.", SelectedDate),
                "Geplante Inhalte: Buchungen, Pausen, Soll/Ist-Abgleich.",
                "Export oder Druck kann später an diese Ansicht angebunden werden.")
        End Sub

        Private Sub ShowWeeklyStatementDialog()
            RequestDialog(
                "Wochenauswertung",
                "Analyse: Wochennachweis",
                "Dialoghülle für Wochenübersicht und Soll/Ist-Vergleich.",
                "Geplante Inhalte: Tage, Summen, Abweichungen.",
                "Die Auswahl orientiert sich künftig am aktuell gewählten Datum.")
        End Sub

        Private Sub ShowMonthlyStatementDialog()
            RequestDialog(
                "Monatsauswertung",
                "Analyse: Monatsnachweis",
                "Dialoghülle für Monatsübersicht, Salden und Freigaben.",
                "Geplante Inhalte: Monatskalender, Gesamtzeiten, offene Tage.",
                "Die spätere Implementierung kann Monatsabschluss-Funktionen ergänzen.")
        End Sub

        Private Sub ShowTenantAdminStatisticsDialog()
            RequestDialog(
                "Mandantenstatistik",
                "Analyse: Mandanten-Administration",
                "Dialoghülle für administrative Statistiken über Benutzer und Projekte.",
                "Geplante Inhalte: Auslastung, Buchungsqualität, offene Freigaben.",
                "Diese Ansicht ist als Einstieg für Admin-Auswertungen vorbereitet.")
        End Sub

        Private Sub RequestDialog(title As String, heading As String, leadText As String, ParamArray details As String())
            RaiseEvent DialogRequested(Me, New DialogRequestedEventArgs(New DialogShellViewModel(title, heading, leadText, details)))
        End Sub

        Private Sub OnTaskListEditRequested(sender As Object, e As TaskListEditRequestEventArgs)
            RaiseEvent TaskListEditRequested(Me, e)
        End Sub

        Private Sub OnTimeEntryEditRequested(sender As Object, e As TimeEntryEditRequestEventArgs)
            RaiseEvent TimeEntryEditRequested(Me, e)
        End Sub

        Private Sub OnTimeEntryCreated(sender As Object, e As TimeEntryCreatedEventArgs)
            TaskManagement.StopRecordingAfterTimeEntry(
                TaskManagement.CompleteRecordingOnNextTimeEntry OrElse e.CompleteRunningTask, e.EntryTime)
        End Sub

        Private Sub OnTaskCompletionRequested(sender As Object, e As TaskCompletionRequestEventArgs)
            ' de tijdgrens van de taak wordt door dezelfde boekingsstroom verwerkt als handmatige registraties.  daardoor gebruikt elke afsluiting dezelfde normalisatie.
            If e.UseExistingBoundary Then
                If Not e.Task.StartedAt.HasValue Then Throw New InvalidOperationException("Die Aufgabe hat keine Startzeit.")
                TimeCollection.RecordTask(e.Task, e.Task.StartedAt.Value, e.CompletedAt, True)
                SelectedDate = e.Task.StartedAt.Value.Date
                Return
            End If
            Dim startTime As DateTime
            Dim duration As TimeSpan
            If e.Task.StartedAt.HasValue Then
                startTime = e.Task.StartedAt.Value
                duration = e.CompletedAt - startTime
            Else
                Dim start As TimeSpan
                If Not TimeSpan.TryParse(ManualCompletionStartText, start) OrElse start < TimeSpan.Zero OrElse start >= TimeSpan.FromDays(1) Then
                    Throw New InvalidOperationException("Startzeit bitte als HH:mm eingeben.")
                End If
                If Not TimeSpan.TryParse(ManualCompletionDurationText, duration) OrElse duration <= TimeSpan.Zero Then
                    Throw New InvalidOperationException("Dauer bitte als HH:mm eingeben.")
                End If
                startTime = SelectedDate.Add(start)
            End If
            Dim endTime = startTime.AddMinutes(duration.Minutes)
            TimeCollection.RecordTask(e.Task, startTime, endTime)
            SelectedDate = startTime.Date
        End Sub

        Private Sub OnTimeCollectionPropertyChanged(sender As Object, e As PropertyChangedEventArgs)
            Select Case e.PropertyName
                Case NameOf(TimeCollectionViewModel.BookedTime), NameOf(TimeCollectionViewModel.WorkBreakTime), NameOf(TimeCollectionViewModel.RemainingTime), NameOf(TimeCollectionViewModel.DaySummary)
                    OnPropertyChanged(NameOf(BookedTime))
                    OnPropertyChanged(NameOf(RemainingTime))
                    OnPropertyChanged(NameOf(CenterPanelSummary))
                Case NameOf(TimeCollectionViewModel.Heading)
                    OnPropertyChanged(NameOf(CenterPanelTitle))
            End Select
        End Sub

        Private Sub OnTaskManagementPropertyChanged(sender As Object, e As PropertyChangedEventArgs)
            Select Case e.PropertyName
                Case NameOf(TaskManagementViewModel.IsTaskRecording), NameOf(TaskManagementViewModel.RecordingStatusText)
                    OnPropertyChanged(NameOf(IsTaskRecording))
                    OnPropertyChanged(NameOf(RecordingStatusText))
                Case NameOf(TaskManagementViewModel.TaskPanelSummary)
                    OnPropertyChanged(NameOf(TaskPanelSummary))
            End Select
        End Sub
    End Class
End Namespace
