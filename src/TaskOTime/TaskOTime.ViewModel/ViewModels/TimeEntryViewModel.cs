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

        // '' <summary>
        // ''  legt tijdstip, tekst en marker vast en initialiseert beide actievlaggen op onwaar.
        // '' </summary>
        // '' <remarks>
        // ''  de marker stuurt de presentatie; deze constructor leidt er geen andere actievlaggen uit af.
        // '' </remarks>
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

        // '' <summary>
        // ''  biedt datum en kloktijd van het opgeslagen tijdstip aan, zonder omzetting naar een andere zone.
        // '' </summary>
        // '' <remarks>
        // ''  bij een ontbrekend tijdstip wordt de minimale datum getoond.  schrijven maakt een nieuw
        // ''  tijdstip met offset volgens de soort van de aangeleverde datumwaarde.
        // '' </remarks>
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

        // '' <summary>
        // ''  toont een streep voor een ontbrekende duur, zodat die niet als een gemeten nulduur verschijnt.
        // '' </summary>
        private static string FormatNullableDuration(TimeSpan? duration)
        {
            if (!duration.HasValue)
            {
                return "—";
            }

            return FormatDuration(duration.Value);
        }

        // '' <summary>
        // ''  houdt het teken apart en toont totale uren met twee cijfers voor het minutendeel.
        // '' </summary>
        // '' <remarks>
        // ''  uren lopen door voorbij een etmaal.  seconden worden niet afzonderlijk weergegeven.
        // '' </remarks>
        public static string FormatDuration(TimeSpan duration)
        {
            string sign = duration < TimeSpan.Zero ? "-" : string.Empty;
            var absoluteDuration = duration.Duration();
            int totalHours = (int)Math.Round(Math.Floor(absoluteDuration.TotalHours));

            return string.Format(LocalizationService.Current.Culture, "{0}{1:0}:{2:00} h", sign, totalHours, absoluteDuration.Minutes);
        }

        // '' <summary>
        // ''  geeft eerst de oorspronkelijke melding door en meldt daarna de bijbehorende weergavewaarden.
        // '' </summary>
        // '' <remarks>
        // ''  de afhankelijkheden zijn hier expliciet opgesomd.  een berekende eigenschap meldt zichzelf
        // ''  niet alleen doordat haar getter andere eigenschappen leest.
        // '' </remarks>
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