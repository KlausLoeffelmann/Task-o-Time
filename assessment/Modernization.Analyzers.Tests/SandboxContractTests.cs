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
    [Theory]
    [InlineData(59)]
    [InlineData(601)]
    public async Task Cli_execution_budget_rejects_unbounded_values_before_guest_launch(int seconds)
    {
        var result = await ProtectedCompilerTests.Run("""
            function Get-CimInstance { throw 'UNEXPECTED_GUEST_CHECK' }
            try { & (Join-Path $sandbox 'Invoke-SandboxProbe.ps1') -CliTimeoutSeconds __SECONDS__; throw 'UNEXPECTED_ACCEPTANCE' }
            catch {
                if($_.FullyQualifiedErrorId -notlike 'ParameterArgumentValidationError,*') { throw }
                Write-Output 'bounded-rejection'
            }
            """.Replace("__SECONDS__", seconds.ToString()));
        Assert.Equal("bounded-rejection", result.Trim());
    }

    [Theory]
    [InlineData(60)]
    [InlineData(600)]
    public async Task Prepared_cli_job_binds_its_explicit_execution_budget_without_running_it(int seconds)
    {
        var root = Path.Combine(EvaluatorConfiguration.ArtifactRoot, "cli-budget-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var result = await ProtectedCompilerTests.Run("$root='" + root.Replace("'", "''") + "'; " + """
                New-Item -ItemType Directory -Path (Join-Path $root 'binary'),(Join-Path $root 'input') | Out-Null
                [IO.File]::WriteAllText((Join-Path $root 'binary\Unit.dll'),'owned non-executable contract stub')
                $prepared=& (Join-Path $sandbox 'Invoke-SandboxProbe.ps1') -PrepareOnly `
                    -BinaryRoot (Join-Path $root 'binary') -EntryAssembly 'Unit.dll' `
                    -InputRoot (Join-Path $root 'input') -CommandArguments @('{input}','{output}') -CliTimeoutSeconds __SECONDS__
                try {
                    $job=Get-Content (Join-Path $prepared.Artifacts 'payload\job.json') -Raw | ConvertFrom-Json
                    if($prepared.Executed -or $prepared.FormalVerified -or $job.CliTimeoutSeconds -ne __SECONDS__) { throw 'Changed execution budget' }
                    Write-Output 'prepared-bounded-policy'
                } finally { Remove-Item -LiteralPath $prepared.Artifacts -Recurse -Force }
                """.Replace("__SECONDS__", seconds.ToString()));
            Assert.Equal("prepared-bounded-policy", result.Trim());
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unsupported_producer_signing_is_rejected_before_preparation_or_guest_launch(bool prepareOnly)
    {
        var script = Path.Combine(EvaluatorConfiguration.AssessmentRoot, "Sandbox", "Invoke-SandboxProbe.ps1");
        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("$ErrorActionPreference='Stop'; " +
            "function Get-CimInstance { throw 'Unexpected feature check' }; " +
            "function Start-Process { throw 'Unexpected guest launch' }; " +
            "try { & '" + script.Replace("'", "''") + "' -SourceRoot 'Z:\\nonexistent-source' " +
            "-Projects 'Payload.csproj' -SignProducerProof " + (prepareOnly ? "-PrepareOnly " : "") +
            "; exit 3 } catch { if ($_.Exception.Message -like 'Unsupported producer provenance:*') " +
            "{ Write-Output $_.Exception.Message; exit 0 }; Write-Error $_; exit 2 }");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await Task.WhenAll(process.WaitForExitAsync(), output, error).WaitAsync(TimeSpan.FromSeconds(30)); }
        catch (TimeoutException) { if (!process.HasExited) process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await error);
        Assert.Contains("Signing is disabled", await output);
    }

    [Theory]
    [InlineData("""{"Status":"succeeded","OutputFiles":[]}""", true)]
    [InlineData("""{"Status":"failed"}""", false)]
    [InlineData("""{"Status":"failed","Status":"succeeded"}""", false)]
    [InlineData("""{"status":"succeeded"}""", false)]
    [InlineData("""{"Status":true}""", false)]
    [InlineData("""[{"Status":"succeeded"}]""", false)]
    [InlineData("{", false)]
    public async Task Host_evidence_validation_matches_replay_schema_without_starting_a_guest(string json, bool succeeds)
    {
        var root = Path.Combine(EvaluatorConfiguration.ArtifactRoot, "sandbox-evidence-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "run.json");
            File.WriteAllText(file, json, new System.Text.UTF8Encoding(true));
            var replayError = Record.Exception(() => ToolReplay.ReadExecutionArtifact(root,
                new("run.json", "Status", "succeeded"), "initial"));
            Assert.Equal(succeeds, replayError == null);
            var helper = Path.Combine(EvaluatorConfiguration.AssessmentRoot, "Sandbox", "OutputEvidence.ps1");
            var start = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add("$ErrorActionPreference='Stop'; . '" + helper.Replace("'", "''") +
                "'; $root='" + root.Replace("'", "''") + "'; $hash=(Get-FileHash (Join-Path $root 'run.json')).Hash; " +
                "Read-ExecutionEvidence $root 'run.json' 'Status' 'succeeded' @{'run.json'=$hash} | ConvertTo-Json -Compress");
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            Assert.True((process.ExitCode == 0) == succeeds, await error);
            if (succeeds)
            {
                using var result = JsonDocument.Parse(await output);
                Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))),
                    result.RootElement.GetProperty("Sha256").GetString());
                Assert.Equal("succeeded", result.RootElement.GetProperty("Status").GetString());
            }
        }
        finally { Directory.Delete(root, true); }
    }

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
