using System.Data.Common;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TaskOTime.Validation;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.SequenceEqual(new[] { "--self-test" }))
            {
                SelfTest();
                return 0;
            }
            if (args.Length == 2 && args[0] == "--startup-self-test" &&
                new[] { "core", "invalid-login", "early-exit", "missing-ideal", "timeout" }.Contains(args[1]))
            {
                StartupDriverSelfTest.Run(args[1]);
                return 0;
            }
            var options = Parse(args);
            var connection = IsolatedConnection.Validate(options["--connection"], options["--owner"]);
            IsolatedConnection.VerifyOwnership(connection, options["--owner"]);
            var assemblyPath = Path.GetFullPath(options["--app"]);
            RequireModernApp(assemblyPath);
            var password = Environment.GetEnvironmentVariable("TASKOTIME_VALIDATION_PASSWORD");
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Set TASKOTIME_VALIDATION_PASSWORD to the isolated fixture user's password.");

            var provider = new DbConnectionStringBuilder
            {
                ["Data Source"] = connection.DataSource,
                ["Initial Catalog"] = connection.InitialCatalog,
                ["Integrated Security"] = true,
                ["Pooling"] = false,
                ["Connect Timeout"] = 15,
                ["Encrypt"] = false
            };
            var startup = options.GetValueOrDefault("--mode", "construction") == "startup";
            RunDesktop(assemblyPath, provider.ConnectionString, options["--user"], password, startup,
                options.GetValueOrDefault("--required-features") == "ideal",
                int.Parse(options.GetValueOrDefault("--timeout-seconds", "110")));
            Console.WriteLine(startup
                ? "Genuine App startup passed (" + options["--required-features"] + "): forced password UI, main/options/SQL booking, isolated settings and shutdown."
                : "Construction smoke passed: isolated login, main collection identity, categories, and dialog construction; genuine App startup NOT RUN.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Smoke failed: " + error.GetBaseException().Message);
            return 1;
        }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var required = new[] { "--app", "--connection", "--owner", "--user" };
        var names = required.Concat(new[] { "--mode", "--required-features", "--timeout-seconds" }).ToArray();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !names.Contains(args[index]) ||
                string.IsNullOrWhiteSpace(args[index + 1]) ||
                !options.TryAdd(args[index], args[index + 1]))
                throw new ArgumentException("Expected --app <net10 app.dll> --connection <isolated SQL connection> --owner <token> --user <fixture user>.");
        }
        if (required.Any(name => !options.ContainsKey(name)))
            throw new ArgumentException("All four explicit smoke options are required; there are no demo defaults.");
        var mode = options.GetValueOrDefault("--mode", "construction");
        if (mode != "construction" && mode != "startup")
            throw new ArgumentException("Mode must be construction or startup.");
        if (mode == "startup" && options.GetValueOrDefault("--required-features") is not ("core" or "ideal"))
            throw new ArgumentException("Startup requires explicit --required-features core|ideal.");
        if (mode == "construction" && options.ContainsKey("--required-features"))
            throw new ArgumentException("Required features apply only to genuine startup, never construction.");
        if (options.TryGetValue("--timeout-seconds", out var timeout) &&
            (!int.TryParse(timeout, out var seconds) || seconds < 1 || seconds > 1800))
            throw new ArgumentException("Timeout must be between 1 and 1800 seconds.");
        return options;
    }

    private static void RequireModernApp(string assemblyPath)
    {
        var runtimeConfig = Path.ChangeExtension(assemblyPath, ".runtimeconfig.json");
        using var json = JsonDocument.Parse(File.ReadAllText(runtimeConfig));
        var tfm = json.RootElement.GetProperty("runtimeOptions").GetProperty("tfm").GetString();
        if (tfm is null || !tfm.StartsWith("net10.0", StringComparison.Ordinal))
            throw new InvalidOperationException("This host requires the .NET 10 application, not Framework binaries.");
    }

    private static void RunDesktop(string assemblyPath, string providerConnection, string user, string password,
        bool startup, bool ideal, int timeoutSeconds)
    {
        var root = Path.GetDirectoryName(assemblyPath)!;
        var metadata = Path.Combine(root, "Model", "TaskOTime");
        foreach (var extension in new[] { ".csdl", ".ssdl", ".msl" })
            if (!File.Exists(metadata + extension))
                throw new FileNotFoundException("Missing deployed EF6 metadata: " + metadata + extension);

        var entity = new DbConnectionStringBuilder
        {
            ["metadata"] = metadata + ".csdl|" + metadata + ".ssdl|" + metadata + ".msl",
            ["provider"] = "System.Data.SqlClient",
            ["provider connection string"] = providerConnection
        };
        var oldMode = Environment.GetEnvironmentVariable("TASKOTIME_MODE");
        var oldConnection = Environment.GetEnvironmentVariable("TASKOTIME_CONNECTION_STRING");
        var oldDirectory = Environment.CurrentDirectory;
        var resolver = new AssemblyDependencyResolver(assemblyPath);
        Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
        {
            var path = resolver.ResolveAssemblyToPath(name);
            if (path is null)
            {
                var adjacent = Path.Combine(root, name.Name + ".dll");
                if (File.Exists(adjacent)) path = adjacent;
            }
            return path is null ? null : context.LoadFromAssemblyPath(path);
        }
        IntPtr ResolveNative(Assembly requestingAssembly, string name)
        {
            var path = resolver.ResolveUnmanagedDllToPath(name);
            return path is null ? IntPtr.Zero : NativeLibrary.Load(path);
        }
        Application? app = null;
        dynamic? login = null;
        var windows = new List<Window>();
        AssemblyLoadContext.Default.Resolving += Resolve;
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += ResolveNative;
        try
        {
            Environment.SetEnvironmentVariable("TASKOTIME_MODE", "Production");
            Environment.SetEnvironmentVariable("TASKOTIME_CONNECTION_STRING", entity.ConnectionString);
            Environment.CurrentDirectory = root;
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            var viewModel = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root, "TaskOTime.ViewModel.dll"));
            var settings = IsolatedSettings.Install(assembly);
            if (startup)
            {
                StartupDriver.Run(StartupDriver.ProductContract(assembly, viewModel), viewModel, user, password, ideal,
                    settings, timeoutSeconds, booking => StartupDriver.VerifySqlBooking(providerConnection, booking));
                return;
            }
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var resource in new[] { "Themes/ClassicDark.xaml", "Resources/Strings.xaml" })
                app.Resources.MergedDictionaries.Add((ResourceDictionary)Application.LoadComponent(
                    new Uri("/TaskOTime.App;component/" + resource, UriKind.Relative)));

            dynamic services = assembly.GetType("TaskOTime.App.DesktopServices", true)!
                .GetMethod("Create", Type.EmptyTypes)!.Invoke(null, null)!;
            login = Activator.CreateInstance(viewModel.GetType("TaskOTime.ViewModel.ViewModels.LoginViewModel", true)!,
                new object[] { services.Authentication })!;
            if (!login.Login(user, password))
            {
                if (!login.MustChangePassword ||
                    !login.ChangeTemporaryPassword(password, "Validation-" + Guid.NewGuid().ToString("N") + "!"))
                    throw new InvalidOperationException("Isolated fixture login/forced-password change failed.");
            }
            dynamic session = login.Session;
            dynamic main = services.CreateMain(session);
            var window = (Window)Activator.CreateInstance(assembly.GetType("TaskOTime.App.MainWindow", true)!,
                new object[] { main, services, session })!;
            windows.Add(window);
            window.ApplyTemplate();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var list = FindTimeList(window) ?? throw new InvalidOperationException("Time ListView was not constructed.");
            if (!ReferenceEquals(list.ItemsSource, main.TimeCollection.TimeItems))
                throw new InvalidOperationException("Time collection identity was lost.");
            if (main.TimeCollection.Categories.Count == 0 || main.TimeCollection.SelectedCategory is null)
                throw new InvalidOperationException("Fixture booking categories were not loaded.");

            object tenant = services.TenantFor(session);
            var maintenance = MainDataComposition.Resolve(viewModel, tenant.GetType().Assembly);
            MainDataComposition.Create(maintenance, tenant, (Guid)session.IdUser,
                (object)services.Admin, (object)services.Users, (object)services.Bookings, windows);
            windows.Add((Window)Activator.CreateInstance(assembly.GetType("TaskOTime.App.LoginWindow", true)!,
                new object[] { login, services.ModeDescription })!);
            windows.Add((Window)Activator.CreateInstance(assembly.GetType("TaskOTime.App.OptionsDialog", true)!,
                new object[] { main.Options })!);
        }
        finally
        {
            try
            {
                foreach (var window in windows) window.Close();
                if (login is not null) login.Logout();
                app?.Shutdown();
            }
            finally
            {
                AssemblyLoadContext.Default.Resolving -= Resolve;
                AssemblyLoadContext.Default.ResolvingUnmanagedDll -= ResolveNative;
                Environment.CurrentDirectory = oldDirectory;
                Environment.SetEnvironmentVariable("TASKOTIME_MODE", oldMode);
                Environment.SetEnvironmentVariable("TASKOTIME_CONNECTION_STRING", oldConnection);
            }
        }
    }

    private static ListView? FindTimeList(DependencyObject node)
    {
        if (node is ListView list) return list;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
        {
            var found = FindTimeList(child);
            if (found is not null) return found;
        }
        return null;
    }

    private static void SelfTest()
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Host is not STA.");
        var owner = Guid.NewGuid().ToString("N");
        var database = "TaskOTime_Validation_" + Guid.NewGuid().ToString("N");
        var valid = @"Data Source=(localdb)\MSSQLLocalDB;Integrated Security=True;Initial Catalog=" + database;
        _ = IsolatedConnection.Validate(valid, owner);
        var rejected = 0;
        foreach (var invalid in new[]
        {
            valid.Replace(database, "TaskOTime"),
            valid.Replace(database, "TaskOTime_AppServerIntegrationTests"),
            valid.Replace(database, "TaskOTime_Validation_not-a-guid"),
            valid.Replace(@"(localdb)\MSSQLLocalDB", "production"),
            valid + ";AttachDBFilename=shared.mdf",
            valid + ";Failover Partner=production",
            valid.Replace("Integrated Security=True", "Integrated Security=False;User ID=sa;Password=test")
        })
        {
            try { IsolatedConnection.Validate(invalid, owner); }
            catch (ArgumentException) { rejected++; }
        }
        if (rejected != 7) throw new InvalidOperationException("Connection guardrail self-test failed.");
        try { Parse(Array.Empty<string>()); throw new InvalidOperationException("Missing options were accepted."); }
        catch (ArgumentException) { }
        var explicitOptions = new[] { "--app", "app.dll", "--connection", valid, "--owner", owner, "--user", "fixture" };
        foreach (var invalid in new[]
        {
            new[] { "--mode", "startup" },
            new[] { "--mode", "startup", "--required-features", "optional" },
            new[] { "--required-features", "core" },
            new[] { "--mode", "unknown" },
            new[] { "--timeout-seconds", "0" }
        })
        {
            try { Parse(explicitOptions.Concat(invalid).ToArray()); throw new InvalidOperationException("Unsafe startup options accepted."); }
            catch (ArgumentException) { }
        }
        _ = Parse(explicitOptions.Concat(new[] { "--mode", "startup", "--required-features", "core" }).ToArray());
        try { IsolatedConnection.Validate(valid, ""); throw new InvalidOperationException("Missing owner was accepted."); }
        catch (ArgumentException) { }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        MainDataCompositionSelfTest.Run();
        OptionsCultureSelectionSelfTest.Run();
        ThemeLifetimeSelfTest.Run();
        var items = new object[] { "isolated STA probe" };
        var list = new ListView { ItemsSource = items };
        var window = new Window { Content = list };
        window.ApplyTemplate();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        if (!ReferenceEquals(FindTimeList(window)?.ItemsSource, items))
            throw new InvalidOperationException("STA WPF identity self-test failed.");
        window.Close();
        app.Shutdown();
        Console.WriteLine("STA host self-test passed: connection guards, legacy/modern maintenance composition and WPF identity; no SQL connection opened.");
    }
}
