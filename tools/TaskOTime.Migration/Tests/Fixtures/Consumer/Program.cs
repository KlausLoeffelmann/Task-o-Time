using System;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using Canary;
using Canary.Views;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var collection = new IdentityCollection();
        var events = 0;
        collection.CollectionChanged += (_, __) => events++;
        var list = (IList)collection;
        list.Add("z");
        list.Add("a");
        list.Add("a");
        Require(collection.Count == 2 && collection[0] == "a" && collection["z"] == "z" && events == 2, "identity, sort, overloaded indexers, events");
        list[1] = "b";
        Require((string)list[1] == "b" && events == 3, "explicit untyped indexer");
        var number = 4;
        Require(Semantics.Adjust(ref number) == 9 && number == 7, "ByRef, optional, rounding, integer division");
        var application = new Application();
        var view = new Probe();
        Require(view.ActionButton != null, "implicit InitializeComponent and public generated member");
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        view.ActionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(view.Loads == 1 && view.Clicks == 1 && (string)view.ActionButton.Content == "loaded", "Handles Me and named field exactly once");
        view.InitializeComponent();
        view.ActionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(view.Clicks == 2, "InitializeComponent idempotence");
        Console.WriteLine("CANARY_OK");
        application.Shutdown();
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
    }
}
