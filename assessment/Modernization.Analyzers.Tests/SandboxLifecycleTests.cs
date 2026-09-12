using ExternalEvaluation;
using Xunit;

namespace Modernization.Analyzers.Tests;

[Trait("Category", "ReplayUnit")]
public sealed class SandboxLifecycleTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Failure_cleanup_is_configuration_bound_and_requires_server_termination(bool serverStaysAlive, bool wrongConfiguration)
    {
        var root = Path.Combine(EvaluatorConfiguration.ArtifactRoot, "sandbox-lifecycle-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var script = "$root='" + root.Replace("'", "''") + "'; " + """
                . (Join-Path $sandbox 'SandboxLifecycle.ps1')
                $config=Join-Path $root 'owned.wsb'
                $script:ours=[pscustomobject]@{Id=41;HasExited=$false}
                $script:foreign=[pscustomobject]@{Id=42;HasExited=$false}
                $script:server=[pscustomobject]@{Id=51;HasExited=$false}
                $launcher=[pscustomobject]@{Id=1;HasExited=$true}
                $script:stops=New-Object 'Collections.Generic.List[int]'
                $script:serverStaysAlive=__SERVER__
                $script:wrongConfiguration=__CONFIGURATION__
                foreach($process in @($script:ours,$script:foreign,$script:server)) {
                    $process | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value {param($milliseconds) return $false}
                }
                function Get-CimInstance {
                    [CmdletBinding()]param([Parameter(Position=0)]$ClassName,$Filter)
                    $ownConfig=$(if($script:wrongConfiguration) { 'C:\different.wsb' } else { $config })
                    [pscustomobject]@{ProcessId=41;CommandLine=('"client.exe" "'+$ownConfig+'"')}
                    [pscustomobject]@{ProcessId=42;CommandLine='"client.exe" "C:\someone-elses.wsb"'}
                }
                function Get-Process {
                    [CmdletBinding()]param([int]$Id)
                    if($Id -eq 41) { return $script:ours }
                    if($Id -eq 42) { return $script:foreign }
                    throw 'Unexpected process lookup'
                }
                function Stop-Process {
                    [CmdletBinding()]param([int]$Id)
                    $script:stops.Add($Id)
                    if($Id -ne 41) { throw 'Attempted to stop a non-owned process' }
                    $script:ours.HasExited=$true
                    if(-not $script:serverStaysAlive) { $script:server.HasExited=$true }
                }
                $path=Join-Path $root 'cleanup.json'
                $failed=$false
                try {
                    Close-OwnedSandboxSession -Configuration $config -Launcher $launcher -RemoteSessions @($script:ours) `
                        -Servers @($script:server) -EvidencePath $path -OriginalFailure 'Owned post-bootstrap timeout' -TimeoutSeconds 1
                } catch { $failed=$true }
                $report=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
                $expectedFailure=$script:serverStaysAlive -or $script:wrongConfiguration
                if($failed -ne $expectedFailure -or $report.SandboxStopped -eq $expectedFailure -or
                    $script:foreign.HasExited -or $script:stops.Contains(51) -or $report.FormalVerified) {
                    throw ('Incorrect lifecycle cleanup result: '+($report | ConvertTo-Json -Compress)+'; stops='+($script:stops -join ','))
                }
                if($script:wrongConfiguration) {
                    if($script:stops.Count -ne 0) { throw 'Configuration mismatch was ignored' }
                } elseif($script:stops.Count -ne 1 -or $script:stops[0] -ne 41) { throw ('Owned client was not closed exactly once: '+($report | ConvertTo-Json -Compress)) }
                Write-Output 'verified-cleanup-contract'
                """
                .Replace("__SERVER__", serverStaysAlive ? "$true" : "$false")
                .Replace("__CONFIGURATION__", wrongConfiguration ? "$true" : "$false");
            Assert.Equal("verified-cleanup-contract", (await ProtectedCompilerTests.Run(script)).Trim());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Every_launched_failure_reaches_finally_and_success_is_not_set_inside_the_wait_loop()
    {
        var output = await ProtectedCompilerTests.Run("""
            $tokens=$null; $errors=$null
            $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $sandbox 'Invoke-SandboxProbe.ps1'),[ref]$tokens,[ref]$errors)
            if($errors) { throw 'Host script does not parse' }
            $cleanup=@($ast.FindAll({param($node)
                $node -is [Management.Automation.Language.TryStatementAst] -and $node.Finally -and
                    $node.Finally.Extent.Text.Contains('Close-OwnedSandboxSession')
            },$true))
            if($cleanup.Count -ne 1 -or -not $cleanup[0].Body.Extent.Text.Contains('Start-Process')) {
                throw 'Owned launch is not enclosed by failure cleanup'
            }
            $assignments=@($ast.FindAll({param($node)
                $node -is [Management.Automation.Language.AssignmentStatementAst] -and
                    $node.Left.Extent.Text -eq '$sandboxStopped' -and $node.Right.Extent.Text -eq '$true'
            },$true))
            if($assignments.Count -ne 1) { throw 'Unexpected lifecycle success assignments' }
            for($parent=$assignments[0].Parent; $parent; $parent=$parent.Parent) {
                if($parent -is [Management.Automation.Language.ForEachStatementAst]) { throw 'Success set before all processes were observed stopped' }
            }
            Write-Output 'verified-finally'
            """);
        Assert.Equal("verified-finally", output.Trim());
    }
}
