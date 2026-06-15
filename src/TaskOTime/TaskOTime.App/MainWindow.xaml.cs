using System.Windows;
using TaskOTime.ViewModel.ViewModels;

namespace TaskOTime.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const double WideLayoutThreshold = 1080d;
    private readonly VmMain _viewModel = new VmMain();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.DialogRequested += OnDialogRequested;
        Loaded += (_, _) => UpdateLayoutMode(ActualWidth);
        SizeChanged += (_, args) => UpdateLayoutMode(args.NewSize.Width);
    }

    private void UpdateLayoutMode(double width)
    {
        _viewModel.IsWideLayout = width >= WideLayoutThreshold;
    }

    private void OnQuitMenuItemClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnDialogRequested(object sender, DialogRequestedEventArgs e)
    {
        var dialog = new DialogShell(e.Dialog)
        {
            Owner = this
        };

        dialog.ShowDialog();
    }
}
