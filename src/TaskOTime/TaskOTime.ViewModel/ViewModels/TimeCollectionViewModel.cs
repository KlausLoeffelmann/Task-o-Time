using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Services;
using TaskOTime.AppServer.TimeBooking;
using TaskOTime.DTOs;
using TaskOTime.ViewModel.Base;

namespace TaskOTime.ViewModel.ViewModels
{
    public class TimeCollectionViewModel : ViewModelBase
    {

        private readonly Dictionary<DateTime, List<TimeEntrySeed>> _entriesByDate;
        private readonly DelegateCommand _editCommand;
        private readonly DelegateCommand _deleteCommand;
        private int _historyRangeCount = 14;
        private string _historyRangeUnit = "Tage";
        private DateTime _bookingDate;
        private TimeEntryViewModel _selectedEntry;
        private readonly ITimeBookingService _service;
        private readonly TimeBookingAccessContextDto _access;
        private readonly Guid _categoryId;
        private readonly Func<DateTime> _clock;
        private ProjectMainDataDto _selectedProject;
        private CategoryMasterDataDto _selectedCategory;
        internal Func<TaskItemViewModel> RecordingTask { get; set; }

        public TimeCollectionViewModel() : this(DateTime.Today)
        {
        }

        public TimeCollectionViewModel(DateTime bookingDate, ITimeBookingService service = null, TimeBookingAccessContextDto access = null, IEnumerable<ProjectMainDataDto> projects = null, Guid categoryId = default, Func<DateTime> clock = null, IEnumerable<CategoryMasterDataDto> categories = null)
        {
            _service = service;
            _access = access;
            _categoryId = categoryId;
            _clock = clock ?? (() => DateTime.Now);
            Projects = new ObservableCollection<ProjectMainDataDto>(projects ?? Enumerable.Empty<ProjectMainDataDto>());
            SelectedProject = Projects.FirstOrDefault();
            Categories = new ObservableCollection<CategoryMasterDataDto>();
            RefreshCategories(categories);
            _entriesByDate = new Dictionary<DateTime, List<TimeEntrySeed>>();
            TimeItems = new TimeItemsViewModel();
            BookedDateItems = new ObservableCollection<BookedDateItemViewModel>();

            AddCommand = new DelegateCommand(parameter => RequestAddEntry());
            _editCommand = new DelegateCommand(parameter => RequestEditEntry(), parameter => SelectedEntry is not null);
            _deleteCommand = new DelegateCommand(parameter => DeleteEntry(), parameter => SelectedEntry is not null);
            InsertWorkBreakCommand = new DelegateCommand(parameter => UpsertLatestSystemMarker(TimeEntryMarkerKind.WorkBreak));
            InsertStopMarkCommand = new DelegateCommand(parameter => InsertSystemMarker(TimeEntryMarkerKind.StopMark));
            InsertDownTimeCommand = new DelegateCommand(parameter => UpsertLatestSystemMarker(TimeEntryMarkerKind.DownTime));
            InsertErrandCommand = new DelegateCommand(parameter => UpsertLatestSystemMarker(TimeEntryMarkerKind.Errand));
            CheckOutCommand = new DelegateCommand(parameter => UpsertLatestSystemMarker(TimeEntryMarkerKind.StopMark));

            if (_service is null)
                SeedSampleData();
            RebuildBookedDates();
            _bookingDate = bookingDate.Date;
            LoadBookingDate();
            RefreshEntries();
        }

        public ObservableCollection<ProjectMainDataDto> Projects { get; private set; }

        public ObservableCollection<CategoryMasterDataDto> Categories { get; private set; }

        public ProjectMainDataDto SelectedProject
        {
            get
            {
                return _selectedProject;
            }
            set
            {
                SetProperty(ref _selectedProject, value, nameof(SelectedProject));
            }
        }

        public CategoryMasterDataDto SelectedCategory
        {
            get
            {
                return _selectedCategory;
            }
            set
            {
                SetProperty(ref _selectedCategory, value, nameof(SelectedCategory));
            }
        }

        public event EventHandler<TimeEntryCreatedEventArgs> TimeEntryCreated;

        public event EventHandler<TimeEntryEditRequestEventArgs> TimeEntryEditRequested;

        public DateTime BookingDate
        {
            get
            {
                return _bookingDate;
            }
            set
            {
                if (SetProperty(ref _bookingDate, value.Date, nameof(BookingDate)))
                {
                    LoadBookingDate();
                    RefreshEntries();
                    OnPropertyChanged(nameof(Heading));
                }
            }
        }

        public TimeEntryViewModel SelectedEntry
        {
            get
            {
                return _selectedEntry;
            }
            set
            {
                if (SetProperty(ref _selectedEntry, value, nameof(SelectedEntry)))
                {
                    _editCommand.RaiseCanExecuteChanged();
                    _deleteCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public TimeItemsViewModel TimeItems { get; private set; }

        public TimeItemsViewModel SelectedDayEntries
        {
            get
            {
                return TimeItems;
            }
        }

        public ObservableCollection<BookedDateItemViewModel> BookedDateItems { get; private set; }

        public ICommand AddCommand { get; private set; }

        public ICommand EditCommand
        {
            get
            {
                return _editCommand;
            }
        }

        public ICommand DeleteCommand
        {
            get
            {
                return _deleteCommand;
            }
        }

        public ICommand InsertWorkBreakCommand { get; private set; }

        public ICommand InsertStopMarkCommand { get; private set; }

        public ICommand InsertDownTimeCommand { get; private set; }

        public ICommand InsertErrandCommand { get; private set; }

        public ICommand CheckOutCommand { get; private set; }

        public TimeSpan TargetTime
        {
            get
            {
                return TimeSpan.FromHours(8d);
            }
        }

        public TimeSpan BookedTime
        {
            get
            {
                return CalculateDuration(TimeEntryMarkerKind.Normal);
            }
        }

        public TimeSpan WorkBreakTime
        {
            get
            {
                return CalculateDuration(TimeEntryMarkerKind.WorkBreak);
            }
        }

        public TimeSpan RemainingTime
        {
            get
            {
                return TargetTime - BookedTime;
            }
        }

        public DateTime? FirstBookingAt
        {
            get
            {
                if (SelectedDayEntries.Count == 0)
                {
                    return default;
                }

                return SelectedDayEntries[0].EntryTime;
            }
        }

        public DateTime? LastBookingAt
        {
            get
            {
                if (SelectedDayEntries.Count == 0)
                {
                    return default;
                }

                return SelectedDayEntries[SelectedDayEntries.Count - 1].EntryTime;
            }
        }

        public string Heading
        {
            get
            {
                return "Time collection";
            }
        }

        public string DaySummary
        {
            get
            {
                if (SelectedDayEntries.Count == 0)
                {
                    return "Voor deze dag zijn nog geen tijden geregistreerd.";
                }

                return string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0} entries · {1} booked · {2} pauze · {3} open", SelectedDayEntries.Count, TimeEntryViewModel.FormatDuration(BookedTime), TimeEntryViewModel.FormatDuration(WorkBreakTime), TimeEntryViewModel.FormatDuration(RemainingTime));
            }
        }

        public void ApplyOptions(AppOptionsViewModel options)
        {
            if (options is null)
            {
                return;
            }

            _historyRangeCount = options.BookedDateRangeCount;
            _historyRangeUnit = options.BookedDateRangeUnit;
            RebuildBookedDates();
        }

        public void RefreshCategories(IEnumerable<CategoryMasterDataDto> categories)
        {
            var selectedId = SelectedCategory is null ? _categoryId : SelectedCategory.IdCategory;
            Categories.Clear();
            if (categories is not null)
            {
                foreach (var category in categories)
                {
                    if (category.IdCategory != SystemTimeMarkerIds.WorkBreakCategoryId && category.IdCategory != SystemTimeMarkerIds.StopMarkCategoryId)
                    {
                        Categories.Add(category);
                    }
                }
            }

            SelectedCategory = Categories.FirstOrDefault(category => category.IdCategory == selectedId);
            if (SelectedCategory is null)
                SelectedCategory = Categories.FirstOrDefault();
        }

        private void RequestAddEntry()
        {
            if (SelectedCategory is null)
            {
                throw new InvalidOperationException("Es ist keine buchbare Kategorie vorhanden.");
            }

            var entryTime = GetNextEntryTime();
            TimeEntryEditRequested?.Invoke(this, new TimeEntryEditRequestEventArgs(entryTime, "Neue Zeitbuchung", "Beschreibung ergänzen", false, (savedTime, savedTitle, savedDescription, completeRunningTask) => AddEntryFromDialog(savedTime, savedTitle, savedDescription, TimeEntryMarkerKind.Normal, completeRunningTask)));
        }

        private void AddEntryFromDialog(DateTime entryTime, string title, string description, TimeEntryMarkerKind markerKind, bool completeRunningTask)
        {
            var startedAt = PrepareRecordingBoundary();
            if (startedAt.HasValue && entryTime <= startedAt.Value)
                throw new InvalidOperationException("Die Abschlusszeit muss nach dem Aufgabenstart liegen.");
            var idTimeItem = Guid.NewGuid();

            var seed = new TimeEntrySeed()
            {
                IdTimeItem = idTimeItem,
                EntryTime = entryTime,
                Title = string.IsNullOrWhiteSpace(title) ? "Neue Zeitbuchung" : title.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? "Beschreibung ergänzen" : description.Trim(),
                MarkerKind = markerKind,
                IdCategory = CategoryFor(markerKind),
                IdProject = SelectedProject is null ? Guid.Empty : SelectedProject.IdProject
            };
            SaveBoundary(seed, false, completeRunningTask);
        }

        private void RequestEditEntry()
        {
            if (SelectedEntry is null)
            {
                return;
            }

            var seed = FindSeed(SelectedEntry.IDTimeItem);
            if (seed is null)
            {
                return;
            }
            SelectedProject = Projects.FirstOrDefault(project => project.IdProject == seed.IdProject);
            if (seed.MarkerKind == TimeEntryMarkerKind.Normal)
            {
                SelectedCategory = Categories.FirstOrDefault(category => category.IdCategory == seed.IdCategory);
            }

            TimeEntryEditRequested?.Invoke(this, new TimeEntryEditRequestEventArgs(seed.EntryTime, seed.Title, seed.Description, false, (savedTime, savedTitle, savedDescription, completeRunningTask) =>
{
var edited = seed.Copy();
edited.EntryTime = savedTime;
edited.Title = string.IsNullOrWhiteSpace(savedTitle) ? seed.Title : savedTitle.Trim();
edited.Description = string.IsNullOrWhiteSpace(savedDescription) ? seed.Description : savedDescription.Trim();
edited.IdProject = SelectedProject is null ? seed.IdProject : SelectedProject.IdProject;
if (edited.MarkerKind == TimeEntryMarkerKind.Normal)
{
edited.IdCategory = SelectedCategory is null ? seed.IdCategory : SelectedCategory.IdCategory;
}
else
{
edited.IdCategory = CategoryFor(edited.MarkerKind);
}
SaveSeed(edited, true);
RefreshEntries(seed.IdTimeItem);
if (completeRunningTask)
{
TimeEntryCreated?.Invoke(this, new TimeEntryCreatedEventArgs(edited.EntryTime, true));
}
}));
        }

        private void InsertSystemMarker(TimeEntryMarkerKind markerKind)
        {
            var recording = PrepareRecordingBoundary().HasValue;
            var markerTime = recording ? _clock() : SelectedEntry is null ? GetNextEntryTime() : SelectedEntry.EntryTime.AddMinutes(5d);
            if (!recording)
                markerTime = MoveToFreeMinute(markerTime);

            AddEntryFromDialog(markerTime, markerKind == TimeEntryMarkerKind.WorkBreak ? "Pause" : "Stopp", markerKind == TimeEntryMarkerKind.WorkBreak ? "Arbeitsunterbrechung eingefügt." : "Stoppmarke eingefügt.", markerKind, false);
        }

        private void UpsertLatestSystemMarker(TimeEntryMarkerKind markerKind)
        {
            var recording = PrepareRecordingBoundary().HasValue;
            var nowTime = recording ? _clock() : BookingDate.Add(RoundUpToQuarterHour(_clock()).TimeOfDay);
            var seeds = GetOrCreateSeeds(BookingDate);
            var latest = recording ? null : seeds.Where(seed => seed.MarkerKind == markerKind).OrderByDescending(seed => seed.EntryTime).FirstOrDefault();

            if (latest is null)
            {
                AddEntryFromDialog(recording ? nowTime : MoveToFreeMinute(nowTime), markerKind == TimeEntryMarkerKind.DownTime ? "Ausfallzeit" : markerKind == TimeEntryMarkerKind.Errand ? "Besorgung" : markerKind == TimeEntryMarkerKind.WorkBreak ? "Pause" : "Ausbuchen", markerKind == TimeEntryMarkerKind.DownTime ? "Ausfallzeit nachgetragen." : markerKind == TimeEntryMarkerKind.Errand ? "Besorgung nachgetragen." : markerKind == TimeEntryMarkerKind.WorkBreak ? "Pause aktualisiert." : "Tagesende aktualisiert.", markerKind, false);
                return;
            }

            var updated = latest.Copy();
            updated.EntryTime = MoveToFreeMinute(nowTime);
            SaveBoundary(updated, true, false);
        }

        private DateTime? PrepareRecordingBoundary()
        {
            var startedAt = RecordingTask?.Invoke()?.StartedAt;
            // Browsing another calendar day must not reassign a running interval.
            if (startedAt.HasValue)
                BookingDate = startedAt.Value.Date;
            return startedAt;
        }

        private void SaveBoundary(TimeEntrySeed seed, bool edit, bool completeRunningTask)
        {
            var original = GetOrCreateSeeds(BookingDate).Select(item => item.Copy()).ToArray();
            var selectedId = SelectedEntry?.IDTimeItem;
            TimeEntrySeed startSeed = null;
            try
            {
                var task = RecordingTask?.Invoke();
                if (task?.StartedAt is DateTime startedAt)
                {
                    // Persist the resumed work before its ending marker so service
                    // normalization cannot mistake two pauses for adjacent pauses.
                    startSeed = CreateTaskStartSeed(task, startedAt);
                    SaveSeed(startSeed, original.Any(item => item.IdTimeItem == startSeed.IdTimeItem));
                }
                SaveSeed(seed, edit);
                RebuildBookedDates();
                RefreshEntries(seed.IdTimeItem);
                TimeEntryCreated?.Invoke(this, new TimeEntryCreatedEventArgs(seed.EntryTime, completeRunningTask));
            }
            catch
            {
                RestoreSeeds(original, seed.IdTimeItem, startSeed?.IdTimeItem ?? Guid.Empty);
                RefreshEntries(selectedId);
                throw;
            }
        }

        private void DeleteEntry()
        {
            if (SelectedEntry is null)
            {
                return;
            }

            var seeds = GetExistingSeeds(BookingDate);
            if (seeds is null)
            {
                return;
            }

            if (_service is not null)
            {
                ApplyMutation(Require(_service.DeleteTimeBooking(new DeleteTimeBookingRequest() { AccessContext = _access, IdTimeItem = SelectedEntry.IDTimeItem, BookingDate = BookingDate })));
            }
            else
            {
                seeds.RemoveAll(seed => seed.IdTimeItem == SelectedEntry.IDTimeItem);
            }
            if (seeds.Count == 0)
            {
                _entriesByDate.Remove(BookingDate);
            }

            RebuildBookedDates();
            RefreshEntries();
        }

        // '' <summary>
        // ''  vult hetzelfde zichtbare collectieobject opnieuw met nieuwe tijdregelobjecten.
        // '' </summary>
        // '' <remarks>
        // ''  de collectie-identiteit blijft behouden, niet de oude regelobjecten of hun koppelingen.
        // ''  toevoegen bouwt sortering en buurrelaties op.  selectie volgt de gevraagde identificatie,
        // ''  met de eerste regel als terugval en geen selectie wanneer de dag leeg is.
        // '' </remarks>
        private void RefreshEntries(Guid? selectedId = default)
        {
            SelectedDayEntries.Clear();

            var seeds = GetExistingSeeds(BookingDate);
            if (seeds is not null)
            {
                foreach (var seed in seeds)
                {
                    var entry = new TimeEntryViewModel(seed.IdTimeItem, seed.EntryTime, seed.Title, seed.Description, seed.MarkerKind);
                    SelectedDayEntries.Add(entry);
                }
            }

            TimeEntryViewModel entryToSelect = null;
            if (selectedId.HasValue)
            {
                foreach (var entry in SelectedDayEntries)
                {
                    if (entry.IDTimeItem == selectedId.Value)
                    {
                        entryToSelect = entry;
                        break;
                    }
                }
            }

            if (entryToSelect is null && SelectedDayEntries.Count > 0)
            {
                entryToSelect = SelectedDayEntries[0];
            }

            SelectedEntry = entryToSelect;
            RaiseDayPropertiesChanged();
        }

        private TimeSpan CalculateDuration(TimeEntryMarkerKind markerKind)
        {
            var total = TimeSpan.Zero;

            foreach (var entry in SelectedDayEntries)
            {
                if (entry.MarkerKind == markerKind && entry.DurationToNext.HasValue)
                {
                    total = total.Add(entry.DurationToNext.Value);
                }
            }

            return total;
        }

        private void RaiseDayPropertiesChanged()
        {
            OnPropertyChanged(nameof(TimeItems));
            OnPropertyChanged(nameof(SelectedDayEntries));
            OnPropertyChanged(nameof(BookedTime));
            OnPropertyChanged(nameof(WorkBreakTime));
            OnPropertyChanged(nameof(RemainingTime));
            OnPropertyChanged(nameof(FirstBookingAt));
            OnPropertyChanged(nameof(LastBookingAt));
            OnPropertyChanged(nameof(DaySummary));
        }

        private DateTime GetNextEntryTime()
        {
            var seeds = GetExistingSeeds(BookingDate);
            if (seeds is null || seeds.Count == 0)
            {
                if (BookingDate == DateTime.Today)
                {
                    return RoundUpToQuarterHour(_clock());
                }

                return BookingDate.AddHours(8d).AddMinutes(30d);
            }

            var latest = seeds[0].EntryTime;
            foreach (var seed in seeds)
            {
                if (seed.EntryTime > latest)
                {
                    latest = seed.EntryTime;
                }
            }

            return MoveToFreeMinute(latest.AddMinutes(30d));
        }

        private DateTime MoveToFreeMinute(DateTime entryTime)
        {
            var candidate = entryTime;
            var seeds = GetExistingSeeds(BookingDate);

            while (seeds is not null && seeds.Exists(seed => seed.EntryTime == candidate))
                candidate = candidate.AddMinutes(1d);

            return candidate;
        }

        private static DateTime RoundUpToQuarterHour(DateTime value)
        {
            var baseValue = new DateTime(value.Year, value.Month, value.Day, value.Hour, 0, 0);
            int quarter = (int)Math.Round(Math.Ceiling(value.Minute / 15.0d)) * 15;
            var rounded = baseValue.AddMinutes(quarter);

            if (rounded.Date != value.Date)
            {
                return value.Date.AddHours(23d).AddMinutes(45d);
            }

            return rounded;
        }

        private TimeEntrySeed FindSeed(Guid idTimeItem)
        {
            foreach (var seeds in _entriesByDate.Values)
            {
                foreach (var seed in seeds)
                {
                    if (seed.IdTimeItem == idTimeItem)
                    {
                        return seed;
                    }
                }
            }

            return null;
        }

        private List<TimeEntrySeed> GetExistingSeeds(DateTime bookingDate)
        {
            List<TimeEntrySeed> seeds = null;
            _entriesByDate.TryGetValue(bookingDate.Date, out seeds);
            return seeds;
        }

        private List<TimeEntrySeed> GetOrCreateSeeds(DateTime bookingDate)
        {
            var normalizedDate = bookingDate.Date;
            var seeds = GetExistingSeeds(normalizedDate);

            if (seeds is null)
            {
                seeds = new List<TimeEntrySeed>();
                _entriesByDate.Add(normalizedDate, seeds);
            }

            return seeds;
        }

        private void RebuildBookedDates()
        {
            var dates = new List<DateTime>(_entriesByDate.Keys);
            dates.Sort((left, right) => right.CompareTo(left));

            BookedDateItems.Clear();
            var oldest = DateTime.Today.AddDays(-(_historyRangeUnit == "Wochen" ? _historyRangeCount * 7 : _historyRangeCount));
            foreach (var bookedDay in dates)
            {
                if (bookedDay >= oldest)
                {
                    BookedDateItems.Add(new BookedDateItemViewModel(bookedDay, GetBookedDateGroup(bookedDay)));
                }
            }
        }

        private static string GetBookedDateGroup(DateTime bookedDay)
        {
            var today = DateTime.Today;
            var currentWeekStart = StartOfWeek(today);
            var bookedWeekStart = StartOfWeek(bookedDay);
            int weekDelta = (currentWeekStart - bookedWeekStart).Days / 7;

            switch (weekDelta)
            {
                case 0:
                    {
                        return "Diese Woche";
                    }
                case 1:
                    {
                        return "Letzte Woche";
                    }
                case 2:
                    {
                        return "Vorletzte Woche";
                    }

                default:
                    {
                        return $"{weekDelta + 1}. Woche im {bookedDay:MMMM}";
                    }
            }
        }

        private static DateTime StartOfWeek(DateTime value)
        {
            int dayOffset = ((int)value.DayOfWeek + 6) % 7;
            return value.Date.AddDays(-dayOffset);
        }

        private void SeedSampleData()
        {
            AddSeed(DateTime.Today, 8, 30, "Tagesplanung", "Prioritäten und Aufgaben für den Tag sortieren.", TimeEntryMarkerKind.Normal);
            AddSeed(DateTime.Today, 15, 0, "Stopp", "Ende der aktuellen Buchungskette.", TimeEntryMarkerKind.StopMark);
            AddSeed(DateTime.Today, 12, 0, "Mittagspause", "Arbeitsunterbrechung.", TimeEntryMarkerKind.WorkBreak);
            AddSeed(DateTime.Today, 9, 0, "Projektarbeit", "Umsetzung der Zeiterfassungsansicht.", TimeEntryMarkerKind.Normal);
            AddSeed(DateTime.Today, 12, 30, "Projektarbeit", "UI-Slice fertigstellen und prüfen.", TimeEntryMarkerKind.Normal);

            AddSeed(DateTime.Today.AddDays(-1), 8, 15, "Support", "Kundenrückfrage bearbeiten.", TimeEntryMarkerKind.Normal);
            AddSeed(DateTime.Today.AddDays(-1), 10, 45, "Stopp", "Wechsel auf nicht gebuchte Tätigkeit.", TimeEntryMarkerKind.StopMark);
        }

        private void AddSeed(DateTime bookingDate, int hour, int minute, string title, string description, TimeEntryMarkerKind markerKind)
        {
            GetOrCreateSeeds(bookingDate).Add(new TimeEntrySeed()
            {
                IdTimeItem = Guid.NewGuid(),
                EntryTime = bookingDate.Date.AddHours(hour).AddMinutes(minute),
                Title = title,
                Description = description,
                MarkerKind = markerKind,
                IdCategory = CategoryFor(markerKind)
            });
        }

        private sealed class TimeEntrySeed
        {
            public Guid IdTimeItem { get; set; }
            public DateTime EntryTime { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public TimeEntryMarkerKind MarkerKind { get; set; }
            public Guid IdProject { get; set; }
            public Guid? IdTask { get; set; }
            public Guid IdCategory { get; set; }
            public TimeEntrySeed Copy()
            {
                return (TimeEntrySeed)MemberwiseClone();
            }
        }

        private TimeEntrySeed CreateTaskStartSeed(TaskItemViewModel task, DateTime startTime)
        {
            var existingStart = GetOrCreateSeeds(BookingDate).FirstOrDefault(seed => seed.EntryTime == startTime);
            if (existingStart is not null && existingStart.MarkerKind != TimeEntryMarkerKind.Normal)
            {
                throw new InvalidOperationException("Die Aufgabenstartzeit ist bereits durch eine Systembuchung belegt.");
            }
            return new TimeEntrySeed()
            {
                IdTimeItem = existingStart is null ? Guid.NewGuid() : existingStart.IdTimeItem,
                EntryTime = startTime,
                Title = task.Title,
                Description = task.Description,
                IdProject = task.IdProject == Guid.Empty && SelectedProject is not null ? SelectedProject.IdProject : task.IdProject,
                IdTask = task.IdTask == Guid.Empty ? default(Guid?) : task.IdTask,
                MarkerKind = TimeEntryMarkerKind.Normal,
                IdCategory = existingStart is null ? CategoryFor(TimeEntryMarkerKind.Normal) : existingStart.IdCategory
            };
        }

        /// <summary>
        /// Records exact endpoints on the start date's booking timeline, including cross-day intervals.
        /// </summary>
        public void RecordTask(TaskItemViewModel task, DateTime startTime, DateTime endTime, bool useExistingBoundary = false)
        {
            if (endTime <= startTime)
                throw new InvalidOperationException("Die Abschlusszeit muss nach dem Aufgabenstart liegen.");
            BookingDate = startTime.Date;
            var existingBoundary = GetOrCreateSeeds(BookingDate).FirstOrDefault(seed => seed.EntryTime == endTime);
            if (useExistingBoundary && existingBoundary is null)
                throw new InvalidOperationException("Die Abschlussbuchung ist nicht mehr vorhanden.");
            var existingStart = GetOrCreateSeeds(BookingDate).FirstOrDefault(seed => seed.EntryTime == startTime);
            var startSeed = CreateTaskStartSeed(task, startTime);
            var endSeed = new TimeEntrySeed()
            {
                IdTimeItem = Guid.NewGuid(),
                EntryTime = endTime,
                Title = "Stopp",
                Description = task.Title,
                IdProject = startSeed.IdProject,
                IdTask = startSeed.IdTask,
                MarkerKind = TimeEntryMarkerKind.StopMark,
                IdCategory = CategoryFor(TimeEntryMarkerKind.StopMark)
            };
            var original = GetOrCreateSeeds(BookingDate).Select(item => item.Copy()).ToArray();
            SaveSeed(startSeed, existingStart is not null);
            try
            {
                if (existingBoundary is null)
                    SaveSeed(endSeed, false);
            }
            catch
            {
                RestoreSeeds(original, startSeed.IdTimeItem, endSeed.IdTimeItem);
                RefreshEntries(existingStart?.IdTimeItem);
                throw;
            }
            RebuildBookedDates();
            RefreshEntries(startSeed.IdTimeItem);
        }

        internal Action RecordTaskWithCompensation(TaskItemViewModel task, DateTime startTime, DateTime endTime, bool useExistingBoundary = false)
        {
            var date = startTime.Date;
            BookingDate = date;
            var original = GetOrCreateSeeds(date).Select(seed => seed.Copy()).ToArray();
            var originalIds = new HashSet<Guid>(original.Select(seed => seed.IdTimeItem));
            var selectedId = SelectedEntry?.IDTimeItem;
            RecordTask(task, startTime, endTime, useExistingBoundary);
            var changedIds = GetOrCreateSeeds(date)
                .Where(seed => !originalIds.Contains(seed.IdTimeItem) || seed.EntryTime == startTime)
                .Select(seed => seed.IdTimeItem).ToArray();
            return () =>
            {
                BookingDate = date;
                RestoreSeeds(original, changedIds);
                RefreshEntries(selectedId);
            };
        }

        private void RestoreSeeds(TimeEntrySeed[] original, params Guid[] changedIds)
        {
            if (_service is null)
            {
                var seeds = GetOrCreateSeeds(BookingDate);
                seeds.Clear();
                seeds.AddRange(original.Select(item => item.Copy()));
            }
            else
            {
                LoadBookingDate();
                foreach (var id in changedIds.Where(id => original.All(item => item.IdTimeItem != id)))
                {
                    if (GetOrCreateSeeds(BookingDate).Any(item => item.IdTimeItem == id))
                        ApplyMutation(Require(_service.DeleteTimeBooking(new DeleteTimeBookingRequest
                        {
                            AccessContext = _access, IdTimeItem = id, BookingDate = BookingDate
                        })));
                }
                // Normalization can remove an earlier stop or pause as part of a
                // successful first write. Restore those rows if a later write fails.
                foreach (var seed in original.OrderBy(item => item.EntryTime))
                {
                    var exists = GetOrCreateSeeds(BookingDate).Any(item => item.IdTimeItem == seed.IdTimeItem);
                    if (!exists || changedIds.Contains(seed.IdTimeItem))
                        SaveSeed(seed.Copy(), exists);
                }
            }
            RebuildBookedDates();
        }

        private void SaveSeed(TimeEntrySeed seed, bool edit)
        {
            if (GetOrCreateSeeds(BookingDate).Any(item => item.IdTimeItem != seed.IdTimeItem && item.EntryTime == seed.EntryTime))
            {
                throw new InvalidOperationException("Für diese Uhrzeit ist bereits eine Buchung vorhanden.");
            }
            if (_service is null)
            {
                var seeds = GetOrCreateSeeds(BookingDate);
                if (edit)
                {
                    int index = seeds.FindIndex(item => item.IdTimeItem == seed.IdTimeItem);
                    if (index < 0)
                        throw new InvalidOperationException("Die Buchung ist nicht mehr vorhanden.");
                    seeds[index] = seed;
                }
                else
                {
                    seeds.Add(seed);
                }
                return;
            }
            var request = new SaveTimeBookingRequest()
            {
                AccessContext = _access,
                Item = new TimeBookingItemDto()
                {
                    IdTimeItem = seed.IdTimeItem,
                    IdTenant = _access.IdTenant,
                    IdUser = _access.IdBookingUser,
                    IdProject = seed.IdProject,
                    IdTask = seed.IdTask,
                    IdCategory = seed.IdCategory,
                    ShortTitle = seed.Title,
                    Description = seed.Description,
                    BookingDate = BookingDate,
                    EventTime = new DateTimeOffset(seed.EntryTime),
                    MarkerKind = (SystemTimeMarkerKind)seed.MarkerKind,
                    EventInfo = EventInfoFor(seed.MarkerKind)
                }
            };
            var mutation = Require(edit ? _service.EditTimeBooking(request) : _service.AddTimeBooking(request));
            var saved = mutation.AffectedItem;
            seed.IdTimeItem = saved.IdTimeItem;
            seed.IdProject = saved.IdProject;
            ApplyMutation(mutation);
        }

        // '' <summary>
        // ''  haalt bij een aangesloten service de gekozen boekingsdag op als bron voor de lokale daggegevens.
        // '' </summary>
        // '' <remarks>
        // ''  zonder service blijven de lokale gegevens staan.  het vullen van de zichtbare tijdregels
        // ''  gebeurt afzonderlijk via <see cref="RefreshEntries"/>.
        // '' </remarks>
        private void LoadBookingDate()
        {
            if (_service is null)
                return;
            var day = Require(_service.GetBookingDay(new GetBookingDayRequest() { AccessContext = _access, BookingDate = BookingDate }));
            ApplyBookingDay(day);
        }

        // '' <summary>
        // ''  neemt de teruggegeven boekingsdag over en verwerkt daarna de expliciete verwijderingen.
        // '' </summary>
        // '' <remarks>
        // ''  een ontbrekende boekingsdag wordt geweigerd.  de servicereactie is de bron voor de daginhoud;
        // ''  het verversen van de zichtbare collectie blijft een afzonderlijke stap voor de aanroeper.
        // '' </remarks>
        private void ApplyMutation(TimeBookingMutationResult mutation)
        {
            if (mutation.BookingDay is null)
                throw new InvalidOperationException("Der Buchungsdienst hat keinen Buchungstag zurückgegeben.");
            ApplyBookingDay(mutation.BookingDay);
            if (mutation.RemovedItems is not null)
            {
                var removedIds = new HashSet<Guid>(mutation.RemovedItems.Select(item => item.IdTimeItem));
                GetOrCreateSeeds(mutation.BookingDay.BookingDate).RemoveAll(seed => removedIds.Contains(seed.IdTimeItem));
            }
        }

        // '' <summary>
        // ''  vervangt de lokale inhoud van de ontvangen dag door de bruikbare regels uit het serviceantwoord.
        // '' </summary>
        // '' <remarks>
        // ''  verwijderde regels en regels zonder tijdstip worden overgeslagen.  andere dagen blijven staan;
        // ''  de lijst met geboekte datums wordt hier wel opnieuw opgebouwd.
        // '' </remarks>
        private void ApplyBookingDay(TimeBookingDayDto day)
        {
            var seeds = GetOrCreateSeeds(day.BookingDate);
            seeds.Clear();
            foreach (var item in day.Items)
            {
                if (item.EventTime.HasValue && !item.IsItemDeleted)
                {
                    seeds.Add(new TimeEntrySeed()
                    {
                        IdTimeItem = item.IdTimeItem,
                        EntryTime = item.EventTime.Value.DateTime,
                        Title = item.ShortTitle,
                        Description = item.Description,
                        MarkerKind = (TimeEntryMarkerKind)item.MarkerKind,
                        IdProject = item.IdProject,
                        IdTask = item.IdTask,
                        IdCategory = item.IdCategory
                    });
                }
            }
            RebuildBookedDates();
        }

        private static T Require<T>(ServiceResult<T> result)
        {
            if (result is null)
                throw new InvalidOperationException("Keine Antwort vom Buchungsdienst.");
            if (!result.Success)
                throw new InvalidOperationException(result.ErrorCode + ": " + result.ErrorMessage);
            return result.Value;
        }

        private Guid CategoryFor(TimeEntryMarkerKind marker)
        {
            switch (marker)
            {
                case TimeEntryMarkerKind.WorkBreak:
                    {
                        return SystemTimeMarkerIds.WorkBreakCategoryId;
                    }
                case TimeEntryMarkerKind.StopMark:
                case TimeEntryMarkerKind.DownTime:
                case TimeEntryMarkerKind.Errand:
                    {
                        return SystemTimeMarkerIds.StopMarkCategoryId;
                    }

                default:
                    {
                        return SelectedCategory is null ? _categoryId : SelectedCategory.IdCategory;
                    }
            }
        }

        private static string EventInfoFor(TimeEntryMarkerKind marker)
        {
            switch (marker)
            {
                case TimeEntryMarkerKind.DownTime:
                    {
                        return TimeBookingOptions.DownTimeEventInfo;
                    }
                case TimeEntryMarkerKind.Errand:
                    {
                        return TimeBookingOptions.ErrandEventInfo;
                    }

                default:
                    {
                        return null;
                    }
            }
        }
    }
}