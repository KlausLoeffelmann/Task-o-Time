using System;
using System.Globalization;

namespace TaskOTime.ViewModel.Localization
{
    public static class TimeInput
    {
        /// <summary>Accepts the selected culture's short time and the editor's stable 24-hour form, not dates.</summary>
        public static bool TryParseTime(string text, out TimeSpan time)
        {
            var culture = LocalizationService.Current.Culture;
            var patterns = new[] { culture.DateTimeFormat.ShortTimePattern, "H:mm", "HH:mm" };
            if (DateTime.TryParseExact(text?.Trim(), patterns, culture, DateTimeStyles.NoCurrentDateDefault, out var parsed))
            {
                time = parsed.TimeOfDay;
                return true;
            }
            time = default;
            return false;
        }
    }
}
