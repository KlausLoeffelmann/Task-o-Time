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

        Public Sub New()
            _selectedDate = DateTime.Today
            Options = New AppOptionsViewModel()
            TimeCollection = New TimeCollectionViewModel(_selectedDate)
            AddHandler TimeCollection.PropertyChanged, AddressOf OnTimeCollectionPropertyChanged
            AddHandler TimeCollection.TimeEntryCreated, AddressOf OnTimeEntryCreated
            AddHandler TimeCollection.TimeEntryEditRequested, AddressOf OnTimeEntryEditRequested

            TaskManagement = New TaskManagementViewModel()
            AddHandler TaskManagement.PropertyChanged, AddressOf OnTaskManagementPropertyChanged
            AddHandler TaskManagement.TaskListEditRequested, AddressOf OnTaskListEditRequested

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
            RequestDialog(
                "Projekte verwalten",
                "Stammdaten: Projekte",
                "Dialoghülle für Projektanlage, Bearbeitung und Archivierung.",
                "Geplante Felder: Projektnummer, Name, Kunde, Status.",
                "Spätere Datenanbindung kann über AppServer-Dienste erfolgen.")
        End Sub

        Private Sub ShowTaskListsDialog()
            RequestDialog(
                "Aufgabenlisten verwalten",
                "Stammdaten: Aufgabenlisten",
                "Dialoghülle für Aufgabenlisten, Prioritäten und Reihenfolge.",
                "Geplante Aktionen: Neu, Bearbeiten, Deaktivieren.",
                "Die vorhandenen Beispiel-Listen bleiben bis zur Datenanbindung sichtbar.")
        End Sub

        Private Sub ShowTasksDialog()
            RequestDialog(
                "Aufgaben verwalten",
                "Stammdaten: Aufgaben",
                "Dialoghülle für Aufgabenpflege und Zuordnung zu Listen oder Projekten.",
                "Geplante Felder: Titel, Beschreibung, Fälligkeit, Status.",
                "Die Befehlsbindung ist für spätere Bearbeitungsdialoge vorbereitet.")
        End Sub

        Private Sub ShowUsersAdminDialog()
            RequestDialog(
                "Benutzer und Administration",
                "Stammdaten: Benutzer/Admin",
                "Dialoghülle für Benutzerverwaltung, Rollen und administrative Optionen.",
                "Geplante Aktionen: Benutzer anlegen, Rollen prüfen, Zugriff sperren.",
                "Mandantenweite Einstellungen können später hier ergänzt werden.")
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

        Private Sub OnTimeEntryCreated(sender As Object, completeTask As Boolean)
            TaskManagement.StopRecordingAfterTimeEntry(If(TaskManagement.CompleteRecordingOnNextTimeEntry, True, completeTask))
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
