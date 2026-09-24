using Xunit;

namespace Modernization.Analyzers.Tests;

[Trait("Category", "ReplayUnit")]
public sealed class WorkerEnvironmentTests
{
    [Fact]
    public async Task Worker_environment_is_allowlisted_and_does_not_mutate_controller_environment()
    {
        var result = await ProtectedCompilerTests.Run("""
            . (Join-Path $sandbox 'GuestEnvironment.ps1')
            $before=[Environment]::GetEnvironmentVariable('TEMP','Process')
            $env:OWNED_NOT_FOR_WORKER='private-owned-marker'
            $entries=New-ExplicitWorkerEnvironment 'C:\OwnedWorker' 'C:\Windows' 'C:\Windows\System32\WindowsPowerShell\v1.0' $true $true
            if([Environment]::GetEnvironmentVariable('TEMP','Process') -cne $before -or
                $entries -contains 'OWNED_NOT_FOR_WORKER=private-owned-marker' -or
                $entries -notcontains 'TEMP=C:\OwnedWorker\tmp' -or
                $entries -notcontains 'NUGET_PACKAGES=C:\OwnedWorker\packages' -or
                $entries -notcontains 'ProgramFiles(x86)=C:\Program Files (x86)' -or
                $entries -notcontains 'TargetFrameworkRootPath=C:\PublicFrameworkReferences\') { throw 'Environment custody changed.' }
            Add-Type -Path (Join-Path $sandbox 'RestrictedProcess.cs')
            $block=[RestrictedProcess]::EnvironmentBlock($entries)
            if(-not $block.EndsWith("`0`0")) { throw 'No Unicode environment terminator.' }
            $runs=@([RestrictedProcess].GetMethods() | Where-Object Name -eq 'Run')
            if($runs.Count -ne 1 -or $runs[0].GetParameters().Count -ne 7 -or
                $runs[0].GetParameters()[6].ParameterType -ne [string[]]) { throw 'Implicit environment overload remains.' }
            Write-Output 'explicit-worker-environment'
            """);
        Assert.Equal("explicit-worker-environment", result.Trim());
    }

    [Theory]
    [InlineData("$null")]
    [InlineData("@()")]
    [InlineData("@('PATH=one','path=two')")]
    [InlineData("@('=missing-name')")]
    [InlineData("@(\"PATH=bad`0entry\")")]
    [InlineData("@($null)")]
    [InlineData("@('A='+('x'*32768))")]
    public async Task Native_runner_rejects_implicit_or_ambiguous_environment_blocks(string entries)
    {
        var result = await ProtectedCompilerTests.Run("""
            Add-Type -Path (Join-Path $sandbox 'RestrictedProcess.cs')
            try { $null=[RestrictedProcess]::EnvironmentBlock(__ENTRIES__); throw 'UNEXPECTED_ACCEPTANCE' }
            catch { if($_.Exception.Message -eq 'UNEXPECTED_ACCEPTANCE') { throw }; Write-Output 'rejected' }
            """.Replace("__ENTRIES__", entries));
        Assert.Equal("rejected", result.Trim());
    }

    [Theory]
    [InlineData("-SourceRoot 'Z:\\submission'")]
    [InlineData("-ExpectedOutputRoot 'Z:\\oracle'")]
    [InlineData("-CompilerJobRoot 'Z:\\compiler'")]
    [InlineData("-PublicPackageRoot 'Z:\\package'")]
    [InlineData("-ExecutionRuntime framework472")]
    public async Task Appcontainer_compatibility_cannot_become_a_build_or_acceptance_job(string extra)
    {
        var result = await ProtectedCompilerTests.Run("""
            function Get-CimInstance { throw 'UNEXPECTED_FEATURE_CHECK' }
            try {
                & (Join-Path $sandbox 'Invoke-SandboxProbe.ps1') -AppContainerCompatibilityDiagnostic `
                    -BinaryRoot 'Z:\runtime' -InputRoot 'Z:\input' __EXTRA__
                throw 'UNEXPECTED_ACCEPTANCE'
            } catch {
                if($_.Exception.Message -notlike 'AppContainer compatibility is diagnostic-only*') { throw }
                Write-Output 'diagnostic-only'
            }
            """.Replace("__EXTRA__", extra));
        Assert.Equal("diagnostic-only", result.Trim());
    }

    [Fact]
    public async Task Guest_material_acl_reset_preserves_descendant_inheritance()
    {
        var result = await ProtectedCompilerTests.Run("""
            $source=[IO.File]::ReadAllText((Join-Path $sandbox 'GuestAppContainer.ps1'))
            $rootGrants=@($source -split "`n" | Where-Object { $_ -match 'icacls.+/inheritance:r' })
            if($rootGrants.Count -ne 1 -or $rootGrants[0] -match '\s/T(?:\s|$)' -or
                $source -notmatch [regex]::Escape("icacls.exe (`$entry[0]+'\*') /reset /T")) {
                throw 'Recursive inheritance removal would leave descendant files with empty DACLs.'
            }
            if($source -match 'appcontainer-native-smoke|System32\\cmd.exe') { throw 'Early diagnostic worker precedes material initialization.' }
            Write-Output 'root-grant-then-descendant-reset'
            """);
        Assert.Equal("root-grant-then-descendant-reset", result.Trim());
    }
}
