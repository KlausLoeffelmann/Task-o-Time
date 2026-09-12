using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace TaskOTime.ViewModel.Localization
{
    /// <summary>Resolves embedded resources and publishes UI-thread culture changes.</summary>
    public sealed class LocalizationService : INotifyPropertyChanged
    {
        public static LocalizationService Current { get; } = new LocalizationService();
        public IStringLocalizer Strings { get; }
        public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");
        public string CultureName => Culture.Name;
        private volatile ContextRegistration _registration;
        public string this[string key] => InSelectedCulture(() => Strings[key].Value);
        public event PropertyChangedEventHandler PropertyChanged;

        private LocalizationService()
        {
            var factory = new ResourceManagerStringLocalizerFactory(
                Options.Create(new LocalizationOptions { ResourcesPath = "Resources" }),
                NullLoggerFactory.Instance);
            Strings = factory.Create("Strings", typeof(LocalizationService).Assembly.GetName().Name);
        }

        /// <summary>Installs a host access policy until the returned registration is disposed, in reverse registration order.</summary>
        public IDisposable UseChangeContext(ICultureChangeContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            _registration?.VerifyAccess();
            context.VerifyAccess();
            var registration = new ContextRegistration(this, context, _registration);
            _registration = registration;
            return registration;
        }

        public string Format(string key, params object[] arguments) =>
            InSelectedCulture(() => Strings[key, arguments].Value);

        // Dispatcher callbacks can carry an older ExecutionContext culture.
        // Resolve against the selected UI culture without changing the caller's context.
        private string InSelectedCulture(Func<string> lookup)
        {
            var previous = CultureInfo.CurrentCulture;
            var previousUi = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = Culture;
                CultureInfo.CurrentUICulture = Culture;
                return lookup();
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
                CultureInfo.CurrentUICulture = previousUi;
            }
        }

        /// <summary>Retains supported regional formatting; unsupported or invalid names use English.</summary>
        public static CultureInfo ResolveCulture(string name)
        {
            try
            {
                var culture = CultureInfo.GetCultureInfo(name ?? "en");
                switch (culture.TwoLetterISOLanguageName)
                {
                    case "en": case "de": case "nl": case "es": return culture;
                }
            }
            catch (CultureNotFoundException) { }
            return CultureInfo.GetCultureInfo("en");
        }

        public void SetCulture(string name)
        {
            _registration?.VerifyAccess();
            Culture = ResolveCulture(name);
            CultureInfo.DefaultThreadCurrentCulture = Culture;
            CultureInfo.DefaultThreadCurrentUICulture = Culture;
            Thread.CurrentThread.CurrentCulture = Culture;
            Thread.CurrentThread.CurrentUICulture = Culture;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }

        private sealed class ContextRegistration : IDisposable
        {
            private LocalizationService _owner;
            private ICultureChangeContext _context;
            private ContextRegistration _previous;

            public ContextRegistration(LocalizationService owner, ICultureChangeContext context, ContextRegistration previous)
            {
                _owner = owner;
                _context = context;
                _previous = previous;
            }

            public void VerifyAccess() => _context?.VerifyAccess();

            public void Dispose()
            {
                if (_owner == null) return;
                _context.VerifyAccess();
                if (!ReferenceEquals(_owner._registration, this))
                    throw new InvalidOperationException("Culture change contexts must be released in reverse registration order.");
                _owner._registration = _previous;
                _owner = null;
                _context = null;
                _previous = null;
            }
        }
    }
}
