using System;
using System.Windows;
using System.Windows.Threading;
using TaskOTime.App.Properties;
using TaskOTime.ViewModel.ViewModels;

namespace TaskOTime.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const double WideLayoutThreshold = 1080d;
    private readonly VmMain _viewModel = new VmMain();
    private readonly DispatcherTimer _recordingTimer = new DispatcherTimer();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.DialogRequested += OnDialogRequested;
        _viewModel.OptionsRequested += OnOptionsRequested;
        _viewModel.TaskListEditRequested += OnTaskListEditRequested;
        _viewModel.TimeEntryEditRequested += OnTimeEntryEditRequested;
        Loaded += (_, _) =>
        {
            RestoreWindowPlacement();
            UpdateLayoutMode(ActualWidth);
        };
        SizeChanged += (_, args) => UpdateLayoutMode(args.NewSize.Width);
        Closing += (_, _) => SaveWindowPlacement();
        _recordingTimer.Interval = TimeSpan.FromSeconds(1);
        _recordingTimer.Tick += (_, _) => _viewModel.UpdateRecordingClock(DateTime.Now);
        _recordingTimer.Start();
        LoadOptions();
    }

    private void UpdateLayoutMode(double width)
    {
        _viewModel.IsWideLayout = width >= WideLayoutThreshold;
    }

    private void OnQuitMenuItemClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnTaskListDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_viewModel.TaskManagement.EditTaskListCommand.CanExecute(null))
        {
            _viewModel.TaskManagement.EditTaskListCommand.Execute(null);
        }
    }

    private void OnTimeEntryDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_viewModel.TimeCollection.EditCommand.CanExecute(null))
        {
            _viewModel.TimeCollection.EditCommand.Execute(null);
        }
    }

    private void OnDialogRequested(object sender, DialogRequestedEventArgs e)
    {
        var dialog = new DialogShell(e.Dialog) { Owner = this };
        dialog.ShowDialog();
    }

    private void OnOptionsRequested(object sender, AppOptionsViewModel options)
    {
        var dialog = new OptionsDialog(options) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _viewModel.ApplyOptions(dialog.ViewModel);
            SaveOptions(dialog.ViewModel);
        }
    }

    private void OnTaskListEditRequested(object sender, TaskListEditRequestEventArgs e)
    {
        var dialog = new TaskListEditDialog(e) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            e.SaveAction(e.Title, e.Subtitle);
        }
    }

    private void OnTimeEntryEditRequested(object sender, TimeEntryEditRequestEventArgs e)
    {
        var dialog = new TimeEntryEditDialog(e) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            e.SaveAction(e.EntryTime, e.Title, e.Description, e.CompleteRunningTask);
        }
    }

    private void LoadOptions()
    {
        var options = _viewModel.Options;
        options.RestoreMainWindowPlacement = Settings.Default.RestoreMainWindowPlacement;
        options.SaturdayIsWorkday = Settings.Default.SaturdayIsWorkday;
        options.SundayIsWorkday = Settings.Default.SundayIsWorkday;
        options.BookedDateRangeUnit = Settings.Default.BookedDateRangeUnit;
        options.BookedDateRangeCount = Settings.Default.BookedDateRangeCount;
        _viewModel.ApplyOptions(options);
    }

    private static void SaveOptions(AppOptionsViewModel options)
    {
        Settings.Default.RestoreMainWindowPlacement = options.RestoreMainWindowPlacement;
        Settings.Default.SaturdayIsWorkday = options.SaturdayIsWorkday;
        Settings.Default.SundayIsWorkday = options.SundayIsWorkday;
        Settings.Default.BookedDateRangeUnit = options.BookedDateRangeUnit;
        Settings.Default.BookedDateRangeCount = options.BookedDateRangeCount;
        Settings.Default.Save();
    }

    private void RestoreWindowPlacement()
    {
        if (!Settings.Default.RestoreMainWindowPlacement)
        {
            return;
        }

        if (!double.IsNaN(Settings.Default.MainWindowLeft) && !double.IsNaN(Settings.Default.MainWindowTop))
        {
            Left = Settings.Default.MainWindowLeft;
            Top = Settings.Default.MainWindowTop;
        }

        if (Settings.Default.MainWindowWidth > MinWidth)
        {
            Width = Settings.Default.MainWindowWidth;
        }

        if (Settings.Default.MainWindowHeight > MinHeight)
        {
            Height = Settings.Default.MainWindowHeight;
        }
    }

    private void SaveWindowPlacement()
    {
        if (!Settings.Default.RestoreMainWindowPlacement)
        {
            return;
        }

        Settings.Default.MainWindowLeft = RestoreBounds.Left;
        Settings.Default.MainWindowTop = RestoreBounds.Top;
        Settings.Default.MainWindowWidth = RestoreBounds.Width;
        Settings.Default.MainWindowHeight = RestoreBounds.Height;
        Settings.Default.Save();
    }
}
