using Xunit;

namespace Modernization.Analyzers.Tests;

[Trait("Category", "ReplayUnit")]
public sealed class OwnedProfileTests
{
    [Theory]
    [InlineData("-SourceRoot 'Z:\\candidate'")]
    [InlineData("-BinaryRoot 'Z:\\candidate'")]
    [InlineData("-CompilerJobRoot 'Z:\\candidate'")]
    [InlineData("-InputRoot 'Z:\\fixture'")]
    [InlineData("-PublicPackageRoot 'Z:\\packages'")]
    [InlineData("-ExpectedOutputRoot 'Z:\\oracle'")]
    [InlineData("-OwnedControllerDelaySeconds 60")]
    public async Task Diagnostic_medium_cannot_be_selected_for_submitted_or_replay_material(string arguments)
    {
        var result = await ProtectedCompilerTests.Run("""
            function Get-CimInstance { throw 'UNEXPECTED_FEATURE_CHECK' }
            try {
                & (Join-Path $sandbox 'Invoke-SandboxProbe.ps1') -OwnedProfileDiagnostic medium __ARGUMENTS__
                throw 'UNEXPECTED_ACCEPTANCE'
            } catch {
                if($_.Exception.Message -notlike 'Owned profile diagnostics cannot be combined*') { throw }
                Write-Output 'rejected-before-preparation'
            }
            """.Replace("__ARGUMENTS__", arguments));
        Assert.Equal("rejected-before-preparation", result.Trim());
    }

    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    public async Task Prepared_owned_profile_has_no_submission_and_preserves_default_low_implementation(string profile)
    {
        var result = await ProtectedCompilerTests.Run("""
            $prepared=& (Join-Path $sandbox 'Invoke-SandboxProbe.ps1') -PrepareOnly -OwnedProfileDiagnostic __PROFILE__
            try {
                $payload=Join-Path $prepared.Artifacts 'payload'
                $original=Get-Content (Join-Path $sandbox 'RestrictedProcess.cs') -Raw
                if($original -cne (Get-Content (Join-Path $payload 'RestrictedProcess.cs') -Raw) -or
                    (Test-Path (Join-Path $payload 'job.json')) -or $prepared.Executed -or $prepared.FormalVerified) {
                    throw 'Default low implementation or diagnostic scope changed.'
                }
                $spec=Get-Content (Join-Path $payload 'owned-profile.json') -Raw | ConvertFrom-Json
                if($spec.Profile -cne '__PROFILE__' -or $spec.FormalVerified) { throw 'Unexpected profile.' }
                $clone=Join-Path $payload 'owned-profile\OwnedMediumRestrictedProcess.cs'
                if('__PROFILE__' -eq 'medium') {
                    $medium=Get-Content $clone -Raw
                    $restored=$medium.Replace('public static class OwnedMediumRestrictedProcess','public static class RestrictedProcess').
                        Replace('S-1-16-8192','S-1-16-4096').Replace('"Set diagnostic medium integrity"','"Set low integrity"')
                    if($restored -cne $original) { throw 'Medium diagnostic changed more than class identity, integrity and diagnostic label.' }
                } elseif(Test-Path $clone) { throw 'Low diagnostic unexpectedly contains medium execution code.' }
                Write-Output 'owned-only-default-unchanged'
            } finally { Remove-Item -LiteralPath $prepared.Artifacts -Recurse -Force }
            """.Replace("__PROFILE__", profile));
        Assert.Equal("owned-only-default-unchanged", result.Trim());
    }
}
