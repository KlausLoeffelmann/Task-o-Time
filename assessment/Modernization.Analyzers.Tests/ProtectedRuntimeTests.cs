using ExternalEvaluation;
using Xunit;

namespace Modernization.Analyzers.Tests;

[Trait("Category", "ReplayUnit")]
public sealed class ProtectedRuntimeTests
{
    [Fact]
    public async Task Loose_runtime_dll_must_match_the_hash_bound_archive_not_just_the_cache_name()
    {
        var root = Path.Combine(EvaluatorConfiguration.ArtifactRoot, "protected-runtime-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var script = "$root='" + root.Replace("'", "''") + "'; " + """
                # Synthetic inputs exercise rejection only; no binary is executed or accepted.
                $build=Join-Path $root 'build'
                $packageRoot=Join-Path $root 'packages'
                $package=Join-Path $packageRoot 'owned.package\1.0.0'
                $lib=Join-Path $package 'lib\net10.0'
                New-Item -ItemType Directory -Path $build,$lib -Force | Out-Null
                $owned=Join-Path $build 'Owned.dll'
                [IO.File]::WriteAllText($owned,'non-executable owned unit fixture')
                $archivePath=Join-Path $package 'owned.package.1.0.0.nupkg'
                $archive=[IO.Compression.ZipFile]::Open($archivePath,[IO.Compression.ZipArchiveMode]::Create)
                try {
                    $entry=$archive.CreateEntry('lib/net10.0/Dependency.dll')
                    $writer=[IO.StreamWriter]::new($entry.Open())
                    try { $writer.Write('original archive bytes') } finally { $writer.Dispose() }
                } finally { $archive.Dispose() }
                [IO.File]::WriteAllText((Join-Path $lib 'Dependency.dll'),'changed loose bytes')
                $bindings=Join-Path $root 'bindings.json'
                @{Packages=@{'owned.package/1.0.0'=@{ArchiveSha256=(Get-CompilerFileHash $archivePath);NuGetContentHash='unit-hash'}}} |
                    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $bindings
                $evidence=Join-Path $root 'unit-evidence.json'
                @{
                    Purpose='rejection-unit-test-only';Protocol='protected-producer-observation-v1'
                    ClaimedTargetMatches=$true;ProtectedAssembly=$owned;ProtectedSha256=(Get-CompilerFileHash $owned)
                    ClaimedAssembly=$owned;CompilerVerification=@{SandboxStopped=$true;Deterministic=$true}
                } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $evidence
                @{
                    runtimeTarget=@{name='.NETCoreApp,Version=v10.0'}
                    targets=@{'.NETCoreApp,Version=v10.0'=@{}}
                    libraries=@{
                        'Owned/1.0.0'=@{type='project'}
                        'owned.package/1.0.0'=@{type='package';sha512='sha512-unit-hash';path='owned.package/1.0.0'}
                    }
                } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $build 'Owned.deps.json')
                try {
                    & (Join-Path $sandbox 'Publish-ProtectedRuntime.ps1') -ProducerEvidence $evidence `
                        -Destination (Join-Path $root 'published') -PublicPackageRoot $packageRoot -PackageBindings $bindings
                    throw 'UNEXPECTED_ACCEPTANCE'
                } catch {
                    if($_.Exception.Message -ne 'Curated runtime DLL differs from the approved archive.') { throw }
                    if(Test-Path (Join-Path $root 'published')) { throw 'Unexpected partial runtime publication' }
                    Write-Output 'rejected-changed-cache'
                }
                """;
            Assert.Equal("rejected-changed-cache", (await ProtectedCompilerTests.Run(script)).Trim());
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("..", "1.0.0")]
    [InlineData("package", "..")]
    public async Task Curated_package_identity_cannot_escape_its_cache_root(string id, string version)
    {
        var root = Path.Combine(EvaluatorConfiguration.ArtifactRoot, "protected-runtime-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var script = "$root='" + root.Replace("'", "''") + "'; " + """
                $lock=Join-Path $root 'packages.lock.json'
                @{version=1;dependencies=@{'net10.0'=@{'__ID__'=@{resolved='__VERSION__';contentHash='not-used'}}}} |
                    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $lock
                try {
                    & (Join-Path $sandbox 'Copy-LockedPublicPackages.ps1') -LockFile $lock `
                        -Destination (Join-Path $root 'output') -PackageCache (Join-Path $root 'cache')
                    throw 'UNEXPECTED_ACCEPTANCE'
                } catch {
                    if($_.Exception.Message -ne 'Invalid locked package identity.') { throw }
                    Write-Output 'rejected-package-identity'
                }
                """.Replace("__ID__", id).Replace("__VERSION__", version);
            Assert.Equal("rejected-package-identity", (await ProtectedCompilerTests.Run(script)).Trim());
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Runtime_mutation_fails_before_any_guest_launch(bool extraFile)
    {
        var root = Path.Combine(EvaluatorConfiguration.ArtifactRoot, "protected-runtime-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var script = "$root='" + root.Replace("'", "''") + "'; " + """
                $binary=Join-Path $root 'binary'
                $input=Join-Path $root 'input'
                $expected=Join-Path $root 'expected'
                New-Item -ItemType Directory -Path $binary,$input,$expected | Out-Null
                [IO.File]::WriteAllText((Join-Path $binary 'Owned.dll'),'owned non-executable unit fixture')
                $manifest=Join-Path $root 'runtime.json'
                @{BinaryRoot=$binary;EntryAssembly='Owned.dll';Files=@{'Owned.dll'=@{Sha256=(Get-CompilerFileHash (Join-Path $binary 'Owned.dll'))}}} |
                    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifest
                __MUTATION__
                function Get-CimInstance { throw 'Unexpected guest feature inspection' }
                try {
                    & (Join-Path $sandbox 'Invoke-IsolatedToolCase.ps1') -Name 'unit' -RuntimeManifest $manifest `
                        -InputRoot $input -ExpectedOutputRoot $expected -CommandArguments '{input}','{output}'
                    throw 'UNEXPECTED_ACCEPTANCE'
                } catch {
                    if($_.Exception.Message -notlike 'Protected runtime * changed.') { throw }
                    Write-Output 'rejected-runtime-mutation'
                }
                """.Replace("__MUTATION__", extraFile
                    ? "[IO.File]::WriteAllText((Join-Path $binary 'Extra.dll'),'unexpected')"
                    : "Add-Content -LiteralPath (Join-Path $binary 'Owned.dll') 'changed'");
            Assert.Equal("rejected-runtime-mutation", (await ProtectedCompilerTests.Run(script)).Trim());
        }
        finally { Directory.Delete(root, true); }
    }
}
