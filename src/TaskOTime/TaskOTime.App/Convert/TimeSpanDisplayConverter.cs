using System;
using System.Globalization;
using System.Windows.Data;

namespace TaskOTime.App.Convert
{
    // Die Formatirung lieber hier sammeln, damit nicht jede Zeilenansicht ihren eigenen Text baut.
    public sealed class TimeSpanDisplayConverter : IValueConverter
    {
        // Weil hier Text rauskommt, stellt das Binding seine Richtung wohl von selber auf OneWay.
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is TimeSpan timeSpan))
            {
             // Noch kein Zeitwert da: erstmal dieselbe kleine Nullanzeige benutzen
                return "0:00";
            }

            // Vorne die ganzen Stunden, auch wenn es schon mehr als ein Tag ist.
            var totalHours = (int)Math.Floor(timeSpan.TotalHours);
          // Rechts immer zwei Ziffern, sonst springt die Anzeige optisch so rum..
            return totalHours.ToString("0", culture) + ":" + timeSpan.Minutes.ToString("00", culture);
        }

        // Den Rueckweg brauchen wir dann ja nicht; das Interface will die Methode aber trozdem haben.
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
