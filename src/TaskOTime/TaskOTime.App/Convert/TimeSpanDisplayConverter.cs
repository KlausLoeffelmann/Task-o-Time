using System;
using System.Globalization;
using System.Windows.Data;

namespace TaskOTime.App.Convert
{
    /// <summary>
    /// Centralizes duration formatting for bindings that display total hours and a minutes component.
    /// </summary>
    public sealed class TimeSpanDisplayConverter : IValueConverter
    {
        /// <summary>
        /// Formats a duration using the supplied culture, or returns the zero display for other input types.
        /// </summary>
        /// <remarks>
        /// Returning text does not select a binding mode; callers must configure one-way bindings explicitly.
        /// Negative durations retain their signed components rather than being normalized to a separate sign.
        /// </remarks>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is TimeSpan timeSpan))
            {
                // Missing or incompatible values use the same display as a zero duration.
                return "0:00";
            }

            // Use total hours so the displayed hour count does not wrap at a day.
            var totalHours = (int)Math.Floor(timeSpan.TotalHours);
            // Pad the minutes component to two digits for a stable display width.
            return totalHours.ToString("0", culture) + ":" + timeSpan.Minutes.ToString("00", culture);
        }

        /// <summary>
        /// Rejects reverse conversion because display text is not an editable duration format.
        /// </summary>
        /// <exception cref="NotSupportedException">Always thrown; reverse conversion is not implemented.</exception>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
