using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TaskOTime.App.Convert
{
    public sealed class BooleanToVisibilityConverter : IValueConverter
    {
        public bool CollapseWhenFalse { get; set; } = true;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isVisible = value is bool boolean && boolean;
            if (parameter is string text && string.Equals(text, "Invert", StringComparison.OrdinalIgnoreCase))
            {
                isVisible = !isVisible;
            }

            if (isVisible)
            {
                return Visibility.Visible;
            }

            return CollapseWhenFalse ? Visibility.Collapsed : Visibility.Hidden;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility visibility && visibility == Visibility.Visible;
        }
    }
}
