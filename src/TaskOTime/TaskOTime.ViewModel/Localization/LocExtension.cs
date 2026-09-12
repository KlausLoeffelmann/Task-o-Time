using System;
using System.Windows.Data;
using System.Windows.Markup;

namespace TaskOTime.ViewModel.Localization
{
    /// <summary>A normal WPF binding gives resources weak subscriptions and live target updates.</summary>
    [MarkupExtensionReturnType(typeof(string))]
    public sealed class LocExtension : MarkupExtension
    {
        public LocExtension() { }
        public LocExtension(string key) { Key = key; }
        public string Key { get; set; }
        public override object ProvideValue(IServiceProvider serviceProvider) =>
            new Binding("[" + Key + "]") { Source = LocalizationService.Current, Mode = BindingMode.OneWay }
                .ProvideValue(serviceProvider);
    }

    /// <summary>Bind a view's inherited Language property so WPF formats and parses with the selected culture.</summary>
    public sealed class CultureExtension : MarkupExtension
    {
        public override object ProvideValue(IServiceProvider serviceProvider) =>
            new Binding(nameof(LocalizationService.Language)) { Source = LocalizationService.Current, Mode = BindingMode.OneWay }
                .ProvideValue(serviceProvider);
    }
}
