using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace TaskOTime.Theme.CaptureHost
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            // WPF otherwise produces transparent captures in disconnected/headless Windows sessions.
            AppContext.SetSwitch("Switch.System.Windows.Media.ShouldRenderEvenWhenNoDisplayDevicesAreAvailable", true);
            CaptureOptions options;
            try
            {
                options = CaptureOptions.Parse(args);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine("Usage: TaskOTime.Theme.CaptureHost --capture --application-root <absolute-build-directory> --output <new-absolute-directory>");
                return 64;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }

            var succeeded = false;
            var failed = false;
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.UnhandledException += (_, e) =>
            {
                failed = true;
                Console.Error.WriteLine(e.Exception);
                e.Handled = true;
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            };
            dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    VisualCapture.Run(options);
                    succeeded = true;
                }
                catch (Exception ex)
                {
                    failed = true;
                    Console.Error.WriteLine(ex);
                }
                finally
                {
                    Application.Current?.Shutdown();
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.ApplicationIdle);
                }
            }));
            try
            {
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
            if (!succeeded || failed) return 1;
            Console.WriteLine("THEME-CAPTURE-PASS:" + options.OutputDirectory);
            return 0;
        }
    }

    internal sealed class CaptureOptions
    {
        public string ApplicationRoot { get; private set; }
        public string OutputDirectory { get; private set; }

        public static CaptureOptions Parse(string[] args)
        {
            if (args.Length != 5 || args[0] != "--capture" || args[1] != "--application-root" || args[3] != "--output")
                throw new ArgumentException("Capture is opt-in and requires both explicit directories.");
            if (!IsAbsoluteWindowsPath(args[2]) || !IsAbsoluteWindowsPath(args[4]))
                throw new ArgumentException("Application root and output must be absolute paths.");
            var root = Path.GetFullPath(args[2]);
            var output = Path.GetFullPath(args[4]).TrimEnd(Path.DirectorySeparatorChar);
            if (!Directory.Exists(root) || !File.Exists(Path.Combine(root, "TaskOTime.ViewModel.dll")) ||
                (!File.Exists(Path.Combine(root, "TaskOTime.App.dll")) && !File.Exists(Path.Combine(root, "TaskOTime.App.exe"))))
                throw new ArgumentException("Application root must contain the built TaskOTime.App and TaskOTime.ViewModel assemblies.");
            if (Directory.Exists(output) || File.Exists(output))
                throw new ArgumentException("Output must be a new directory; existing output is never reused.");
            if (string.IsNullOrEmpty(Path.GetDirectoryName(output)) || !Directory.Exists(Path.GetDirectoryName(output)))
                throw new ArgumentException("The output directory's parent must already exist.");
            return new CaptureOptions { ApplicationRoot = root, OutputDirectory = output };
        }

        private static bool IsAbsoluteWindowsPath(string path) =>
            path.StartsWith(@"\\", StringComparison.Ordinal) ||
            (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && path[2] == '\\');
    }
}
