using System.Windows;
using TaskOTime.ViewModel.ViewModels;

namespace TaskOTime.App;

public partial class TaskListEditDialog : Window
{
    public TaskListEditDialog(TaskListEditRequestEventArgs request)
    {
        InitializeComponent();
        DataContext = request;
    }

    public TaskListEditRequestEventArgs Request => (TaskListEditRequestEventArgs)DataContext;

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
