using System;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
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
        public XmlLanguage Language => XmlLanguage.GetLanguage(Culture.Name);
        public string this[string key] => InSelectedCulture(() => Strings[key].Value);
        public event PropertyChangedEventHandler PropertyChanged;

        private LocalizationService()
        {
            var factory = new ResourceManagerStringLocalizerFactory(
                Options.Create(new LocalizationOptions { ResourcesPath = "Resources" }),
                NullLoggerFactory.Instance);
            Strings = factory.Create("Strings", typeof(LocalizationService).Assembly.GetName().Name);
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
            Application.Current?.Dispatcher.VerifyAccess();
            Culture = ResolveCulture(name);
            CultureInfo.DefaultThreadCurrentCulture = Culture;
            CultureInfo.DefaultThreadCurrentUICulture = Culture;
            Thread.CurrentThread.CurrentCulture = Culture;
            Thread.CurrentThread.CurrentUICulture = Culture;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }
}
