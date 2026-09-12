using System.Collections.Specialized;
using System.Configuration;
using System.Reflection;

namespace TaskOTime.Validation;

internal sealed class IsolatedSettings : SettingsProvider
{
    private readonly Dictionary<string, object?> values = new(StringComparer.Ordinal);
    private readonly ApplicationSettingsBase settings;
    public override string ApplicationName { get; set; } = "TaskOTime.Validation";
    internal int Writes { get; private set; }

    private IsolatedSettings(ApplicationSettingsBase settings)
    {
        this.settings = settings;
        Initialize("TaskOTime.Validation.Memory", new NameValueCollection());
        settings.Providers.Clear();
        settings.Providers.Add(this);
        foreach (SettingsProperty property in settings.Properties) property.Provider = this;
        settings.PropertyValues.Clear();
        settings.Reload();
        AssertInstalled();
    }

    internal static IsolatedSettings Install(Assembly app)
    {
        var type = app.GetType("TaskOTime.App.Properties.Settings", true)!;
        var instance = type.GetProperty("Default", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null) as ApplicationSettingsBase
            ?? throw new InvalidOperationException("Cannot isolate the application's settings provider.");
        return Install(instance);
    }

    internal static IsolatedSettings Install(ApplicationSettingsBase settings) => new(settings);

    internal void AssertInstalled()
    {
        if (settings.Providers.Count != 1 || !ReferenceEquals(settings.Providers[Name], this) ||
            settings.Properties.Cast<SettingsProperty>().Any(p => !ReferenceEquals(p.Provider, this)))
            throw new InvalidOperationException("Application settings escaped the in-memory validation profile.");
    }

    internal void AssertSavedString(string name, string expected)
    {
        AssertInstalled();
        if (!values.TryGetValue(Key(settings.Context, name), out var value) || value as string != expected)
            throw new InvalidOperationException("Application did not persist the expected isolated setting: " + name);
    }

    public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection properties)
    {
        var result = new SettingsPropertyValueCollection();
        foreach (SettingsProperty property in properties)
        {
            var found = values.TryGetValue(Key(context, property.Name), out var stored);
            result.Add(new SettingsPropertyValue(property) { SerializedValue = found ? stored : property.DefaultValue, IsDirty = false });
        }
        return result;
    }

    public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection properties)
    {
        AssertInstalled();
        foreach (SettingsPropertyValue property in properties)
            values[Key(context, property.Name)] = property.SerializedValue;
        Writes++;
    }

    private static string Key(SettingsContext context, string name) => context["GroupName"] + ":" + name;
}
