using System.Text.Json;
using ExternalEvaluation;
using Xunit;

namespace Modernization.Analyzers.Tests;

[Trait("Category", "ReplayUnit")]
public sealed class UnsupportedEvidenceTests
{
    [Theory]
    [InlineData("valid", true)]
    [InlineData("access-denied", false)]
    [InlineData("dependency", false)]
    [InlineData("runtime", false)]
    [InlineData("timeout", false)]
    [InlineData("wrong-exit", false)]
    [InlineData("mixed-error", false)]
    [InlineData("partial-output", false)]
    public async Task Exact_unsupported_contract_rejects_infrastructure_errors_even_with_nonzero_exit(string variant, bool accepted)
    {
        var contract = new
        {
            Name = "language-no-vb", InputSha256 = new string('A', 64), ExpectedExitCode = 1,
            Format = "exact-streams", DiagnosticCode = "NO_VB", ExpectedStandardOutput = "",
            ExpectedStandardError = "NO_VB: No eligible VB projects.\r\n"
        };
        var stderr = variant switch
        {
            "access-denied" => "UNEXPECTED: System.UnauthorizedAccessException: Access to the path is denied.\r\n",
            "dependency" => "UNEXPECTED: Could not load file or assembly Microsoft.CodeAnalysis.Workspaces.\r\n",
            "runtime" => "You must install or update .NET to run this application.\r\n",
            "mixed-error" => contract.ExpectedStandardError + "UNEXPECTED: Access denied.\r\n",
            _ => contract.ExpectedStandardError
        };
        var result = await Validate(contract, variant == "timeout" ? 124 : variant == "wrong-exit" ? 2 : 1,
            "", stderr, variant == "partial-output");
        Assert.Equal(accepted, result);
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("evaluation-failed", false)]
    [InlineData("additional-error", false)]
    [InlineData("wrong-status", false)]
    [InlineData("duplicate-status", false)]
    [InlineData("wrong-project", false)]
    [InlineData("error-property", false)]
    [InlineData("stderr-error", false)]
    [InlineData("wrong-exit", false)]
    public async Task Structured_unsupported_contract_requires_exact_status_and_only_expected_diagnostic(string variant, bool accepted)
    {
        var diagnostic = new { Severity = "error", Code = "framework-downgrade", Project = "Modern.csproj", Message = "Refusing to downgrade." };
        var contract = new
        {
            Name = "project-downgrade", InputSha256 = new string('A', 64), ExpectedExitCode = 2,
            Format = "json-diagnostics", DiagnosticCode = "framework-downgrade", ExpectedStandardError = "",
            StatusProperty = "Verification", StatusValue = "planned-only", ExpectedDiagnostic = diagnostic,
            AllowedProperties = new[] { "Verification", "Diagnostics" }
        };
        object error = new { Severity = "error", Code = "evaluation-failed", Project = "Modern.csproj", Message = "UnauthorizedAccessException" };
        var selected = variant == "evaluation-failed" ? error : variant == "wrong-project" ?
            new { diagnostic.Severity, diagnostic.Code, Project = "Other.csproj", diagnostic.Message } : (object)diagnostic;
        var json = JsonSerializer.Serialize(new
        {
            Verification = variant == "wrong-status" ? "output-evaluated" : "planned-only",
            Diagnostics = variant == "additional-error" ? new[] { selected, error } : [selected]
        });
        if (variant == "duplicate-status") json = json.Replace("{\"Verification\":", "{\"Verification\":\"failed\",\"Verification\":");
        if (variant == "error-property") json = json.Replace("{\"Verification\":", "{\"error\":\"Access denied\",\"Verification\":");
        Assert.Equal(accepted, await Validate(contract, variant == "wrong-exit" ? 1 : 2, json,
            variant == "stderr-error" ? "Unhandled runtime exception" : "", false));
    }

    [Fact]
    public async Task Missing_case_specific_contract_is_rejected_before_runtime_access_or_guest_launch()
    {
        var output = await ProtectedCompilerTests.Run("""
            function Get-CimInstance { throw 'UNEXPECTED_GUEST_LAUNCH' }
            try {
                & (Join-Path $sandbox 'Invoke-IsolatedToolCase.ps1') -Name owned -Unsupported `
                    -RuntimeManifest 'Z:\missing.json' -InputRoot 'Z:\missing' -CommandArguments @('{input}','{output}')
                throw 'UNEXPECTED_ACCEPTANCE'
            } catch {
                if($_.Exception.Message -notlike 'Unsupported cases require a trusted case-specific*') { throw }
                Write-Output 'rejected-before-launch'
            }
            """);
        Assert.Equal("rejected-before-launch", output.Trim());
    }

    [Theory]
    [InlineData("case")]
    [InlineData("input")]
    [InlineData("zero-exit")]
    [InlineData("timeout-exit")]
    [InlineData("array")]
    public async Task Unsupported_contract_cannot_be_reused_for_another_case_or_input(string mutation)
    {
        var root = Path.Combine(EvaluatorConfiguration.ArtifactRoot, "unsupported-binding-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var result = await ProtectedCompilerTests.Run("$root='" + root.Replace("'", "''") + "'; " + """
                . (Join-Path $sandbox 'UnsupportedEvidence.ps1')
                $contract=@{Name='owned';InputSha256=('A'*64);ExpectedExitCode=1;Format='exact-streams'
                    DiagnosticCode='NO_VB';ExpectedStandardOutput='';ExpectedStandardError="NO_VB: No eligible VB projects.`r`n"}
                switch('__MUTATION__') {
                    'case' { $contract.Name='other' }
                    'input' { $contract.InputSha256='B'*64 }
                    'zero-exit' { $contract.ExpectedExitCode=0 }
                    'timeout-exit' { $contract.ExpectedExitCode=124 }
                }
                $json=$contract | ConvertTo-Json
                if('__MUTATION__' -eq 'array') { $json='['+$json+']' }
                [IO.File]::WriteAllText((Join-Path $root 'contract.json'),$json)
                try {
                    $null=Read-UnsupportedContract (Join-Path $root 'contract.json') 'owned' ('A'*64)
                    throw 'UNEXPECTED_ACCEPTANCE'
                } catch {
                    if($_.Exception.Message -eq 'UNEXPECTED_ACCEPTANCE') { throw }
                    Write-Output 'rejected-binding'
                }
                """.Replace("__MUTATION__", mutation));
            Assert.Equal("rejected-binding", result.Trim());
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task<bool> Validate(object contract, int exit, string stdout, string stderr, bool hasOutput)
    {
        var root = Path.Combine(EvaluatorConfiguration.ArtifactRoot, "unsupported-evidence-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var json = JsonSerializer.Serialize(contract);
            File.WriteAllText(Path.Combine(root, "contract.json"), json);
            File.WriteAllText(Path.Combine(root, "stdout.txt"), stdout);
            File.WriteAllText(Path.Combine(root, "stderr.txt"), stderr);
            using var doc = JsonDocument.Parse(json);
            var name = doc.RootElement.GetProperty("Name").GetString()!;
            var output = await ProtectedCompilerTests.Run("$root='" + root.Replace("'", "''") + "'; " + """
                . (Join-Path $sandbox 'UnsupportedEvidence.ps1')
                $contract=Read-UnsupportedContract (Join-Path $root 'contract.json') '__NAME__' ('A'*64)
                try {
                    Assert-UnsupportedEvidence $contract __EXIT__ ([IO.File]::ReadAllText((Join-Path $root 'stdout.txt'))) `
                        ([IO.File]::ReadAllText((Join-Path $root 'stderr.txt'))) $__OUTPUT__
                    Write-Output 'accepted'
                } catch { Write-Output 'rejected' }
                """.Replace("__NAME__", name).Replace("__EXIT__", exit.ToString()).Replace("__OUTPUT__", hasOutput ? "true" : "false"));
            return output.Trim() == "accepted";
        }
        finally { Directory.Delete(root, true); }
    }
}
