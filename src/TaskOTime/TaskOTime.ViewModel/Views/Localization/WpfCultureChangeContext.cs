using System;
using System.Windows.Threading;
using TaskOTime.ViewModel.Localization;

namespace TaskOTime.ViewModel.Views.Localization
{
    /// <summary>Adapts the owning WPF dispatcher to the framework-neutral culture access policy.</summary>
    public sealed class WpfCultureChangeContext : ICultureChangeContext
    {
        private readonly Dispatcher _dispatcher;
        public WpfCultureChangeContext(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }
        public void VerifyAccess() => _dispatcher.VerifyAccess();
    }
}
