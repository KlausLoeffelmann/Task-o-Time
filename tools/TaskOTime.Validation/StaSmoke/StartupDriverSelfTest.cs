using System.Collections.ObjectModel;
using System.Configuration;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace TaskOTime.Validation;

internal static class StartupDriverSelfTest
{
    internal static bool RejectPasswordFlow;
    internal static bool EarlyExit;
    internal static bool WaitForTimeout;
    internal static bool Initialized, Started, Exited;
    internal static readonly LoginModel Login = new();
    internal static readonly MainModel Main = new();
    internal static readonly Profile Settings = new();

    internal static void Run(string scenario)
    {
        Console.WriteLine("SYNTHETIC startup-driver check; no product assemblies or SQL, NOT desktop acceptance.");
        RejectPasswordFlow = scenario == "invalid-login";
        EarlyExit = scenario == "early-exit";
        WaitForTimeout = scenario == "timeout";
        var settings = IsolatedSettings.Install(Settings);
        WpfProbe.Assert(!Settings.SaturdayIsWorkday, "Isolated settings defaults failed.");
        Settings.SaturdayIsWorkday = true;
        Settings.CultureName = "de";
        Settings.Save();
        Settings.CultureName = "nl";
        settings.AssertSavedString("CultureName", "de");
        Settings.SaturdayIsWorkday = false;
        Settings.Reload();
        WpfProbe.Assert(Settings.SaturdayIsWorkday && Settings.CultureName == "de" &&
            settings.Writes == 1, "Memory settings save/reload failed.");
        Settings.SaturdayIsWorkday = false;
        Settings.Save();
        Main.Options.SaturdayIsWorkday = Settings.SaturdayIsWorkday;
        var writesBefore = settings.Writes;
        var contract = new StartupDriver.Contract(typeof(SyntheticApp), typeof(LoginWindow), typeof(MainWindow),
            typeof(OptionsWindow), typeof(BookingWindow), typeof(Window));
        StartupDriver.Run(contract, typeof(StartupDriverSelfTest).Assembly, "owned-user", "owned-temporary-password",
            scenario == "missing-ideal", settings, 5, expected =>
            {
                var actual = Main.TimeCollection.TimeItems.Single();
                WpfProbe.Assert(actual == expected, "Synthetic booking callback received wrong identities/title.");
            });
        WpfProbe.Assert(Initialized && Started && Exited && settings.Writes > writesBefore &&
            Settings.SaturdayIsWorkday, "Synthetic application lifecycle/options save was not observed.");
        Console.WriteLine("SYNTHETIC driver passed: InitializeComponent/OnStartup, nested modal login/password/options/booking, memory settings and OnExit.");
    }

    internal sealed class Profile : ApplicationSettingsBase
    {
        [UserScopedSetting, DefaultSettingValue("en")]
        public string CultureName
        {
            get => (string)this[nameof(CultureName)];
            set => this[nameof(CultureName)] = value;
        }

        [UserScopedSetting, DefaultSettingValue("False")]
        public bool SaturdayIsWorkday
        {
            get => (bool)this[nameof(SaturdayIsWorkday)];
            set => this[nameof(SaturdayIsWorkday)] = value;
        }
    }

    public sealed class SyntheticApp : Application
    {
        public void InitializeComponent()
        {
            Initialized = true;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Started = true;
            if (EarlyExit) { Shutdown(); return; }
            if (WaitForTimeout) { new Window { Title = "Synthetic timeout" }.Show(); return; }
            var login = new LoginWindow();
            if (login.ShowDialog() != true) { Shutdown(1); return; }
            var main = new MainWindow();
            MainWindow = main;
            main.Closed += (_, _) => { Login.Session = null; Shutdown(); };
            main.Show();
        }
        protected override void OnExit(ExitEventArgs e) { Exited = true; base.OnExit(e); }
    }

    public sealed class LoginModel
    {
        public bool MustChangePassword { get; set; }
        public Session? Session { get; set; }
    }
    public sealed class Session
    {
        public Guid IdUser { get; } = Guid.NewGuid();
        public bool MustChangePassword => false;
    }
    public sealed class Project { public Guid IdProject { get; } = Guid.NewGuid(); }
    public sealed class Category { public Guid IdCategory { get; } = Guid.NewGuid(); }
    public sealed class Options
    {
        public bool RestoreMainWindowPlacement { get; set; } = true;
        public bool SaturdayIsWorkday { get; set; }
    }
    public sealed class Request { public string Title { get; set; } = ""; }
    public sealed class TimeModel
    {
        public ObservableCollection<StartupDriver.Booking> TimeItems { get; } = new();
        public Project[] Projects { get; } = { new(), new() };
        public Category[] Categories { get; } = { new() };
        public Category SelectedCategory => Categories[0];
        public ICommand AddCommand { get; set; } = null!;
    }
    public sealed class MainModel
    {
        public Options Options { get; } = new();
        public TimeModel TimeCollection { get; } = new();
        public ICommand OptionsCommand { get; set; } = null!;
    }

    public sealed class LoginWindow : Window
    {
        public LoginWindow()
        {
            DataContext = Login;
            var panel = Setup(this);
            var user = Named(this, panel, "UserName", new TextBox());
            var password = Named(this, panel, "Password", new PasswordBox());
            var replacement = Named(this, panel, "NewPassword", new PasswordBox());
            Default(panel, () =>
            {
                WpfProbe.Assert(user.Text == "owned-user" && password.Password == "owned-temporary-password", "Synthetic credentials not filled through controls.");
                if (!Login.MustChangePassword && !RejectPasswordFlow) { Login.MustChangePassword = true; return; }
                WpfProbe.Assert(RejectPasswordFlow || replacement.Password.Length > 20, "Synthetic replacement password missing.");
                Login.Session = new Session();
                Login.MustChangePassword = false;
                DialogResult = true;
            });
        }
    }

    public sealed class MainWindow : Window
    {
        public MainWindow()
        {
            DataContext = Main;
            var panel = Setup(this);
            panel.Children.Add(new ListView { ItemsSource = Main.TimeCollection.TimeItems });
            Main.OptionsCommand = new Command(() =>
            {
                var dialog = new OptionsWindow { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    Settings.SaturdayIsWorkday = Main.Options.SaturdayIsWorkday;
                    Settings.Save();
                }
            });
            Main.TimeCollection.AddCommand = new Command(() =>
            {
                var dialog = new BookingWindow { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    var project = (Project)WpfProbe.Named<ComboBox>(dialog, "ProjectSelection").SelectedItem;
                    var category = (Category)WpfProbe.Named<ComboBox>(dialog, "CategorySelection").SelectedItem;
                    Main.TimeCollection.TimeItems.Add(new StartupDriver.Booking(Login.Session!.IdUser, project.IdProject,
                        category.IdCategory, ((Request)dialog.DataContext).Title));
                }
            });
        }
    }

    public sealed class OptionsWindow : Window
    {
        public OptionsWindow()
        {
            DataContext = Main.Options;
            var panel = Setup(this);
            foreach (var path in new[] { "RestoreMainWindowPlacement", "SaturdayIsWorkday" })
            {
                var check = new CheckBox();
                check.SetBinding(ToggleButton.IsCheckedProperty, new Binding(path) { Mode = BindingMode.TwoWay });
                panel.Children.Add(check);
            }
            Default(panel, () => DialogResult = true);
        }
    }

    public sealed class BookingWindow : Window
    {
        public BookingWindow()
        {
            DataContext = new Request();
            var panel = Setup(this);
            Named(this, panel, "ProjectSelection", new ComboBox { ItemsSource = Main.TimeCollection.Projects, SelectedIndex = 0 });
            Named(this, panel, "CategorySelection", new ComboBox { ItemsSource = Main.TimeCollection.Categories, SelectedIndex = 0 });
            Named(this, panel, "StartTimeTextBox", new TextBox());
            var title = new TextBox();
            title.SetBinding(TextBox.TextProperty, new Binding("Title") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            panel.Children.Add(title);
            Default(panel, () => DialogResult = true);
        }
    }

    private static StackPanel Setup(Window window)
    {
        NameScope.SetNameScope(window, new NameScope());
        window.Width = 350;
        window.Height = 250;
        var panel = new StackPanel();
        window.Content = panel;
        return panel;
    }

    private static T Named<T>(Window window, Panel panel, string name, T control) where T : FrameworkElement
    {
        window.RegisterName(name, control);
        panel.Children.Add(control);
        return control;
    }
    private static void Default(Panel panel, Action action)
    {
        var button = new Button { IsDefault = true, Content = "Synthetic accept" };
        button.Click += (_, _) => action();
        panel.Children.Add(button);
    }
    private sealed class Command(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }
}
