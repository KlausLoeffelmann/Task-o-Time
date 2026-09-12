using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace TaskOTime.Validation
{
    internal static class ChildProcess
    {
        internal static int Run(string executable, IEnumerable<string> arguments, string password, int timeoutSeconds)
        {
            var start = new ProcessStartInfo(executable, string.Join(" ", arguments.Select(Quote)))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.EnvironmentVariables.Remove("TASKOTIME_MODE");
            start.EnvironmentVariables.Remove("TASKOTIME_CONNECTION_STRING");
            start.EnvironmentVariables.Remove("TASKOTIME_VALIDATION_PASSWORD");
            if (password != null) start.EnvironmentVariables["TASKOTIME_VALIDATION_PASSWORD"] = password;
            using (var process = new Process { StartInfo = start })
            {
                DataReceivedEventHandler output = (_, args) =>
                {
                    if (args.Data != null)
                        Console.WriteLine(password == null ? args.Data : args.Data.Replace(password, "[redacted]"));
                };
                process.OutputDataReceived += output;
                process.ErrorDataReceived += output;
                if (!process.Start()) throw new InvalidOperationException("Child process did not start.");
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(checked(timeoutSeconds * 1000)))
                {
                    // The smoke host does not spawn descendants. Ensure it has exited
                    // before the parent's finally block disposes the owned database.
#if NETFRAMEWORK
                    process.Kill();
#else
                    process.Kill(entireProcessTree: true);
#endif
                    process.WaitForExit();
                    throw new TimeoutException("Validation child exceeded its timeout and was terminated.");
                }
                process.WaitForExit();
                return process.ExitCode;
            }
        }

        internal static string Quote(string value)
        {
            var result = new StringBuilder("\"");
            var backslashes = 0;
            foreach (var character in value)
            {
                if (character == '\\') { backslashes++; continue; }
                if (character == '"')
                    result.Append('\\', backslashes * 2 + 1).Append('"');
                else
                    result.Append('\\', backslashes).Append(character);
                backslashes = 0;
            }
            return result.Append('\\', backslashes * 2).Append('"').ToString();
        }
    }
}
