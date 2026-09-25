using TaskOTime.ViewModel.Base;

namespace TaskOTime.ViewModel.Localization
{
    /// <summary>Refreshes computed presentation properties without retaining discarded view models.</summary>
    public class LocalizedViewModelBase : ViewModelBase
    {
        protected LocalizedViewModelBase()
        {
            WeakNotifications.SubscribePropertyChanged(LocalizationService.Current, this, static target => target.OnCultureChanged());
        }

        protected virtual void OnCultureChanged() => OnPropertyChanged(string.Empty);
        protected static string Text(string key) => LocalizationService.Current[key];
        protected static string Text(string key, params object[] arguments) => LocalizationService.Current.Format(key, arguments);
    }
}
