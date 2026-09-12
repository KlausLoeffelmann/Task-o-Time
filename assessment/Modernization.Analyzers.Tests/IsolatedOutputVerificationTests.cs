using System.Text;
using ExternalEvaluation;
using Xunit;

namespace Modernization.Analyzers.Tests;

[Trait("Category", "ReplayUnit")]
public sealed class IsolatedOutputVerificationTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("output")]
    [InlineData("extra")]
    [InlineData("status")]
    [InlineData("input")]
    [InlineData("baseline")]
    [InlineData("self-oracle")]
    public async Task Checkpoints_require_full_frozen_output_and_exact_input_revision_bindings(string mutation)
    {
        var root = Path.Combine(EvaluatorConfiguration.ArtifactRoot, "checkpoint-contract-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var result = await ProtectedCompilerTests.Run("$root='" + root.Replace("'", "''") + "'; " + """
                . (Join-Path $sandbox 'ReplayVerification.ps1')
                $input=Join-Path $root 'input'; $expected=Join-Path $root 'expected'; $actual=Join-Path $root 'actual'
                New-Item -ItemType Directory -Path $input,$expected,$actual | Out-Null
                [IO.File]::WriteAllText((Join-Path $input 'source.cs'),'before')
                [IO.File]::WriteAllText((Join-Path $expected 'source.cs'),'after')
                [IO.File]::WriteAllText((Join-Path $actual 'source.cs'),'after')
                [IO.File]::WriteAllText((Join-Path $actual 'run.json'),'{"Status":"succeeded","OutputFiles":[]}')
                $binding=@{InputRevision=('a'*40);ExpectedRevision=('b'*40)
                    InputSha256=(Get-ReplayTreeHash (Get-ReplayTree $input))
                    ExpectedSha256=(Get-ReplayTreeHash (Get-ReplayTree $expected))}
                switch('__MUTATION__') {
                    'output' { Add-Content (Join-Path $actual 'source.cs') 'changed' }
                    'extra' { Set-Content (Join-Path $actual 'unlisted.txt') 'extra' }
                    'status' { Set-Content (Join-Path $actual 'run.json') '{"Status":"failed"}' }
                    'input' { Add-Content (Join-Path $input 'source.cs') 'changed' }
                    'baseline' { $binding.ExpectedSha256='0'*64 }
                    'self-oracle' { $expected=$actual }
                }
                try {
                    $comparison=Assert-FrozenReplayOutput $actual $expected 'run.json'
                    $proof=Assert-ReplayCheckpoint $binding $input $expected $comparison
                    if(-not $proof.BaselineCheckpointVerified -or $proof.FormalVerified) { throw 'Bad scope' }
                    Write-Output ('accepted|'+$comparison.OutputSha256)
                } catch { Write-Output 'rejected' }
                """.Replace("__MUTATION__", mutation));
            if (mutation == "none")
                Assert.Equal("accepted|" + ToolReplay.HashTree(Path.Combine(root, "expected")), result.Trim());
            else Assert.Equal("rejected", result.Trim());
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(0, "answer", "", true)]
    [InlineData(1, "answer", "", false)]
    [InlineData(0, "Answer", "", false)]
    [InlineData(0, "answer", "unexpected", false)]
    public async Task Behavior_requires_exact_predeclared_observations(int exit, string stdout, string stderr, bool accepted)
    {
        var result = await ProtectedCompilerTests.Run("""
            . (Join-Path $sandbox 'ReplayVerification.ps1')
            $spec=[pscustomobject]@{ExpectedExitCode=0;ExpectedStandardOutput='answer';ExpectedStandardError=''}
            try { Assert-ReplayBehavior $spec __EXIT__ '__STDOUT__' '__STDERR__'; Write-Output 'accepted' }
            catch { Write-Output 'rejected' }
            """.Replace("__EXIT__", exit.ToString()).Replace("__STDOUT__", stdout).Replace("__STDERR__", stderr));
        Assert.Equal(accepted ? "accepted" : "rejected", result.Trim());
    }

    [Fact]
    public async Task Behavior_oracle_inside_candidate_binary_tree_is_rejected_before_launch()
    {
        var root = Path.Combine(EvaluatorConfiguration.ArtifactRoot, "behavior-contract-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "behavior.json"), "{}", Encoding.UTF8);
            var result = await ProtectedCompilerTests.Run("$root='" + root.Replace("'", "''") + "'; " + """
                function Get-CimInstance { throw 'UNEXPECTED_LAUNCH' }
                try {
                    & (Join-Path $sandbox 'Invoke-IsolatedBehavior.ps1') -BinaryRoot $root -InputRoot $root -BehaviorFile (Join-Path $root 'behavior.json')
                    throw 'UNEXPECTED_ACCEPTANCE'
                } catch {
                    if($_.Exception.Message -notlike 'Behavior expectations must not*') { throw }
                    Write-Output 'rejected-private-oracle'
                }
                """);
            Assert.Equal("rejected-private-oracle", result.Trim());
        }
        finally { Directory.Delete(root, true); }
    }
}
