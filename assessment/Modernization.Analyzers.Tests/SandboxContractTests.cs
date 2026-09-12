using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using ExternalEvaluation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Modernization.Analyzers.Tests;

[Trait("Category", "AnalyzerUnit")]
public sealed class SandboxContractTests
{
    [Fact]
    public void Guest_native_helper_compiles_as_Framework_compatible_CSharp5_without_execution()
    {
        var source = File.ReadAllText(Path.Combine(EvaluatorConfiguration.AssessmentRoot, "Sandbox", "RestrictedProcess.cs"));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("GuestHelper", [
            CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp5))
        ], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(compilation.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Prepared_wsb_disables_redirection_and_never_maps_expected_or_assessor_roots()
    {
        var script = Path.Combine(EvaluatorConfiguration.AssessmentRoot, "Sandbox", "Invoke-SandboxProbe.ps1");
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("& '" + script.Replace("'", "''") + "' -PrepareOnly | ConvertTo-Json -Compress");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await error);
        using var result = JsonDocument.Parse(await output);
        var root = result.RootElement.GetProperty("Artifacts").GetString()!;
        try
        {
            Assert.False(result.RootElement.GetProperty("Executed").GetBoolean());
            Assert.False(result.RootElement.GetProperty("FormalVerified").GetBoolean());
            var xml = XDocument.Load(result.RootElement.GetProperty("Configuration").GetString()!);
            foreach (var name in new[] { "Networking", "ClipboardRedirection", "AudioInput", "VideoInput", "PrinterRedirection", "vGPU" })
                Assert.Equal("Disable", xml.Root!.Element(name)!.Value);
            Assert.Equal("Enable", xml.Root!.Element("ProtectedClient")!.Value);
            var folders = xml.Descendants("MappedFolder").ToArray();
            Assert.Equal(3, folders.Length);
            Assert.Single(folders, f => f.Element("ReadOnly")!.Value == "false");
            Assert.DoesNotContain(folders, f => f.Element("HostFolder")!.Value.TrimEnd('\\') ==
                EvaluatorConfiguration.AssessmentRoot.TrimEnd('\\'));
        }
        finally { Directory.Delete(root, true); }
    }
}
