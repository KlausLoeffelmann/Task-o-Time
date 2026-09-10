using System.Windows;
using TaskOTime.ViewModel.ViewModels;

namespace TaskOTime.App;

public partial class OptionsDialog : Window
{
    public OptionsDialog(AppOptionsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public AppOptionsViewModel ViewModel => (AppOptionsViewModel)DataContext;

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
