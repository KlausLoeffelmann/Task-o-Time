using System;
using System.Globalization;
using System.Windows.Data;

namespace TaskOTime.App.Convert
{
    public sealed class WidthToLayoutModeConverter : IValueConverter
    {
        public double WideThreshold { get; set; } = 1180d;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var width = value is double number ? number : 0d;
            return width >= WideThreshold;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
