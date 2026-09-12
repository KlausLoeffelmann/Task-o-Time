using System;
using TaskOTime.ViewModel.Base;

namespace TaskOTime.ViewModel.ViewModels
{
    // Die Optionen zusammen halten, dann weiss das Binding an einer Stelle wie die Ansicht aussehen soll
    public class AppOptionsViewModel : ViewModelBase
    {

        private bool _restoreMainWindowPlacement = true;
        private bool _saturdayIsWorkday;
        private bool _sundayIsWorkday;
        // Zwei Wochen als Startwert erschien mir erstmal uebersichtlicher.
        private int _bookedDateRangeCount = 14;
        private string _bookedDateRangeUnit = "Tage";

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

        // Der Getter liest die Optionen, also muesste MVVM den Text doch schon dadurch mit beobachten.
        public string BookedDateRangeDescription
        {
            get
            {
                return $"{BookedDateRangeCount} {BookedDateRangeUnit} anzeigen, die Buchungen aufweisen.";
            }
        }

        public string[] AvailableRangeUnits
        {
            get
            {
                return new[] { "Tage", "Wochen" };
            }
        }

        // Eigene Kopie fuer den Dialog machen, damit Abbrechen nicht schon alle Felder ueberschreibt.
        public AppOptionsViewModel Clone()
        {
            return new AppOptionsViewModel()
            {
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