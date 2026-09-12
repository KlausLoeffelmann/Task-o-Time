using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;

namespace TaskOTime.Validation;

internal static class OptionsCultureSelectionSelfTest
{
    public sealed class Clone
    {
        public string CultureName { get; set; } = "en";
        public CultureInfo[] AvailableCultures { get; } =
            new[] { "en", "de", "nl", "es" }.Select(name => new CultureInfo(name)).ToArray();
    }

    internal static void Run()
    {
        var clone = new Clone();
        var selector = new ComboBox { SelectedValuePath = "Name", DisplayMemberPath = "NativeName" };
        selector.SetBinding(ItemsControl.ItemsSourceProperty, new Binding("AvailableCultures"));
        selector.SetBinding(Selector.SelectedValueProperty, new Binding("CultureName") { Mode = BindingMode.TwoWay });
        var window = new Window { DataContext = clone, Content = selector };
        try
        {
            window.Show();
            foreach (var culture in new[] { "en", "de", "nl", "es" })
            {
                IdealStartupProbe.SelectOptionsCulture(window, culture);
                WpfProbe.Assert(clone.CultureName == culture &&
                    selector.SelectedItem is CultureInfo item && item.Name == culture,
                    "Synthetic Options CultureInfo selector failed.");
            }
            var rejected = false;
            try { IdealStartupProbe.SelectOptionsCulture(window, "fr"); }
            catch (InvalidOperationException) { rejected = true; }
            WpfProbe.Assert(rejected && clone.CultureName == "es", "Missing culture was accepted or changed the pending clone.");
            selector.SelectedValuePath = "NativeName";
            rejected = false;
            try { IdealStartupProbe.SelectOptionsCulture(window, "en"); }
            catch (InvalidOperationException) { rejected = true; }
            WpfProbe.Assert(rejected, "An incompatible language selector contract was accepted.");
        }
        finally { window.Close(); }
        Console.WriteLine("Synthetic Options selector passed: en/de/nl/es CultureName binding and two negative contracts; no product startup.");
    }
}
