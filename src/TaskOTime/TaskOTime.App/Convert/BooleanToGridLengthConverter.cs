using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TaskOTime.App.Convert
{
    public sealed class BooleanToGridLengthConverter : IValueConverter
    {
        public GridLength TrueLength { get; set; } = new GridLength(1, GridUnitType.Star);

        public GridLength FalseLength { get; set; } = new GridLength(0);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var flag = value is bool boolean && boolean;
            if (parameter is string text && string.Equals(text, "Invert", StringComparison.OrdinalIgnoreCase))
            {
                flag = !flag;
            }

            return flag ? TrueLength : FalseLength;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
