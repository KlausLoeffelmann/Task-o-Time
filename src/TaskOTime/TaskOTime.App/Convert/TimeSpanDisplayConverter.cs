using System;
using System.Globalization;
using System.Windows.Data;

namespace TaskOTime.App.Convert
{
    public sealed class TimeSpanDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is TimeSpan timeSpan))
            {
                return "0:00";
            }

            var totalHours = (int)Math.Floor(timeSpan.TotalHours);
            return totalHours.ToString("0", culture) + ":" + timeSpan.Minutes.ToString("00", culture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
