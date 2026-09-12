using System.Data.Common;
using System.IO;
using System.Reflection;
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
            RunDesktop(assemblyPath, provider.ConnectionString, options["--user"], password);
            Console.WriteLine("SQL/WPF smoke passed: isolated login, main collection identity, categories, and dialog construction.");
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
        var names = new[] { "--app", "--connection", "--owner", "--user" };
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !names.Contains(args[index]) ||
                string.IsNullOrWhiteSpace(args[index + 1]) ||
                !options.TryAdd(args[index], args[index + 1]))
                throw new ArgumentException("Expected --app <net10 app.dll> --connection <isolated SQL connection> --owner <token> --user <fixture user>.");
        }
        if (options.Count != names.Length)
            throw new ArgumentException("All four explicit smoke options are required; there are no demo defaults.");
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

    private static void RunDesktop(string assemblyPath, string providerConnection, string user, string password)
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
        Application? app = null;
        dynamic? login = null;
        var windows = new List<Window>();
        AssemblyLoadContext.Default.Resolving += Resolve;
        try
        {
            Environment.SetEnvironmentVariable("TASKOTIME_MODE", "Production");
            Environment.SetEnvironmentVariable("TASKOTIME_CONNECTION_STRING", entity.ConnectionString);
            Environment.CurrentDirectory = root;
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            var viewModel = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root, "TaskOTime.ViewModel.dll"));
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

            var master = (Window)Activator.CreateInstance(viewModel.GetType("TaskOTime.ViewModel.Views.MasterDataWindow", true)!)!;
            windows.Add(master);
            _ = Activator.CreateInstance(viewModel.GetType("TaskOTime.ViewModel.ViewModels.MasterDataViewModel", true)!,
                new object[] { master, services.TenantFor(session), session.IdUser, services.Admin, services.Users, services.Bookings, 1 });
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
        try { IsolatedConnection.Validate(valid, ""); throw new InvalidOperationException("Missing owner was accepted."); }
        catch (ArgumentException) { }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var items = new object[] { "isolated STA probe" };
        var list = new ListView { ItemsSource = items };
        var window = new Window { Content = list };
        window.ApplyTemplate();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        if (!ReferenceEquals(FindTimeList(window)?.ItemsSource, items))
            throw new InvalidOperationException("STA WPF identity self-test failed.");
        window.Close();
        app.Shutdown();
        Console.WriteLine("STA host self-test passed: nine rejection cases and WPF collection identity; no SQL connection opened.");
    }
}
