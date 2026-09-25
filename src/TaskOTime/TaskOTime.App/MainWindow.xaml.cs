using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using System.Linq;
using TaskOTime.AppServer.Models;
using TaskOTime.ViewModel.Views;
using TaskOTime.App.Properties;
using TaskOTime.ViewModel.ViewModels;
using TaskOTime.ViewModel.Localization;

namespace TaskOTime.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const double WideLayoutThreshold = 1080d;
    private readonly VmMain _viewModel;
    private readonly DesktopServices _services;
    private readonly TenantUserDto _session;
    private readonly DispatcherTimer _recordingTimer = new DispatcherTimer();

    public MainWindow(VmMain viewModel, DesktopServices services, TenantUserDto session)
    {
        _viewModel = viewModel;
        _services = services;
        _session = session;
        InitializeComponent();
        UpdateLocalizedTitle(null, null);
        PropertyChangedEventManager.AddHandler(LocalizationService.Current, UpdateLocalizedTitle, string.Empty);
        DataContext = _viewModel;
        _viewModel.DialogRequested += OnDialogRequested;
        _viewModel.OptionsRequested += OnOptionsRequested;
        _viewModel.TaskListEditRequested += OnTaskListEditRequested;
        _viewModel.TimeEntryEditRequested += OnTimeEntryEditRequested;
        _viewModel.MainDataRequested += OnMainDataRequested;
        Closed += (_, _) => _recordingTimer.Stop();
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

    public bool LogoutRequested { get; private set; }

    private void UpdateLocalizedTitle(object sender, PropertyChangedEventArgs e)
    {
        Title = LocalizationService.Current["AppTitle"] + " – " + _session.UserIdent + " – " + _services.ModeDescription;
    }

    private void OnLogoutClick(object sender, RoutedEventArgs e)
    {
        LogoutRequested = true;
        Close();
    }

    private void OnMainDataRequested(object sender, int tab)
    {
        var window = new MainDataWindow { Owner = this };
        window.DataContext = new MainDataViewModel(_services.TenantFor(_session), _session.IdUser,
            _services.Admin, _services.Users, _services.Bookings, tab, new MaintenanceInteraction(window));
        window.ShowDialog();
        if (!_services.TenantFor(_session).IsActive)
        {
            LogoutRequested = true;
            Close();
            return;
        }
        var selectedProjectId = _viewModel.TimeCollection.SelectedProject?.IdProject;
        _viewModel.TimeCollection.Projects.Clear();
        foreach (var project in DesktopServices.Require(_services.Admin.GetProjects(DesktopServices.Query(_session))))
            _viewModel.TimeCollection.Projects.Add(project);
        _viewModel.TimeCollection.SelectedProject = _viewModel.TimeCollection.Projects.FirstOrDefault(x => x.IdProject == selectedProjectId)
            ?? _viewModel.TimeCollection.Projects.FirstOrDefault();
        _viewModel.TimeCollection.RefreshCategories(
            DesktopServices.Require(_services.Admin.GetCategories(DesktopServices.Query(_session))));
        if (!_viewModel.TaskManagement.IsTaskRecording)
        {
            _viewModel.TaskManagement.TaskLists.Clear();
            foreach (var list in _services.LoadTaskLists(_session))
                _viewModel.TaskManagement.TaskLists.Add(list);
            _viewModel.TaskManagement.SelectedTaskList = _viewModel.TaskManagement.TaskLists.FirstOrDefault();
        }
    }

    private void UpdateLayoutMode(double width)
    {
        _viewModel.IsWideLayout = width >= WideLayoutThreshold;
    }

    private void OnQuitMenuItemClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnCommandStripCheckOutClick(object sender, RoutedEventArgs e)
    {
        ExecuteCommand(_viewModel.TimeCollection.CheckOutCommand);
    }

    private void OnCommandStripWorkBreakClick(object sender, RoutedEventArgs e)
    {
        ExecuteCommand(_viewModel.TimeCollection.InsertWorkBreakCommand);
    }

    private void OnCommandStripDownTimeClick(object sender, RoutedEventArgs e)
    {
        ExecuteCommand(_viewModel.TimeCollection.InsertDownTimeCommand);
    }

    private void OnCommandStripErrandClick(object sender, RoutedEventArgs e)
    {
        ExecuteCommand(_viewModel.TimeCollection.InsertErrandCommand);
    }

    private static void ExecuteCommand(System.Windows.Input.ICommand command)
    {
        if (command != null && command.CanExecute(null))
        {
            command.Execute(null);
        }
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
        var dialog = new TimeEntryEditDialog(e, _viewModel.TimeCollection) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            e.SaveAction(e.EntryTime, e.Title, e.Description, e.CompleteRunningTask);
        }
    }

    private void LoadOptions()
    {
        var options = _viewModel.Options;
        options.CultureName = Settings.Default.CultureName;
        options.RestoreMainWindowPlacement = Settings.Default.RestoreMainWindowPlacement;
        options.SaturdayIsWorkday = Settings.Default.SaturdayIsWorkday;
        options.SundayIsWorkday = Settings.Default.SundayIsWorkday;
        options.BookedDateRangeUnit = Settings.Default.BookedDateRangeUnit;
        options.BookedDateRangeCount = Settings.Default.BookedDateRangeCount;
        _viewModel.ApplyOptions(options);
    }

    private static void SaveOptions(AppOptionsViewModel options)
    {
        Settings.Default.CultureName = options.CultureName;
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
