using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace TaskOTime.Validation;

internal sealed class IdealStartupProbe
{
    private sealed record Translation(Window Window, DependencyObject Target, DependencyProperty Property, string Key);
    private readonly Application app;
    private readonly object localization;
    private readonly object theme;
    private readonly MethodInfo setCulture, setTheme;
    private readonly PropertyInfo indexer;
    private readonly EventInfo themeChanged;
    private readonly EventHandler themeHandler;
    private readonly string[] cultures = { "en", "de", "nl", "es" };
    private readonly string[] themes = { "Light", "Dark", "HighContrast", "System" };
    private Translation[] translations = Array.Empty<Translation>();
    private Window[] windows = Array.Empty<Window>();
    private Window? options;
    private string[]? previousTranslations;
    private string? previousEffectiveTheme;
    private int position, notifications, previousNotifications;
    private bool awaiting;

    internal IdealStartupProbe(Application app, Assembly assembly, Assembly viewModels)
    {
        this.app = app;
        var localizationType = viewModels.GetType("TaskOTime.ViewModel.Localization.LocalizationService", true)!;
        localization = localizationType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
            ?? throw new InvalidOperationException("The real localization singleton is missing.");
        setCulture = localizationType.GetMethod("SetCulture", new[] { typeof(string) })
            ?? throw new InvalidOperationException("Localization SetCulture(string) is missing.");
        indexer = localizationType.GetProperty("Item", new[] { typeof(string) })
            ?? throw new InvalidOperationException("Localization string indexer is missing.");
        var themeType = assembly.GetType("TaskOTime.App.Themes.ThemeService", true)!;
        theme = StartupTheme(app, themeType);
        var enumType = themeType.GetProperty("SelectedTheme")?.PropertyType
            ?? throw new InvalidOperationException("Theme selection is missing.");
        WpfProbe.Assert(enumType.IsEnum, "Theme selection is not the expected enum.");
        setTheme = themeType.GetMethod("SetTheme", new[] { enumType })
            ?? throw new InvalidOperationException("Theme SetTheme(enum) is missing.");
        themeChanged = themeType.GetEvent("ThemeChanged")
            ?? throw new InvalidOperationException("ThemeChanged is missing.");
        WpfProbe.Assert(themeChanged.EventHandlerType == typeof(EventHandler), "Unexpected ThemeChanged event signature.");
        themeHandler = (_, _) => notifications++;
        themeChanged.AddEventHandler(theme, themeHandler);
    }

    private static object StartupTheme(Application app, Type themeType)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var values = app.GetType().GetFields(flags).Where(field => field.FieldType == themeType).Select(field => field.GetValue(app))
            .Concat(app.GetType().GetProperties(flags).Where(property => property.PropertyType == themeType &&
                property.GetIndexParameters().Length == 0).Select(property => property.GetValue(app)))
            .Where(value => value is not null).Distinct(ReferenceEqualityComparer.Instance).ToArray();
        return values.Length == 1 ? values[0]! :
            throw new InvalidOperationException("Exactly one real startup-owned ThemeService is required; the host never creates one.");
    }

    internal void BeginCultures(Window[] openWindows, Window? optionsWindow)
    {
        windows = openWindows;
        options = optionsWindow;
        translations = windows.SelectMany(CaptureTranslations).ToArray();
        previousTranslations = null;
        position = 0;
        awaiting = false;
    }

    private IEnumerable<Translation> CaptureTranslations(Window window)
    {
        window.UpdateLayout();
        var result = new List<Translation>();
        foreach (var target in WpfProbe.Tree(window))
        {
            var properties = new List<DependencyProperty>();
            if (target is Window) properties.Add(Window.TitleProperty);
            if (target is TextBlock) properties.Add(TextBlock.TextProperty);
            if (target is ContentControl) properties.Add(ContentControl.ContentProperty);
            if (target is HeaderedItemsControl) properties.Add(HeaderedItemsControl.HeaderProperty);
            foreach (var property in properties)
            {
                var binding = BindingOperations.GetBinding(target, property);
                if (!ReferenceEquals(binding?.Source, localization)) continue;
                var match = Regex.Match(binding.Path?.Path ?? "", @"^(?:Item)?\[(?<key>[^\]]+)\]$");
                WpfProbe.Assert(match.Success, "Unexpected live localization binding path.");
                result.Add(new Translation(window, target, property, match.Groups["key"].Value));
            }
        }
        WpfProbe.Assert(result.Count > 0, "No live localization bindings found on " + window.GetType().FullName + ".");
        return result;
    }

    internal bool AdvanceCulture()
    {
        if (position == cultures.Length) return true;
        var culture = cultures[position];
        if (!awaiting)
        {
            if (options is null) setCulture.Invoke(localization, new object[] { culture });
            else SelectLanguage(options, culture);
            awaiting = true;
            return false;
        }
        WpfProbe.Assert(WpfProbe.Required<CultureInfo>(localization, "Culture").TwoLetterISOLanguageName == culture,
            "Language selection did not update the live localization service.");
        var actual = new string[translations.Length];
        for (var index = 0; index < translations.Length; index++)
        {
            var probe = translations[index];
            var expected = indexer.GetValue(localization, new object[] { probe.Key }) as string;
            actual[index] = probe.Target.GetValue(probe.Property) as string ?? "";
            WpfProbe.Assert(!string.IsNullOrWhiteSpace(expected) && expected != probe.Key && actual[index] == expected,
                "Visible localization did not update: " + probe.Window.GetType().Name + "." + probe.Key + " (" + culture + ").");
        }
        if (previousTranslations is not null)
            foreach (var window in windows)
                WpfProbe.Assert(translations.Select((probe, index) => ReferenceEquals(probe.Window, window) &&
                    actual[index] != previousTranslations[index]).Any(changed => changed),
                    "No visible translation changed on " + window.GetType().Name + " for " + culture + ".");
        previousTranslations = actual;
        position++;
        awaiting = false;
        return false;
    }

    private static void SelectLanguage(Window options, string culture)
    {
        var selectors = WpfProbe.Tree(options).OfType<ComboBox>().Where(combo =>
            new[] { Selector.SelectedItemProperty, Selector.SelectedValueProperty }.Any(property =>
                new[] { "Language", "SelectedLanguage", "CultureName" }.Contains(
                    BindingOperations.GetBinding(combo, property)?.Path?.Path))).ToArray();
        WpfProbe.Assert(selectors.Length == 1, "Expected one Options language selector bound to Language, SelectedLanguage or CultureName.");
        var selector = selectors[0];
        var candidates = selector.Items.Cast<object>().Where(item => LanguageCode(item) == culture).ToArray();
        WpfProbe.Assert(candidates.Length == 1, "Options must expose exactly one language item for " + culture + ".");
        selector.SelectedItem = candidates[0];
    }

    private static string? LanguageCode(object item)
    {
        if (item is string text) return text;
        if (item is CultureInfo culture) return culture.TwoLetterISOLanguageName;
        foreach (var name in new[] { "Code", "CultureName", "Language" })
            if (item.GetType().GetProperty(name)?.GetValue(item) is string value) return value;
        return null;
    }

    internal void BeginThemes(Window[] openWindows)
    {
        windows = openWindows;
        position = 0;
        awaiting = false;
        previousEffectiveTheme = WpfProbe.Read(theme, "EffectiveTheme")?.ToString();
    }

    internal bool AdvanceTheme()
    {
        if (position == themes.Length) return true;
        var selected = themes[position];
        if (!awaiting)
        {
            previousNotifications = notifications;
            setTheme.Invoke(theme, new[] { Enum.Parse(setTheme.GetParameters()[0].ParameterType, selected) });
            awaiting = true;
            return false;
        }
        WpfProbe.Assert(WpfProbe.Read(theme, "SelectedTheme")?.ToString() == selected, "Theme selection did not change.");
        var effective = WpfProbe.Read(theme, "EffectiveTheme")?.ToString();
        if (SystemParameters.HighContrast || selected == "HighContrast")
            WpfProbe.Assert(effective == "HighContrast", "High-contrast precedence was not respected.");
        else if (selected != "System")
            WpfProbe.Assert(effective == selected, "Effective theme did not change.");
        WpfProbe.Assert(effective == previousEffectiveTheme || notifications > previousNotifications, "Effective theme changed without ThemeChanged.");
        foreach (var key in new[] { "WindowBackgroundBrush", "SelectedForegroundBrush", "DisabledBackgroundBrush", "DisabledForegroundBrush", "FocusBrush" })
            WpfProbe.Assert(app.TryFindResource(key) is SolidColorBrush, "Required live theme brush is missing: " + key);
        var background = (SolidColorBrush)app.TryFindResource("WindowBackgroundBrush");
        foreach (var window in windows)
            WpfProbe.Assert(window.Background is SolidColorBrush brush && brush.Color == background.Color,
                "Open window background did not follow the actual startup theme: " + window.GetType().Name);
        var calendars = WpfProbe.Tree(app.MainWindow).OfType<System.Windows.Controls.Calendar>().ToArray();
        WpfProbe.Assert(calendars.Length > 0 && calendars.All(calendar => ReferenceEquals(calendar.Style, app.TryFindResource("ThemedCalendarStyle"))),
            "The real main calendar did not use the shared themed style.");
        previousEffectiveTheme = effective;
        position++;
        awaiting = false;
        return false;
    }

    internal void VerifyDisposedByApplication()
    {
        themeChanged.RemoveEventHandler(theme, themeHandler);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var disposed = theme.GetType().GetProperty("IsDisposed", flags)?.GetValue(theme)
            ?? theme.GetType().GetField("_disposed", flags)?.GetValue(theme);
        WpfProbe.Assert(disposed is true, "Product OnExit did not expose a disposed startup ThemeService (IsDisposed/_disposed).");
    }
}
