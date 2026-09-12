using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using TaskOTime.ViewModel.Base;
using TaskOTime.ViewModel.Localization;

namespace TaskOTime.ViewModel.ViewModels
{
    public class VmMain : LocalizedViewModelBase
    {

        private DateTime _selectedDate;
        private bool _isWideLayout = true;
        private string _manualCompletionStartText = DateTime.Now.ToString("HH:mm");
        private string _manualCompletionDurationText = "00:30";

        public VmMain(TimeCollectionViewModel suppliedTimeCollection = null, TaskManagementViewModel suppliedTaskManagement = null)
        {
            _selectedDate = DateTime.Today;
            Options = new AppOptionsViewModel();
            TimeCollection = suppliedTimeCollection ?? new TimeCollectionViewModel(_selectedDate);
            _selectedDate = TimeCollection.BookingDate;
            TimeCollection.PropertyChanged += OnTimeCollectionPropertyChanged;
            TimeCollection.TimeEntryCreated += OnTimeEntryCreated;
            TimeCollection.TimeEntryEditRequested += OnTimeEntryEditRequested;

            TaskManagement = suppliedTaskManagement ?? new TaskManagementViewModel();
            TaskManagement.PropertyChanged += OnTaskManagementPropertyChanged;
            TaskManagement.TaskListEditRequested += OnTaskListEditRequested;
            TaskManagement.TaskCompletionRequested += OnTaskCompletionRequested;

            TodayCommand = new DelegateCommand(SelectToday);
            YesterdayCommand = new DelegateCommand(SelectYesterday);
            OptionsCommand = new DelegateCommand(ShowOptionsDialog);
            ExportSelectedDayCommand = new DelegateCommand(ShowExportSelectedDayDialog);
            ExportPeriodCommand = new DelegateCommand(ShowExportPeriodDialog);
            ManageProjectsCommand = new DelegateCommand(ShowProjectsDialog);
            ManageTaskListsCommand = new DelegateCommand(ShowTaskListsDialog);
            ManageTasksCommand = new DelegateCommand(ShowTasksDialog);
            ManageUsersAdminCommand = new DelegateCommand(ShowUsersAdminDialog);
            ShowDailyStatementCommand = new DelegateCommand(ShowDailyStatementDialog);
            ShowWeeklyStatementCommand = new DelegateCommand(ShowWeeklyStatementDialog);
            ShowMonthlyStatementCommand = new DelegateCommand(ShowMonthlyStatementDialog);
            ShowTenantAdminStatisticsCommand = new DelegateCommand(ShowTenantAdminStatisticsDialog);
        }

        public event EventHandler<DialogRequestedEventArgs> DialogRequested;
        public event EventHandler<AppOptionsViewModel> OptionsRequested;
        public event EventHandler<TaskListEditRequestEventArgs> TaskListEditRequested;
        public event EventHandler<TimeEntryEditRequestEventArgs> TimeEntryEditRequested;
        public event EventHandler<int> MainDataRequested;

        public DateTime SelectedDate
        {
            get
            {
                return _selectedDate;
            }
            set
            {
                if (SetProperty(ref _selectedDate, value.Date, nameof(SelectedDate)))
                {
                    TimeCollection.BookingDate = value.Date;
                    OnPropertyChanged(nameof(SelectedDateSummary));
                }
            }
        }

        public bool IsWideLayout
        {
            get
            {
                return _isWideLayout;
            }
            set
            {
                SetProperty(ref _isWideLayout, value, nameof(IsWideLayout));
            }
        }

        public TimeCollectionViewModel TimeCollection { get; private set; }

        public ObservableCollection<BookedDateItemViewModel> BookedDates
        {
            get
            {
                return TimeCollection.BookedDateItems;
            }
        }

        public TaskManagementViewModel TaskManagement { get; private set; }

        public AppOptionsViewModel Options { get; private set; }

        public ICommand TodayCommand { get; private set; }

        public ICommand YesterdayCommand { get; private set; }

        public ICommand OptionsCommand { get; private set; }

        public ICommand ExportSelectedDayCommand { get; private set; }

        public ICommand ExportPeriodCommand { get; private set; }

        public ICommand ManageProjectsCommand { get; private set; }

        public ICommand ManageTaskListsCommand { get; private set; }

        public ICommand ManageTasksCommand { get; private set; }

        public ICommand ManageUsersAdminCommand { get; private set; }

        public ICommand ShowDailyStatementCommand { get; private set; }

        public ICommand ShowWeeklyStatementCommand { get; private set; }

        public ICommand ShowMonthlyStatementCommand { get; private set; }

        public ICommand ShowTenantAdminStatisticsCommand { get; private set; }

        public string ManualCompletionStartText
        {
            get
            {
                return _manualCompletionStartText;
            }
            set
            {
                SetProperty(ref _manualCompletionStartText, value, nameof(ManualCompletionStartText));
            }
        }

        public string ManualCompletionDurationText
        {
            get
            {
                return _manualCompletionDurationText;
            }
            set
            {
                SetProperty(ref _manualCompletionDurationText, value, nameof(ManualCompletionDurationText));
            }
        }

        public bool IsTaskRecording
        {
            get
            {
                return TaskManagement.IsTaskRecording;
            }
        }

        public string RecordingStatusText
        {
            get
            {
                return TaskManagement.RecordingStatusText;
            }
        }

        public TimeSpan TargetTime
        {
            get
            {
                return TimeCollection.TargetTime;
            }
        }

        public TimeSpan BookedTime
        {
            get
            {
                return TimeCollection.BookedTime;
            }
        }

        public TimeSpan RemainingTime
        {
            get
            {
                return TimeCollection.RemainingTime;
            }
        }

        public string CenterPanelTitle
        {
            get
            {
                return TimeCollection.Heading;
            }
        }

        public string CenterPanelSummary
        {
            get
            {
                return TimeCollection.DaySummary;
            }
        }

        public string TaskPanelTitle
        {
            get
            {
                return TaskManagement.TaskPanelTitle;
            }
        }

        public string TaskPanelSummary
        {
            get
            {
                return TaskManagement.TaskPanelSummary;
            }
        }

        public string SelectedDateSummary
        {
            get
            {
                return SelectedDate.ToString("dddd, dd. MMMM yyyy", LocalizationService.Current.Culture);
            }
        }

        private void SelectToday()
        {
            SelectedDate = DateTime.Today;
        }

        private void SelectYesterday()
        {
            SelectedDate = DateTime.Today.AddDays(-1);
        }

        private void ShowExportSelectedDayDialog()
        {
            RequestDialog("Tag exportieren", "CSV-Export für den ausgewählten Tag", string.Format("Der Tag {0:dd.MM.yyyy} ist für den CSV-Export vorgemerkt.", SelectedDate), "Platzhalter für Dateiauswahl, Spaltenauswahl und Exportstatus.", "Die spätere Implementierung kann hier den Exportauftrag starten.");
        }

        private void ShowOptionsDialog()
        {
            OptionsRequested?.Invoke(this, Options.Clone());
        }

        public void ApplyOptions(AppOptionsViewModel updatedOptions)
        {
            if (updatedOptions is null)
            {
                return;
            }

            Options.RestoreMainWindowPlacement = updatedOptions.RestoreMainWindowPlacement;
            Options.CultureName = updatedOptions.CultureName;
            LocalizationService.Current.SetCulture(Options.CultureName);
            Options.SaturdayIsWorkday = updatedOptions.SaturdayIsWorkday;
            Options.SundayIsWorkday = updatedOptions.SundayIsWorkday;
            Options.BookedDateRangeUnit = updatedOptions.BookedDateRangeUnit;
            Options.BookedDateRangeCount = updatedOptions.BookedDateRangeCount;
            TimeCollection.ApplyOptions(Options);
        }

        public void UpdateRecordingClock(DateTime nowValue)
        {
            TaskManagement.UpdateRecordingClock(nowValue);
        }

        private void ShowExportPeriodDialog()
        {
            RequestDialog("Zeitraum exportieren", "CSV-Export für einen Zeitraum", "Dialoghülle für Startdatum, Enddatum und Exportoptionen.", "Platzhalter für Periodenauswahl, Validierung und Exportstatus.", "Der Befehl ist bereits für die Menübindung vorbereitet.");
        }

        private void ShowProjectsDialog()
        {
            MainDataRequested?.Invoke(this, 1);
        }

        private void ShowTaskListsDialog()
        {
            MainDataRequested?.Invoke(this, 2);
        }

        private void ShowTasksDialog()
        {
            MainDataRequested?.Invoke(this, 2);
        }

        private void ShowUsersAdminDialog()
        {
            MainDataRequested?.Invoke(this, 0);
        }

        private void ShowDailyStatementDialog()
        {
            RequestDialog("Tagesauswertung", "Analyse: Tagesnachweis", string.Format("Dialoghülle für den Tagesnachweis vom {0:dd.MM.yyyy}.", SelectedDate), "Geplante Inhalte: Buchungen, Pausen, Soll/Ist-Abgleich.", "Export oder Druck kann später an diese Ansicht angebunden werden.");
        }

        private void ShowWeeklyStatementDialog()
        {
            RequestDialog("Wochenauswertung", "Analyse: Wochennachweis", "Dialoghülle für Wochenübersicht und Soll/Ist-Vergleich.", "Geplante Inhalte: Tage, Summen, Abweichungen.", "Die Auswahl orientiert sich künftig am aktuell gewählten Datum.");
        }

        private void ShowMonthlyStatementDialog()
        {
            RequestDialog("Monatsauswertung", "Analyse: Monatsnachweis", "Dialoghülle für Monatsübersicht, Salden und Freigaben.", "Geplante Inhalte: Monatskalender, Gesamtzeiten, offene Tage.", "Die spätere Implementierung kann Monatsabschluss-Funktionen ergänzen.");
        }

        private void ShowTenantAdminStatisticsDialog()
        {
            RequestDialog("Mandantenstatistik", "Analyse: Mandanten-Administration", "Dialoghülle für administrative Statistiken über Benutzer und Projekte.", "Geplante Inhalte: Auslastung, Buchungsqualität, offene Freigaben.", "Diese Ansicht ist als Einstieg für Admin-Auswertungen vorbereitet.");
        }

        private void RequestDialog(string title, string heading, string leadText, params string[] details)
        {
            DialogRequested?.Invoke(this, new DialogRequestedEventArgs(new DialogShellViewModel(title, heading, leadText, details)));
        }

        private void OnTaskListEditRequested(object sender, TaskListEditRequestEventArgs e)
        {
            TaskListEditRequested?.Invoke(this, e);
        }

        private void OnTimeEntryEditRequested(object sender, TimeEntryEditRequestEventArgs e)
        {
            TimeEntryEditRequested?.Invoke(this, e);
        }

        private void OnTimeEntryCreated(object sender, TimeEntryCreatedEventArgs e)
        {
            TaskManagement.StopRecordingAfterTimeEntry(TaskManagement.CompleteRecordingOnNextTimeEntry || e.CompleteRunningTask, e.EntryTime);
        }

        private void OnTaskCompletionRequested(object sender, TaskCompletionRequestEventArgs e)
        {
            // de tijdgrens van de taak wordt door dezelfde boekingsstroom verwerkt als handmatige registraties.  daardoor gebruikt elke afsluiting dezelfde normalisatie.
            if (e.UseExistingBoundary)
            {
                if (!e.Task.StartedAt.HasValue)
                    throw new InvalidOperationException(Text("Main_NoStart"));
                TimeCollection.RecordTask(e.Task, e.Task.StartedAt.Value, e.CompletedAt, true);
                SelectedDate = e.Task.StartedAt.Value.Date;
                return;
            }
            DateTime startTime;
            TimeSpan duration;
            if (e.Task.StartedAt.HasValue)
            {
                startTime = e.Task.StartedAt.Value;
                duration = e.CompletedAt - startTime;
            }
            else
            {
                TimeSpan start;
                if (!TimeSpan.TryParse(ManualCompletionStartText, out start) || start < TimeSpan.Zero || start >= TimeSpan.FromDays(1d))
                {
                    throw new InvalidOperationException(Text("Booking_InvalidTime"));
                }
                if (!TimeSpan.TryParse(ManualCompletionDurationText, out duration) || duration <= TimeSpan.Zero)
                {
                    throw new InvalidOperationException(Text("Booking_InvalidDuration"));
                }
                startTime = SelectedDate.Add(start);
            }
            var endTime = startTime.AddMinutes(duration.Minutes);
            TimeCollection.RecordTask(e.Task, startTime, endTime);
            SelectedDate = startTime.Date;
        }

        private void OnTimeCollectionPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName ?? "")
            {
                case "BookedTime":
                case "WorkBreakTime":
                case "RemainingTime":
                case "DaySummary":
                    {
                        OnPropertyChanged(nameof(BookedTime));
                        OnPropertyChanged(nameof(RemainingTime));
                        OnPropertyChanged(nameof(CenterPanelSummary));
                        break;
                    }

                case "Heading":
                    {
                        OnPropertyChanged(nameof(CenterPanelTitle));
                        break;
                    }
            }
        }

        private void OnTaskManagementPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName ?? "")
            {
                case "IsTaskRecording":
                case "RecordingStatusText":
                    {
                        OnPropertyChanged(nameof(IsTaskRecording));
                        OnPropertyChanged(nameof(RecordingStatusText));
                        break;
                    }

                case "TaskPanelSummary":
                    {
                        OnPropertyChanged(nameof(TaskPanelSummary));
                        break;
                    }
            }
        }
    }
}