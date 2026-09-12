using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Microsoft.Data.SqlClient;

namespace TaskOTime.Validation;

internal sealed class StartupDriver
{
    internal sealed record Contract(Type App, Type Login, Type Main, Type Options, Type Booking, Type Maintenance);
    internal sealed record Booking(Guid User, Guid Project, Guid Category, string Title);
    private enum Phase { Login, LoginCultures, PasswordChange, Main, Options, Maintenance, Booking, IdealCultures, IdealThemes, BookingSaved, MaintenanceClosed, OptionsSelected, OptionsClosed, Done }
    private readonly Application app;
    private readonly Contract contract;
    private readonly string user;
    private readonly string temporaryPassword;
    private readonly bool ideal;
    private readonly IsolatedSettings settings;
    private readonly Action<Booking> verifyBooking;
    private readonly DispatcherTimer timer;
    private readonly Stopwatch elapsed = new();
    private readonly int timeoutSeconds;
    private readonly Assembly viewModels;
    private Phase phase;
    private Exception? failure;
    private Window? loginWindow, mainWindow, optionsWindow, maintenanceWindow, bookingWindow;
    private object? loginVm, mainVm, timeVm, collection;
    private Booking? booking;
    private IdealStartupProbe? idealProbe;
    private bool saturdayOption;

    private StartupDriver(Application app, Contract contract, Assembly viewModels, string user, string password,
        bool ideal, IsolatedSettings settings, int timeoutSeconds, Action<Booking> verifyBooking)
    {
        this.app = app;
        this.contract = contract;
        this.viewModels = viewModels;
        this.user = user;
        temporaryPassword = password;
        this.ideal = ideal;
        this.settings = settings;
        this.timeoutSeconds = timeoutSeconds;
        this.verifyBooking = verifyBooking;
        timer = new DispatcherTimer(DispatcherPriority.Background, app.Dispatcher) { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += Tick;
    }

    internal static Contract ProductContract(Assembly app, Assembly viewModels) => new(
        app.GetType("TaskOTime.App.App", true)!, app.GetType("TaskOTime.App.LoginWindow", true)!,
        app.GetType("TaskOTime.App.MainWindow", true)!, app.GetType("TaskOTime.App.OptionsDialog", true)!,
        app.GetType("TaskOTime.App.TimeEntryEditDialog", true)!,
        viewModels.GetType("TaskOTime.ViewModel.Views.MainDataWindow")
            ?? viewModels.GetType("TaskOTime.ViewModel.Views.MasterDataWindow", true)!);

    internal static void Run(Contract contract, Assembly viewModels, string user, string password, bool ideal,
        IsolatedSettings settings, int timeoutSeconds, Action<Booking> verifyBooking)
    {
        if (!typeof(Application).IsAssignableFrom(contract.App) ||
            contract.App.GetMethod("OnStartup", BindingFlags.Instance | BindingFlags.NonPublic)?.DeclaringType == typeof(Application))
            throw new InvalidOperationException("Genuine startup requires the application's OnStartup override.");
        var app = (Application)Activator.CreateInstance(contract.App)!;
        var driver = new StartupDriver(app, contract, viewModels, user, password, ideal, settings, timeoutSeconds, verifyBooking);
        void TraceProductException(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (args.Exception.StackTrace?.Contains("TaskOTime.App.", StringComparison.Ordinal) == true)
                Console.Error.WriteLine("Product first-chance diagnostic (not a verdict): " +
                    args.Exception.GetType().FullName + ": " + args.Exception.Message +
                    " | Cause: " + args.Exception.GetBaseException().GetType().FullName + ": " +
                    args.Exception.GetBaseException().Message);
        }
        AppDomain.CurrentDomain.FirstChanceException += TraceProductException;
        try
        {
            var initialize = contract.App.GetMethod("InitializeComponent", Type.EmptyTypes)
                ?? throw new InvalidOperationException("Application InitializeComponent is missing.");
            initialize.Invoke(app, null);
            settings.AssertInstalled();
            driver.elapsed.Start();
            driver.timer.Start();
            var exit = app.Run();
            driver.timer.Stop();
            if (driver.failure is not null) throw new InvalidOperationException("Startup validation failed in " + driver.phase + ".", driver.failure);
            WpfProbe.Assert(driver.phase == Phase.Done && exit == 0, "Application exited before startup validation completed successfully.");
            WpfProbe.Assert(app.Windows.Count == 0 && app.Dispatcher.HasShutdownFinished, "Application windows/dispatcher did not finish shutdown.");
            WpfProbe.Assert(driver.loginVm is not null && WpfProbe.Read(driver.loginVm, "Session") is null, "Application shutdown did not log out the real session.");
            settings.AssertInstalled();
            driver.idealProbe?.VerifyDisposedByApplication();
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= TraceProductException;
            driver.timer.Stop();
            if (!app.Dispatcher.HasShutdownStarted)
            {
                app.Shutdown(1);
                if (!app.Dispatcher.HasShutdownStarted) app.Dispatcher.InvokeShutdown();
            }
        }
    }

    private Window? Find(Type type) => app.Windows.OfType<Window>().SingleOrDefault(window => window.GetType() == type && window.IsLoaded && window.IsVisible);

    private void Tick(object? sender, EventArgs args)
    {
        // Rearm before Execute/ShowDialog enters a nested dispatcher frame.
        timer.Stop();
        timer.Start();
        try
        {
            if (elapsed.Elapsed.TotalSeconds > timeoutSeconds)
                throw new TimeoutException("Startup timed out waiting in phase " + phase + ".");
            Step();
        }
        catch (Exception error)
        {
            failure = error;
            timer.Stop();
            Console.Error.WriteLine("Startup phase failed: " + phase + ": " + error.GetBaseException().Message);
            app.Shutdown(1);
        }
    }

    private void Step()
    {
        switch (phase)
        {
            case Phase.Login:
                loginWindow = Find(contract.Login);
                if (loginWindow is null) return;
                loginVm = loginWindow.DataContext ?? throw new InvalidOperationException("Login DataContext is missing.");
                if (ideal)
                {
                    idealProbe = new IdealStartupProbe(app, contract.App.Assembly, viewModels);
                    idealProbe.BeginCultures(new[] { loginWindow });
                    phase = Phase.LoginCultures;
                }
                else SubmitLogin();
                break;
            case Phase.LoginCultures:
                if (idealProbe!.AdvanceCulture()) SubmitLogin();
                break;
            case Phase.PasswordChange:
                WpfProbe.Assert(loginWindow!.IsVisible && WpfProbe.Required<bool>(loginVm!, "MustChangePassword") &&
                    WpfProbe.Read(loginVm!, "Session") is null, "Login did not enforce the seeded temporary-password flow.");
                WpfProbe.Named<PasswordBox>(loginWindow, "Password").Password = temporaryPassword;
                WpfProbe.Named<PasswordBox>(loginWindow, "NewPassword").Password = "Startup-" + Guid.NewGuid().ToString("N") + "!a9";
                phase = Phase.Main;
                WpfProbe.DefaultButton(loginWindow);
                break;
            case Phase.Main:
                mainWindow = Find(contract.Main);
                if (mainWindow is null) return;
                WpfProbe.Assert(ReferenceEquals(app.MainWindow, mainWindow), "MainWindow was not assigned by real startup.");
                var session = WpfProbe.Read(loginVm!, "Session") ?? throw new InvalidOperationException("Startup did not establish a session.");
                WpfProbe.Assert(!WpfProbe.Required<bool>(session, "MustChangePassword"), "The initial password change was not completed.");
                mainVm = mainWindow.DataContext ?? throw new InvalidOperationException("Main DataContext is missing.");
                timeVm = WpfProbe.Read(mainVm, "TimeCollection")!;
                collection = WpfProbe.Read(timeVm, "TimeItems")!;
                CheckCollection();
                WpfProbe.Assert(WpfProbe.Read(timeVm, "SelectedCategory") is not null, "Owned fixture categories were not loaded.");
                phase = Phase.Options;
                WpfProbe.Command(mainVm, "OptionsCommand");
                break;
            case Phase.Options:
                optionsWindow = Find(contract.Options);
                if (optionsWindow is null) return;
                WpfProbe.Assert(ReferenceEquals(optionsWindow.Owner, mainWindow) &&
                    optionsWindow.DataContext?.GetType() == WpfProbe.Read(mainVm!, "Options")?.GetType(), "Options was not composed by the real main window.");
                WpfProbe.Bound<CheckBox>(optionsWindow, ToggleButton.IsCheckedProperty, "RestoreMainWindowPlacement").IsChecked = false;
                var saturday = WpfProbe.Bound<CheckBox>(optionsWindow, ToggleButton.IsCheckedProperty, "SaturdayIsWorkday");
                saturdayOption = !(saturday.IsChecked ?? false);
                saturday.IsChecked = saturdayOption;
                if (ideal)
                {
                    phase = Phase.Maintenance;
                    WpfProbe.Command(mainVm!, "ManageProjectsCommand");
                }
                else
                {
                    phase = Phase.OptionsClosed;
                    WpfProbe.DefaultButton(optionsWindow);
                }
                break;
            case Phase.Maintenance:
                maintenanceWindow = Find(contract.Maintenance);
                if (maintenanceWindow is null) return;
                WpfProbe.Assert(maintenanceWindow.DataContext is not null, "Main Data has no viewmodel.");
                phase = Phase.Booking;
                WpfProbe.Command(timeVm!, "AddCommand");
                break;
            case Phase.Booking:
                bookingWindow = Find(contract.Booking);
                if (bookingWindow is null) return;
                FillBooking();
                if (ideal)
                {
                    idealProbe!.BeginCultures(new[] { mainWindow!, optionsWindow!, maintenanceWindow!, bookingWindow });
                    phase = Phase.IdealCultures;
                }
                else
                {
                    phase = Phase.BookingSaved;
                    WpfProbe.DefaultButton(bookingWindow);
                }
                break;
            case Phase.IdealCultures:
                if (idealProbe!.AdvanceCulture())
                {
                    idealProbe.BeginThemes(new[] { mainWindow!, optionsWindow!, maintenanceWindow!, bookingWindow! });
                    phase = Phase.IdealThemes;
                }
                break;
            case Phase.IdealThemes:
                if (idealProbe!.AdvanceTheme())
                {
                    phase = Phase.BookingSaved;
                    WpfProbe.DefaultButton(bookingWindow!);
                }
                break;
            case Phase.BookingSaved:
                if (Find(contract.Booking) is not null) return;
                CheckCollection();
                WpfProbe.Assert(((IEnumerable)collection!).Cast<object>().Count() == 1, "The booking command did not persist exactly one fixture entry.");
                verifyBooking(booking!);
                if (ideal)
                {
                    phase = Phase.MaintenanceClosed;
                    maintenanceWindow!.Close();
                }
                else Finish();
                break;
            case Phase.MaintenanceClosed:
                if (Find(contract.Maintenance) is not null) return;
                idealProbe!.BeginOptionsCommit(optionsWindow!);
                phase = Phase.OptionsSelected;
                break;
            case Phase.OptionsSelected:
                idealProbe!.VerifyOptionsPending(optionsWindow!);
                phase = Phase.OptionsClosed;
                WpfProbe.DefaultButton(optionsWindow!);
                break;
            case Phase.OptionsClosed:
                if (Find(contract.Options) is not null) return;
                WpfProbe.Assert(settings.Writes > 0, "Options did not save through the isolated settings provider.");
                WpfProbe.Assert(WpfProbe.Required<bool>(WpfProbe.Read(mainVm!, "Options")!, "SaturdayIsWorkday") == saturdayOption,
                    "Accepted options were not applied to the real main viewmodel.");
                settings.AssertInstalled();
                if (ideal)
                {
                    idealProbe!.VerifyOptionsCommitted(settings);
                    Finish();
                }
                else
                {
                    phase = Phase.Booking;
                    WpfProbe.Command(timeVm!, "AddCommand");
                }
                break;
        }
    }

    private void SubmitLogin()
    {
        WpfProbe.Named<TextBox>(loginWindow!, "UserName").Text = user;
        WpfProbe.Named<PasswordBox>(loginWindow!, "Password").Password = temporaryPassword;
        phase = Phase.PasswordChange;
        WpfProbe.DefaultButton(loginWindow!);
    }

    private void FillBooking()
    {
        var project = WpfProbe.Named<ComboBox>(bookingWindow!, "ProjectSelection");
        var category = WpfProbe.Named<ComboBox>(bookingWindow!, "CategorySelection");
        WpfProbe.Assert(project.Items.Count >= 2 && category.Items.Count > 0, "Booking fixture projects/categories are missing.");
        project.SelectedIndex = ideal ? project.Items.Count - 1 : 0;
        WpfProbe.Named<TextBox>(bookingWindow!, "StartTimeTextBox").Text = "01:00";
        var title = "Startup validation " + Guid.NewGuid().ToString("N");
        WpfProbe.Bound<TextBox>(bookingWindow!, TextBox.TextProperty, "Title").Text = title;
        booking = new Booking(WpfProbe.Required<Guid>(WpfProbe.Read(loginVm!, "Session")!, "IdUser"),
            WpfProbe.Required<Guid>(project.SelectedItem, "IdProject"),
            WpfProbe.Required<Guid>(category.SelectedItem, "IdCategory"), title);
    }

    private void CheckCollection()
    {
        WpfProbe.Assert(ReferenceEquals(collection, WpfProbe.Read(timeVm!, "TimeItems")) &&
            WpfProbe.Tree(mainWindow!).OfType<ListView>().Count(list => ReferenceEquals(list.ItemsSource, collection)) == 1,
            "The original main ListView lost its production collection identity.");
    }

    private void Finish()
    {
        CheckCollection();
        phase = Phase.Done;
        timer.Stop();
        mainWindow!.Close();
    }

    internal static void VerifySqlBooking(string connectionString, Booking expected)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT IdProject, IdCategory FROM dbo.TimeItem WHERE IdUser = @user AND ShortTitle = @title";
        command.Parameters.AddWithValue("@user", expected.User);
        command.Parameters.AddWithValue("@title", expected.Title);
        using var reader = command.ExecuteReader();
        WpfProbe.Assert(reader.Read() && reader.GetGuid(0) == expected.Project && reader.GetGuid(1) == expected.Category,
            "Real SQL booking did not retain the selected fixture project/category.");
        WpfProbe.Assert(!reader.Read(), "Duplicate SQL booking was created.");
    }
}
