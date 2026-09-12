using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Xml.Linq;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Security;
using TaskOTime.AppServer.Services;
using TaskOTime.DataLayer;
using TaskOTime.DTOs;

namespace TaskOTime.Validation
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                var options = Parse(args);
                switch (args[0])
                {
                    case "self-test":
                        SelfTest();
                        return 0;
                    case "probe-timeout":
                        Thread.Sleep(30000);
                        return 1;
                    case "probe":
                        Probe(options);
                        return 0;
                    case "fixture-check":
                        WithFixture(CheckFixture);
                        CheckFailureCleanup();
                        Console.WriteLine("Fixture-check passed: real SQL setup/authentication, child failures/timeout, and owned cleanup. Desktop NOT RUN.");
                        return 0;
                    case "desktop":
                    case "startup":
                        ValidateDesktop(options);
                        var seconds = options.ContainsKey("--timeout-seconds") ? int.Parse(options["--timeout-seconds"]) : 120;
                        WithFixture(fixture =>
                        {
                            var arguments = new List<string>
                            {
                                Path.GetFullPath(options["--host"]), "--app", Path.GetFullPath(options["--app"]),
                                "--connection", fixture.Database.ProviderConnectionString,
                                "--owner", fixture.Database.OwnerToken, "--user", fixture.UserName
                            };
                            if (args[0] == "startup")
                                arguments.AddRange(new[]
                                {
                                    "--mode", "startup", "--required-features", options["--required-features"],
                                    "--timeout-seconds", Math.Max(1, seconds - 10).ToString()
                                });
                            var code = ChildProcess.Run("dotnet", arguments, fixture.Password, seconds);
                            if (code != 0) throw new InvalidOperationException("Desktop smoke child failed with exit code " + code + ".");
                        });
                        Console.WriteLine(args[0] == "startup"
                            ? "Genuine App startup (" + options["--required-features"] + ") passed and the owned SQL fixture was removed."
                            : "Construction smoke passed and the owned SQL fixture was removed; genuine App startup NOT RUN.");
                        return 0;
                    default:
                        throw new ArgumentException("Unknown mode.");
                }
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("Validation failed: " + error.GetBaseException().Message);
                return 1;
            }
        }

        private static Dictionary<string, string> Parse(string[] args)
        {
            if (args.Length == 0)
                throw new ArgumentException("Specify self-test, fixture-check, desktop, or startup --required-features core|ideal --app <net10 app.dll> --host <StaSmoke.dll>.");
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            string[] names;
            switch (args[0])
            {
                case "self-test":
                case "fixture-check":
                case "probe-timeout":
                    names = Array.Empty<string>();
                    break;
                case "desktop":
                    names = new[] { "--app", "--host", "--timeout-seconds" };
                    break;
                case "startup":
                    names = new[] { "--app", "--host", "--timeout-seconds", "--required-features" };
                    break;
                case "probe":
                    names = new[] { "--connection", "--owner", "--user" };
                    break;
                default:
                    throw new ArgumentException("Unknown validation mode.");
            }
            for (var index = 1; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length || !names.Contains(args[index]) ||
                    string.IsNullOrWhiteSpace(args[index + 1]) || result.ContainsKey(args[index]))
                    throw new ArgumentException("Unknown, missing or duplicate validation argument.");
                result.Add(args[index], args[index + 1]);
            }
            if ((args[0] == "desktop" || args[0] == "startup") && (!result.ContainsKey("--app") || !result.ContainsKey("--host")))
                throw new ArgumentException("Desktop requires explicit --app and --host paths.");
            if (args[0] == "startup" && (!result.TryGetValue("--required-features", out var features) ||
                (features != "core" && features != "ideal")))
                throw new ArgumentException("Startup requires explicit --required-features core|ideal.");
            if (args[0] == "probe" && result.Count != 3)
                throw new ArgumentException("Probe requires the complete isolated connection contract.");
            if (result.TryGetValue("--timeout-seconds", out var timeout) &&
                (!int.TryParse(timeout, out var seconds) || seconds < 1 || seconds > 1800))
                throw new ArgumentException("Timeout must be between 1 and 1800 seconds.");
            return result;
        }

        private static void ValidateDesktop(Dictionary<string, string> options)
        {
            RequireNet10Output(options["--app"]);
            RequireNet10Output(options["--host"]);
            using (var process = Process.Start(new ProcessStartInfo("dotnet", "--list-runtimes")
            {
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
            }))
            {
                var runtimes = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0 || !runtimes.Split('\n').Any(line => line.StartsWith("Microsoft.WindowsDesktop.App 10.0.", StringComparison.Ordinal)))
                    throw new InvalidOperationException("The .NET 10 WindowsDesktop runtime is required; desktop was not run.");
            }
        }

        private static void RequireNet10Output(string path)
        {
            path = Path.GetFullPath(path);
            if (!File.Exists(path) || !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Desktop requires existing .NET 10 DLL output, not a Framework executable.");
            var configuration = Path.ChangeExtension(path, ".runtimeconfig.json");
            if (!File.Exists(configuration))
                throw new ArgumentException("Missing .NET 10 runtime configuration: " + configuration);
            using (var stream = File.OpenRead(configuration))
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(stream, System.Xml.XmlDictionaryReaderQuotas.Max))
            {
                var tfm = XDocument.Load(reader).Root.Element("runtimeOptions")?.Element("tfm")?.Value;
                if (tfm == null || !tfm.StartsWith("net10.0", StringComparison.Ordinal))
                    throw new ArgumentException("Desktop requires a .NET 10 target, not another framework.");
            }
        }

        private static void WithFixture(Action<SeededFixture> action)
        {
            var fixture = new SeededFixture();
            var initialized = false;
            try
            {
                fixture.Create();
                initialized = true;
                Console.WriteLine("Created and seeded owned database " + fixture.Database.DatabaseName + ".");
                action(fixture);
            }
            finally
            {
                fixture.Dispose();
                if (initialized)
                {
                    if (SeededFixture.Exists(fixture.Database.DatabaseName))
                        throw new InvalidOperationException("Owned database remained after cleanup.");
                    Console.WriteLine("Verified owned database cleanup.");
                }
            }
        }

        private sealed class ExpectedChildFailureException : Exception { }

        private static void CheckFailureCleanup()
        {
            try
            {
                WithFixture(fixture =>
                {
                    var code = RunSelf(new[]
                    {
                        "probe", "--connection", fixture.Database.ProviderConnectionString,
                        "--owner", fixture.Database.OwnerToken, "--user", fixture.UserName
                    }, null);
                    if (code == 0) throw new InvalidOperationException("The negative child unexpectedly succeeded.");
                    throw new ExpectedChildFailureException();
                });
                throw new InvalidOperationException("Child failure did not propagate.");
            }
            catch (ExpectedChildFailureException)
            {
                Console.WriteLine("Verified cleanup while propagating a child failure.");
            }
        }

        private static int RunSelf(string[] args, string password, int timeoutSeconds = 60)
        {
            var executable = typeof(Program).Assembly.Location;
            return executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? ChildProcess.Run(executable, args, password, timeoutSeconds)
                : ChildProcess.Run("dotnet", new[] { executable }.Concat(args), password, timeoutSeconds);
        }

        private static void CheckFixture(SeededFixture fixture)
        {
            var inheritedPassword = Environment.GetEnvironmentVariable("TASKOTIME_VALIDATION_PASSWORD");
            CheckMarkerGuardrail(fixture);
            var args = new[]
            {
                "probe", "--connection", fixture.Database.ProviderConnectionString,
                "--owner", fixture.Database.OwnerToken, "--user", fixture.UserName
            };
            if (RunSelf(args, fixture.Password) != 0)
                throw new InvalidOperationException("Real SQL/authentication child probe failed.");
            if (RunSelf(args, null) == 0)
                throw new InvalidOperationException("Child accepted a missing ephemeral password.");
            var wrongOwner = (string[])args.Clone();
            wrongOwner[4] = Guid.NewGuid().ToString("N");
            if (RunSelf(wrongOwner, fixture.Password) == 0)
                throw new InvalidOperationException("Child accepted a mismatched ownership token.");
            try
            {
                RunSelf(new[] { "probe-timeout" }, null, 1);
                throw new InvalidOperationException("Child timeout was not enforced.");
            }
            catch (TimeoutException) { }
            if (!SeededFixture.Exists(fixture.Database.DatabaseName))
                throw new InvalidOperationException("A child removed its parent's database.");
            if (Environment.GetEnvironmentVariable("TASKOTIME_VALIDATION_PASSWORD") != inheritedPassword)
                throw new InvalidOperationException("A child credential leaked into the parent environment.");
        }

        private static void Probe(Dictionary<string, string> options)
        {
            var password = Environment.GetEnvironmentVariable("TASKOTIME_VALIDATION_PASSWORD");
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("An ephemeral child-only password is required.");
            SeededFixture.PrepareMetadata();
            var factory = SeededFixture.VerifyConnection(options["--connection"], options["--owner"]);
            var authentication = new AuthenticationService(factory, new Pbkdf2PasswordHasher());
            var authenticated = SeededFixture.Require(authentication.Authenticate(new AuthenticateUserRequest
            {
                UserIdentOrEmail = options["--user"], Password = password
            }));
            if (!authenticated.MustChangePassword)
                throw new InvalidOperationException("Expected the seeded initial-password change requirement.");
            SeededFixture.VerifyMarkerCategories(factory, authenticated.User.IdUser);
            var replacement = "Replacement-" + Guid.NewGuid().ToString("N") + "!a9";
            SeededFixture.Require(authentication.ChangeTemporaryPassword(
                authenticated.User.IdTenant, authenticated.User.IdUser, password, replacement));
            var changed = SeededFixture.Require(authentication.Authenticate(new AuthenticateUserRequest
            {
                UserIdentOrEmail = options["--user"], Password = replacement
            }));
            if (changed.MustChangePassword || authentication.Authenticate(new AuthenticateUserRequest
            {
                UserIdentOrEmail = options["--user"], Password = password
            }).Success)
                throw new InvalidOperationException("Initial password change did not replace the temporary credential.");
            Console.WriteLine("Child verified ownership, marker category IDs, real authentication and forced initial-password change.");
        }

        private static void CheckMarkerGuardrail(SeededFixture fixture)
        {
            Guid owner;
            using (var context = fixture.Database.CreateContext())
            {
                var marker = context.Category.Single(category => category.IdCategory == SystemTimeMarkerIds.StopMarkCategoryId);
                owner = marker.IdUser;
                marker.CategoryName = "Invalid fixture marker";
                context.SaveChanges();
            }
            try
            {
                var rejected = false;
                try { SeededFixture.VerifyMarkerCategories(fixture.Database.CreateContext, owner); }
                catch (InvalidOperationException) { rejected = true; }
                if (!rejected) throw new InvalidOperationException("Corrupted marker metadata was accepted.");
            }
            finally
            {
                using (var context = fixture.Database.CreateContext())
                {
                    SystemTimeMarkerSeed.EnsureCategories(context, owner);
                    context.SaveChanges();
                }
            }
            SeededFixture.VerifyMarkerCategories(fixture.Database.CreateContext, owner);
            Console.WriteLine("Verified marker metadata rejection and idempotent owned-fixture reseeding.");
        }

        private static void SelfTest()
        {
            var rejected = 0;
            foreach (var args in new[]
            {
                Array.Empty<string>(), new[] { "unknown" }, new[] { "desktop" },
                new[] { "desktop", "--app", "missing.dll" },
                new[] { "desktop", "--app", "a.dll", "--host", "h.dll", "--timeout-seconds", "0" },
                new[] { "desktop", "--app", "a.dll", "--host", "h.dll", "--app", "b.dll" },
                new[] { "fixture-check", "--connection", "shared" },
                new[] { "startup", "--app", "a.dll", "--host", "h.dll" },
                new[] { "startup", "--app", "a.dll", "--host", "h.dll", "--required-features", "optional" },
                new[] { "desktop", "--app", "a.dll", "--host", "h.dll", "--required-features", "core" }
            })
            {
                try { Parse(args); }
                catch (ArgumentException) { rejected++; }
            }
            if (rejected != 10)
                throw new InvalidOperationException("CLI argument rejection guardrails failed.");
            Parse(new[] { "startup", "--app", "a.dll", "--host", "h.dll", "--required-features", "core" });
            rejected = 0;
#if NETFRAMEWORK
            try { RequireNet10Output(typeof(Program).Assembly.Location); }
            catch (ArgumentException) { rejected++; }
            if (rejected != 1)
                throw new InvalidOperationException("Framework output was accepted as a modern desktop.");
#else
            RequireNet10Output(typeof(Program).Assembly.Location);
#endif
            rejected = 0;
            try { RequireNet10Output(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Guid.NewGuid() + ".dll")); }
            catch (ArgumentException) { rejected++; }
            if (rejected != 1 || ChildProcess.Quote("a b") != "\"a b\"" ||
                ChildProcess.Quote("a\"b") != "\"a\\\"b\"" ||
                ChildProcess.Quote("a\\") != "\"a\\\\\"")
                throw new InvalidOperationException("CLI/runtime/argument quoting guardrails failed.");
            var owner = Guid.NewGuid().ToString("N");
            var name = "TaskOTime_Validation_" + Guid.NewGuid().ToString("N");
            var valid = @"Data Source=(localdb)\MSSQLLocalDB;Integrated Security=True;Initial Catalog=" + name;
            SeededFixture.ValidateConnection(valid, owner);
            var unsafeConnectionsRejected = 0;
            foreach (var invalid in new[]
            {
                valid.Replace(name, "TaskOTime"),
                valid.Replace(name, "TaskOTime_AppServerIntegrationTests"),
                valid.Replace(name, "TaskOTime_Validation_invalid"),
                valid.Replace(@"(localdb)\MSSQLLocalDB", "production"),
                valid + ";AttachDBFilename=shared.mdf",
                valid + ";Failover Partner=production",
                valid.Replace("Integrated Security=True", "Integrated Security=False")
            })
            {
                try { SeededFixture.ValidateConnection(invalid, owner); }
                catch (ArgumentException) { unsafeConnectionsRejected++; }
            }
            try { SeededFixture.ValidateConnection(valid, ""); }
            catch (ArgumentException) { unsafeConnectionsRejected++; }
            if (unsafeConnectionsRejected != 8)
                throw new InvalidOperationException("Isolated connection guardrails failed.");
            Console.WriteLine("Fixture runner self-test passed: CLI/runtime/quoting guardrails; SQL and desktop NOT RUN.");
        }
    }
}
