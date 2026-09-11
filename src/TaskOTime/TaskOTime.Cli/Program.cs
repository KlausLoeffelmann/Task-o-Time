using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaskOTime.DataLayer;

namespace TaskOTime.Cli
{
    internal static class Program
    {
        private const int Success = 0;
        private const int InvalidArguments = 2;
        private const int ExecutionFailed = 3;

        private static int Main(string[] args)
        {
            try
            {
                var options = CommandLineOptions.Parse(args);
                if (options.ShowHelp)
                {
                    WriteUsage();
                    return Success;
                }

                Execute(options);
                return Success;
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                WriteUsage();
                return InvalidArguments;
            }
            catch (Exception ex)
            {
                WriteException(ex);
                return ExecutionFailed;
            }
        }

        private static void Execute(CommandLineOptions options)
        {
            var admin = new TaskOTimeDatabaseAdmin();

            if (options.ResetDatabase)
            {
                var scriptPath = FindGenerationScript();
                admin.ResetDatabase(scriptPath);
                Console.WriteLine("Created a new TaskOTime database from " + scriptPath);
            }

            if (options.CreateDemoData)
            {
                var demoOptions = DemoDataConfigLoader.Load(options.DemoDataConfigPath);
                var report = ExecuteWithCliDirectory(() => new DemoDataGenerator().CreateDemoData(demoOptions));
                Console.WriteLine("Created demo data. " + report.ToSummaryString());
            }

            if (options.ValidateDemoData)
            {
                var demoOptions = DemoDataConfigLoader.Load(options.ValidateDemoDataConfigPath);
                var report = ExecuteWithCliDirectory(DemoDataSmokeReport.Collect);
                report.Validate(demoOptions.TimeItemDates, DateTime.Today);
                Console.WriteLine("Demo data smoke validation passed. " + report.ToSummaryString());
            }

            if (options.ExportMode != ExportMode.None)
            {
                var outputDirectory = Path.Combine(
                    Environment.CurrentDirectory,
                    "TaskOTimeExport-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                var exported = admin.ExportTables(outputDirectory, options.ExportMode == ExportMode.All ? null : options.ExportTables);
                Console.WriteLine("Exported " + exported.Count + " table(s) to " + outputDirectory);
            }

            if (!string.IsNullOrWhiteSpace(options.BackupPath))
            {
                admin.BackupDatabase(options.BackupPath);
                Console.WriteLine("Backed up TaskOTime to " + Path.GetFullPath(options.BackupPath));
            }
        }

        private static string FindGenerationScript()
        {
            var baseDirectoryScript = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DbGenerationScript.sql");
            if (File.Exists(baseDirectoryScript))
            {
                return baseDirectoryScript;
            }

            var sourceTreeScript = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                @"..\..\..\TaskOTime.DataLayer\DbGenerationScript.sql"));
            if (File.Exists(sourceTreeScript))
            {
                return sourceTreeScript;
            }

            throw new FileNotFoundException("DbGenerationScript.sql was not found next to the CLI executable or in the source tree.", baseDirectoryScript);
        }

        private static T ExecuteWithCliDirectory<T>(Func<T> action)
        {
            var originalDirectory = Environment.CurrentDirectory;
            try
            {
                Environment.CurrentDirectory = AppDomain.CurrentDomain.BaseDirectory;
                return action();
            }
            finally
            {
                Environment.CurrentDirectory = originalDirectory;
            }
        }

        private static void WriteUsage()
        {
            Console.WriteLine("TaskOTime.Cli");
            Console.WriteLine("  --new");
            Console.WriteLine("  --createDemoData[:demodataconfig.json]");
            Console.WriteLine("  --validateDemoData[:demodataconfig.json]");
            Console.WriteLine("  Demo booking dates: timeItemDates { days: 30, includeToday: true, skipWeekends: false }");
            Console.WriteLine("  --export:all");
            Console.WriteLine("  --export:tables table1, table2, table3");
            Console.WriteLine("  --backup:\"path-and-filename.bak\"");
        }

        private static void WriteException(Exception exception)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                Console.Error.WriteLine(current.Message);
            }
        }

        private enum ExportMode
        {
            None,
            All,
            Tables
        }

        private sealed class CommandLineOptions
        {
            public bool ShowHelp { get; private set; }
            public bool ResetDatabase { get; private set; }
            public bool CreateDemoData { get; private set; }
            public bool ValidateDemoData { get; private set; }
            public string DemoDataConfigPath { get; private set; }
            public string ValidateDemoDataConfigPath { get; private set; }
            public ExportMode ExportMode { get; private set; }
            public IReadOnlyList<string> ExportTables { get; private set; }
            public string BackupPath { get; private set; }

            public static CommandLineOptions Parse(string[] args)
            {
                var options = new CommandLineOptions
                {
                    ExportMode = ExportMode.None,
                    ExportTables = Array.Empty<string>()
                };

                if (args == null || args.Length == 0)
                {
                    options.ShowHelp = true;
                    return options;
                }

                for (var i = 0; i < args.Length; i++)
                {
                    var arg = args[i];
                    if (IsOption(arg, "--help") || IsOption(arg, "-?"))
                    {
                        options.ShowHelp = true;
                    }
                    else if (IsOption(arg, "--new"))
                    {
                        options.ResetDatabase = true;
                    }
                    else if (StartsWithOption(arg, "--createDemoData"))
                    {
                        options.CreateDemoData = true;
                        options.DemoDataConfigPath = ReadOptionValue(arg, "--createDemoData");
                        if (string.IsNullOrWhiteSpace(options.DemoDataConfigPath))
                        {
                            options.DemoDataConfigPath = ReadOptionalFollowingValue(args, ref i);
                        }
                    }
                    else if (StartsWithOption(arg, "--validateDemoData"))
                    {
                        options.ValidateDemoData = true;
                        options.ValidateDemoDataConfigPath = ReadOptionValue(arg, "--validateDemoData");
                        if (string.IsNullOrWhiteSpace(options.ValidateDemoDataConfigPath))
                        {
                            options.ValidateDemoDataConfigPath = ReadOptionalFollowingValue(args, ref i);
                        }
                    }
                    else if (StartsWithOption(arg, "--backup"))
                    {
                        options.BackupPath = ReadOptionValue(arg, "--backup");
                        if (string.IsNullOrWhiteSpace(options.BackupPath))
                        {
                            options.BackupPath = ReadOptionalFollowingValue(args, ref i);
                        }

                        if (string.IsNullOrWhiteSpace(options.BackupPath))
                        {
                            throw new ArgumentException("--backup requires a value.");
                        }
                    }
                    else if (StartsWithOption(arg, "--export"))
                    {
                        var exportValue = RequireOptionValue(arg, "--export");
                        if (string.Equals(exportValue, "all", StringComparison.OrdinalIgnoreCase))
                        {
                            options.ExportMode = ExportMode.All;
                        }
                        else if (exportValue.StartsWith("tables", StringComparison.OrdinalIgnoreCase))
                        {
                            options.ExportMode = ExportMode.Tables;
                            var inlineTables = ReadTablesInline(exportValue);
                            if (!string.IsNullOrWhiteSpace(inlineTables))
                            {
                                options.ExportTables = SplitTables(inlineTables);
                            }
                            else
                            {
                                var tableText = ReadFollowingTableArguments(args, ref i);
                                options.ExportTables = SplitTables(tableText);
                            }
                        }
                        else
                        {
                            throw new ArgumentException("Unsupported export mode: " + exportValue);
                        }
                    }
                    else
                    {
                        throw new ArgumentException("Unknown option: " + arg);
                    }
                }

                if (options.ExportMode == ExportMode.Tables && options.ExportTables.Count == 0)
                {
                    throw new ArgumentException("--export:tables requires at least one table name.");
                }

                return options;
            }

            private static bool IsOption(string value, string option)
            {
                return string.Equals(value, option, StringComparison.OrdinalIgnoreCase);
            }

            private static bool StartsWithOption(string value, string option)
            {
                return value.StartsWith(option, StringComparison.OrdinalIgnoreCase);
            }

            private static string ReadOptionValue(string value, string option)
            {
                if (value.Length == option.Length)
                {
                    return null;
                }

                if (value.Length > option.Length && value[option.Length] == ':')
                {
                    return value.Substring(option.Length + 1).Trim().Trim('"');
                }

                throw new ArgumentException("Invalid option syntax: " + value);
            }

            private static string RequireOptionValue(string value, string option)
            {
                var optionValue = ReadOptionValue(value, option);
                if (string.IsNullOrWhiteSpace(optionValue))
                {
                    throw new ArgumentException(option + " requires a value.");
                }

                return optionValue;
            }

            private static string ReadTablesInline(string exportValue)
            {
                if (exportValue.Length == "tables".Length)
                {
                    return null;
                }

                if (exportValue.Length > "tables".Length && exportValue["tables".Length] == ':')
                {
                    return exportValue.Substring("tables".Length + 1);
                }

                return null;
            }

            private static string ReadFollowingTableArguments(string[] args, ref int index)
            {
                var parts = new List<string>();
                while (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    index++;
                    parts.Add(args[index]);
                }

                return string.Join(" ", parts);
            }

            private static string ReadOptionalFollowingValue(string[] args, ref int index)
            {
                if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    index++;
                    return args[index].Trim().Trim('"');
                }

                return null;
            }

            private static IReadOnlyList<string> SplitTables(string tableText)
            {
                if (string.IsNullOrWhiteSpace(tableText))
                {
                    return Array.Empty<string>();
                }

                return tableText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Where(t => t.Length > 0)
                    .ToArray();
            }
        }
    }
}
