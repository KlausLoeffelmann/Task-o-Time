using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace TaskOTime.App.Themes
{
    public enum AppTheme { System, Dark, Light, HighContrast }

    /// <summary>Read-only system preferences; injectable without changing the desktop for tests.</summary>
    public interface IThemeEnvironment
    {
        bool HighContrast { get; }
        bool UseLightTheme { get; }
        event EventHandler Changed;
    }

    /// <summary>Replaces only its own palette; consumers must use DynamicResource brush references.</summary>
    public sealed class ThemeService : IDisposable
    {
        private readonly ResourceDictionary resources;
        private readonly Dispatcher dispatcher;
        private readonly IThemeEnvironment environment;
        private readonly bool ownsEnvironment;
        private ResourceDictionary palette;
        private bool disposed;

        public ThemeService(ResourceDictionary resources, Dispatcher dispatcher, IThemeEnvironment environment = null)
        {
            this.resources = resources ?? throw new ArgumentNullException(nameof(resources));
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            dispatcher.VerifyAccess();
            this.environment = environment ?? new WindowsThemeEnvironment();
            ownsEnvironment = environment == null;
            SelectedTheme = AppTheme.System;
            ApplyTheme();
            this.environment.Changed += OnEnvironmentChanged;
        }

        public static ThemeService Start(Application application)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));
            return new ThemeService(application.Resources, application.Dispatcher);
        }

        public AppTheme SelectedTheme { get; private set; }
        public AppTheme EffectiveTheme { get; private set; }
        public event EventHandler ThemeChanged;

        public void SetTheme(AppTheme theme)
        {
            if (!Enum.IsDefined(typeof(AppTheme), theme)) throw new ArgumentOutOfRangeException(nameof(theme));
            dispatcher.Invoke(() =>
            {
                if (disposed) throw new ObjectDisposedException(nameof(ThemeService));
                SelectedTheme = theme;
                ApplyTheme();
            });
        }

        private void OnEnvironmentChanged(object sender, EventArgs e)
        {
            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                if (!disposed) ApplyTheme();
            }));
        }

        private void ApplyTheme()
        {
            // Accessibility preferences take precedence even over an explicit application palette.
            var effective = environment.HighContrast || SelectedTheme == AppTheme.HighContrast
                ? AppTheme.HighContrast
                : SelectedTheme == AppTheme.System
                    ? (environment.UseLightTheme ? AppTheme.Light : AppTheme.Dark)
                    : SelectedTheme;
            var replacement = new ResourceDictionary
            {
                Source = new Uri("/TaskOTime.App;component/Themes/" + effective + ".xaml", UriKind.Relative)
            };
            if (palette == null) resources.MergedDictionaries.Add(replacement);
            else resources.MergedDictionaries[resources.MergedDictionaries.IndexOf(palette)] = replacement;
            palette = replacement;
            EffectiveTheme = effective;
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            dispatcher.Invoke(() =>
            {
                if (disposed) return;
                disposed = true;
                environment.Changed -= OnEnvironmentChanged;
                if (ownsEnvironment) ((IDisposable)environment).Dispose();
            });
        }

        private sealed class WindowsThemeEnvironment : IThemeEnvironment, IDisposable
        {
            public WindowsThemeEnvironment()
            {
                SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            }

            public bool HighContrast => SystemParameters.HighContrast;
            public bool UseLightTheme
            {
                get
                {
                    try
                    {
                        using (var key = Registry.CurrentUser.OpenSubKey(
                            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                            return (key?.GetValue("AppsUseLightTheme") as int? ?? 0) != 0;
                    }
                    catch (SecurityException ex)
                    {
                        Trace.TraceWarning("Cannot read the Windows app-color preference; retaining the dark fallback: {0}", ex.Message);
                        return false;
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        Trace.TraceWarning("Cannot read the Windows app-color preference; retaining the dark fallback: {0}", ex.Message);
                        return false;
                    }
                }
            }

            public event EventHandler Changed;
            private void OnSystemParameterChanged(object sender, PropertyChangedEventArgs e) =>
                Changed?.Invoke(this, EventArgs.Empty);
            private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) =>
                Changed?.Invoke(this, EventArgs.Empty);

            public void Dispose()
            {
                SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
                SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            }
        }
    }
}
