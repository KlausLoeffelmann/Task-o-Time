using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace TaskOTime.Validation;

internal static class WpfProbe
{
    internal static object? Read(object target, string property) =>
        (target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)
            ?? throw new InvalidOperationException("Required property missing: " + target.GetType().FullName + "." + property)).GetValue(target);

    internal static T Required<T>(object target, string property) => Read(target, property) is T value ? value :
        throw new InvalidOperationException("Unexpected value/type for " + target.GetType().FullName + "." + property);

    internal static T Named<T>(Window window, string name) where T : DependencyObject => window.FindName(name) as T ??
        throw new InvalidOperationException("Required control missing: " + window.GetType().FullName + "." + name);

    internal static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var item = queue.Dequeue();
            if (!visited.Add(item)) continue;
            if (visited.Count > 50000) throw new InvalidOperationException("Unexpectedly large WPF tree.");
            yield return item;
            foreach (var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>()) queue.Enqueue(child);
            if (item is Visual or Visual3D)
                for (var index = 0; index < VisualTreeHelper.GetChildrenCount(item); index++)
                    queue.Enqueue(VisualTreeHelper.GetChild(item, index));
        }
    }

    internal static T Bound<T>(Window window, DependencyProperty property, string path) where T : DependencyObject
    {
        var matches = Tree(window).OfType<T>().Where(control =>
            BindingOperations.GetBinding(control, property)?.Path?.Path == path).ToArray();
        return matches.Length == 1 ? matches[0] :
            throw new InvalidOperationException("Expected one " + typeof(T).Name + " bound to " + path + ".");
    }

    internal static void DefaultButton(Window window)
    {
        var buttons = Tree(window).OfType<Button>().Where(button => button.IsDefault).ToArray();
        if (buttons.Length != 1 || !buttons[0].IsEnabled)
            throw new InvalidOperationException("Expected one enabled default dialog button.");
        buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    internal static void Command(object viewModel, string name)
    {
        var command = Required<ICommand>(viewModel, name);
        if (!command.CanExecute(null)) throw new InvalidOperationException("Required command is disabled: " + name);
        command.Execute(null);
    }

    internal static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
