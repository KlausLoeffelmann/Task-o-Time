using System;
using System.Collections.Generic;
using System.Globalization;
using TaskOTime.ViewModel.Localization;
using TaskOTime.ViewModel.Base;

namespace TaskOTime.ViewModel.ViewModels
{
    /// <summary>Bindable options; dialog drafts are applied only after confirmation.</summary>
    public class AppOptionsViewModel : LocalizedViewModelBase
    {

        private bool _restoreMainWindowPlacement = true;
        private bool _saturdayIsWorkday;
        private bool _sundayIsWorkday;
        // Zwei Wochen als Startwert erschien mir erstmal uebersichtlicher.
        private int _bookedDateRangeCount = 14;
        private string _bookedDateRangeUnit = "Tage";
        private string _cultureName = LocalizationService.Current.Culture.Name;

        /// <summary>The dialog edits a draft. Applying options commits this culture.</summary>
        public string CultureName
        {
            get => _cultureName;
            set => SetProperty(ref _cultureName, LocalizationService.ResolveCulture(value).Name, nameof(CultureName));
        }

        public CultureInfo[] AvailableCultures => new[]
        {
            CultureInfo.GetCultureInfo("en"), CultureInfo.GetCultureInfo("de"),
            CultureInfo.GetCultureInfo("nl"), CultureInfo.GetCultureInfo("es")
        };

        // Keep the stored range identifiers compatible with existing settings.
        public KeyValuePair<string, string>[] RangeUnitChoices => new[]
        {
            new KeyValuePair<string, string>("Tage", Text("Options_Days")),
            new KeyValuePair<string, string>("Wochen", Text("Options_Weeks"))
        };

        public bool RestoreMainWindowPlacement
        {
            get
            {
                return _restoreMainWindowPlacement;
            }
            set
            {
                // Mit der Meldung merkt sich das Fenster seine Position dann vermutlich auch gleich dauerhaft.
                SetProperty(ref _restoreMainWindowPlacement, value, nameof(RestoreMainWindowPlacement));
            }
        }

        // Beide Wochenend-Haekchen getrennt, sonst kann man den Samstag garnicht einzeln waehlen.
        public bool SaturdayIsWorkday
        {
            get
            {
                return _saturdayIsWorkday;
            }
            set
            {
                SetProperty(ref _saturdayIsWorkday, value, nameof(SaturdayIsWorkday));
            }
        }

        public bool SundayIsWorkday
        {
            get
            {
                return _sundayIsWorkday;
            }
            set
            {
                SetProperty(ref _sundayIsWorkday, value, nameof(SundayIsWorkday));
            }
        }

        public int BookedDateRangeCount
        {
            get
            {
                return _bookedDateRangeCount;
            }
            set
            {
                // Eingabe erstmal zwischen 3 und 56 halten, die Zahl kommt ja direkt aus dem Dialog..
                int normalized = Math.Max(3, Math.Min(56, value));
                if (SetProperty(ref _bookedDateRangeCount, normalized, nameof(BookedDateRangeCount)))
                {
                    OnPropertyChanged(nameof(BookedDateRangeDescription));
                }
            }
        }

        public string BookedDateRangeUnit
        {
            get
            {
                return _bookedDateRangeUnit;
            }
            set
            {
                // Nur Wochen extra erkennen; alles andere bleibt die Tagesauswahl
                string normalized = string.Equals(value, "Wochen", StringComparison.OrdinalIgnoreCase) ? "Wochen" : "Tage";
                if (SetProperty(ref _bookedDateRangeUnit, normalized, nameof(BookedDateRangeUnit)))
                {
                    NormalizeRangeForUnit();
                    OnPropertyChanged(nameof(BookedDateRangeDescription));
                }
            }
        }

        public string BookedDateRangeDescription
        {
            get
            {
                return Text("Options_Range", BookedDateRangeCount,
                    Text(BookedDateRangeUnit == "Wochen" ? "Options_Weeks" : "Options_Days"));
            }
        }

        public string[] AvailableRangeUnits
        {
            get
            {
                return new[] { "Tage", "Wochen" };
            }
        }

        /// <summary>Creates a draft so cancelling does not modify the live options.</summary>
        public AppOptionsViewModel Clone()
        {
            return new AppOptionsViewModel()
            {
                CultureName = CultureName,
                RestoreMainWindowPlacement = RestoreMainWindowPlacement,
                SaturdayIsWorkday = SaturdayIsWorkday,
                SundayIsWorkday = SundayIsWorkday,
                BookedDateRangeUnit = BookedDateRangeUnit,
                BookedDateRangeCount = BookedDateRangeCount
            };
        }

        // TODO: Beser bei Dutch nochmal nachfragen, weil, hier koennte das vielleicht sogar umgekehrt besser sein.
        // Aber aufpassen, dass man einen Moment erwischt wo er wirklich zeit hat, und mach Notitsen!!
        // Vielleicht lieber erst alle Fragen sammeln; wegen so einer kleinen Sache moechte ich nicht stoeren.
        private void NormalizeRangeForUnit()
        {
            if (BookedDateRangeUnit == "Wochen")
            {
                _bookedDateRangeCount = Math.Max(1, Math.Min(8, _bookedDateRangeCount));
            }
            else
            {
                _bookedDateRangeCount = Math.Max(3, Math.Min(56, _bookedDateRangeCount));
            }
            OnPropertyChanged(nameof(BookedDateRangeCount));
        }
    }
}