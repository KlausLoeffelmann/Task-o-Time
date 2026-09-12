using System;
using System.Globalization;
using System.ComponentModel;
using TaskOTime.ViewModel.Localization;
using ActiveDevelop.TimeTrackingServices;

namespace TaskOTime.ViewModel.ViewModels
{
    public enum TimeEntryMarkerKind
    {
        Normal = 0,
        WorkBreak = 1,
        StopMark = 2,
        DownTime = 3,
        Errand = 4
    }

    public class TimeEntryViewModel : TimeItemBase
    {

        private string _title;
        private string _description;

        public TimeEntryViewModel() : this(Guid.Empty, DateTime.MinValue, string.Empty, string.Empty, TimeEntryMarkerKind.Normal)
        {
        }

        /// <summary>
        /// Stores the timestamp, text and marker, and initializes both action flags to false.
        /// </summary>
        /// <remarks>
        /// The marker controls presentation; this constructor does not derive different action flags from it.
        /// </remarks>
        public TimeEntryViewModel(Guid idTimeItem, DateTime entryTime, string title, string description, TimeEntryMarkerKind markerKind)
        {
            IDTimeItem = idTimeItem;
            EventTime = new DateTimeOffset(entryTime);
            IsStartAction = false;
            IsEndAction = false;
            _title = title;
            _description = description;
            MarkerKind = markerKind;
            PropertyChangedEventManager.AddHandler(LocalizationService.Current, OnCultureChanged, string.Empty);
        }

        private void OnCultureChanged(object sender, PropertyChangedEventArgs e) => OnPropertyChanged(string.Empty);

        public TimeEntryMarkerKind MarkerKind { get; private set; }

        /// <summary>
        /// Exposes the stored timestamp's date and clock time without converting to another time zone.
        /// </summary>
        /// <remarks>
        /// A missing timestamp returns the minimum date. Setting this property creates a new timestamp
        /// whose offset is determined by the supplied date value's kind.
        /// </remarks>
        public DateTime EntryTime
        {
            get
            {
                return EventTime.HasValue ? EventTime.Value.DateTime : DateTime.MinValue;
            }
            set
            {
                EventTime = new DateTimeOffset(value);
            }
        }

        public string Title
        {
            get
            {
                return _title;
            }
            set
            {
                SetProperty(ref _title, value, nameof(Title));
            }
        }

        public string Description
        {
            get
            {
                return _description;
            }
            set
            {
                SetProperty(ref _description, value, nameof(Description));
            }
        }

        public TimeSpan? DurationFromPrevious
        {
            get
            {
                return DurationToPrevious;
            }
            set
            {
                DurationToPrevious = value;
            }
        }

        public string EntryTimeText
        {
            get
            {
                return EntryTime.ToString("HH:mm", LocalizationService.Current.Culture);
            }
        }

        public string DurationToNextText
        {
            get
            {
                return FormatNullableDuration(DurationToNext);
            }
        }

        public string DurationFromPreviousText
        {
            get
            {
                return FormatNullableDuration(DurationFromPrevious);
            }
        }

        public string MarkerLabel
        {
            get
            {
                switch (MarkerKind)
                {
                    case TimeEntryMarkerKind.WorkBreak:
                        {
                            return LocalizationService.Current["PauseButtonText"];
                        }
                    case TimeEntryMarkerKind.StopMark:
                        {
                            return LocalizationService.Current["InsertStopMarkButtonText"];
                        }
                    case TimeEntryMarkerKind.DownTime:
                        {
                            return LocalizationService.Current["DownTimeButtonText"];
                        }
                    case TimeEntryMarkerKind.Errand:
                        {
                            return LocalizationService.Current["ErrandButtonText"];
                        }

                    default:
                        {
                            return LocalizationService.Current["Booking_Title"];
                        }
                }
            }
        }

        public string MarkerVisualHint
        {
            get
            {
                switch (MarkerKind)
                {
                    case TimeEntryMarkerKind.WorkBreak:
                        {
                            return "☕";
                        }
                    case TimeEntryMarkerKind.StopMark:
                        {
                            return "■";
                        }
                    case TimeEntryMarkerKind.DownTime:
                        {
                            return "◆";
                        }
                    case TimeEntryMarkerKind.Errand:
                        {
                            return "◇";
                        }

                    default:
                        {
                            return "●";
                        }
                }
            }
        }

        public string MarkerAccent
        {
            get
            {
                switch (MarkerKind)
                {
                    case TimeEntryMarkerKind.WorkBreak:
                        {
                            return "#FFB58B2B";
                        }
                    case TimeEntryMarkerKind.StopMark:
                        {
                            return "#FFB94A48";
                        }
                    case TimeEntryMarkerKind.DownTime:
                        {
                            return "#FF8E8E8E";
                        }
                    case TimeEntryMarkerKind.Errand:
                        {
                            return "#FFB58B2B";
                        }

                    default:
                        {
                            return "#FF7EA6C8";
                        }
                }
            }
        }

        public bool IsSystemMarker
        {
            get
            {
                return MarkerKind != TimeEntryMarkerKind.Normal;
            }
        }

        /// <summary>
        /// Displays a dash for a missing duration, distinguishing it from a measured duration of zero.
        /// </summary>
        private static string FormatNullableDuration(TimeSpan? duration)
        {
            if (!duration.HasValue)
            {
                return "—";
            }

            return FormatDuration(duration.Value);
        }

        /// <summary>
        /// Formats the sign separately, followed by total hours and a two-digit minutes component.
        /// </summary>
        /// <remarks>
        /// Hours continue beyond a single day. Seconds are not displayed separately.
        /// </remarks>
        public static string FormatDuration(TimeSpan duration)
        {
            string sign = duration < TimeSpan.Zero ? "-" : string.Empty;
            var absoluteDuration = duration.Duration();
            int totalHours = (int)Math.Round(Math.Floor(absoluteDuration.TotalHours));

            return string.Format(LocalizationService.Current.Culture, "{0}{1:0}:{2:00} h", sign, totalHours, absoluteDuration.Minutes);
        }

        /// <summary>
        /// Forwards the original notification before notifying the associated display properties.
        /// </summary>
        /// <remarks>
        /// Dependencies are listed explicitly here. A computed property does not notify subscribers
        /// merely because its getter reads other properties.
        /// </remarks>
        protected override void OnPropertyChanged(string propertyName = null)
        {
            base.OnPropertyChanged(propertyName);

            switch (propertyName ?? "")
            {
                case "EventTime":
                    {
                        base.OnPropertyChanged(nameof(EntryTime));
                        base.OnPropertyChanged(nameof(EntryTimeText));
                        break;
                    }

                case "DurationToNext":
                    {
                        base.OnPropertyChanged(nameof(DurationToNextText));
                        break;
                    }

                case "DurationToPrevious":
                    {
                        base.OnPropertyChanged(nameof(DurationFromPrevious));
                        base.OnPropertyChanged(nameof(DurationFromPreviousText));
                        break;
                    }
            }
        }
    }
}