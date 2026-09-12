[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceRoot,
    [Parameter(Mandatory)][string]$Project,
    [string]$SdkRoot=(Join-Path $env:ProgramFiles 'dotnet'),
    [string]$PublicPackageRoot,
    [string]$PublicFrameworkRoot,
    [ValidateRange(60,900)][int]$TimeoutSeconds=240
)
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -lt 7) { throw 'Protected producer requires PowerShell 7.' }
. (Join-Path $PSScriptRoot 'CompilerPlan.ps1')
$runner=Join-Path $PSScriptRoot 'Invoke-SandboxProbe.ps1'
$build=& $runner -SourceRoot $SourceRoot -Projects @($Project) -SdkRoot $SdkRoot -PublicPackageRoot $PublicPackageRoot -PublicFrameworkRoot $PublicFrameworkRoot -TimeoutSeconds $TimeoutSeconds
$build | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $build.Artifacts 'host-build-observation.json') -Encoding UTF8
# Invoke-SandboxProbe returns only after the submitted VM has stopped.
$capture=Join-Path $build.Artifacts 'protected-compiler-input'
$plan=New-CompilerPlan $build $Project $SdkRoot $PublicPackageRoot $capture $PublicFrameworkRoot
Start-Sleep -Seconds 10
try { $compiled=& $runner -CompilerJobRoot $capture -SdkRoot $SdkRoot -TimeoutSeconds $TimeoutSeconds }
catch {
    if($_.Exception.Message -notlike 'Protected controller did not bootstrap;*') { throw }
    Start-Sleep -Seconds 10
    $compiled=& $runner -CompilerJobRoot $capture -SdkRoot $SdkRoot -TimeoutSeconds $TimeoutSeconds
}
$outArgument=@($plan.Arguments | Where-Object { $_.StartsWith('/out:',[StringComparison]::OrdinalIgnoreCase) })[0]
$relative=$outArgument.Substring('/out:C:\ProbeWork\restricted\producer\'.Length)
$protected=Join-Path $compiled.HostCompilerVerification.Export ('initial\'+$relative)
$record=@($build.Producer.Projects | Where-Object Project -CEQ $Project)[0]
$metadata=$record.StandardOutput | ConvertFrom-Json
$target=$metadata.Properties.TargetPath
$guestRoot='C:\ProbeWork\restricted\producer\'
if(-not $target.StartsWith($guestRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Claimed target escapes source workspace.' }
$claimed=Join-Path $build.HostProducerObservation.Export $target.Substring($guestRoot.Length)
$matched=(Get-CompilerFileHash $claimed) -ceq (Get-CompilerFileHash $protected)
$evidence=[ordered]@{
    Protocol='protected-producer-observation-v1'; BuildArtifacts=$build.Artifacts; CompilerArtifacts=$compiled.Artifacts
    SourceFiles=$plan.AuthoredSourceFiles; CapturedCompilerInputs=$plan.Inputs
    CompilerVerification=$compiled.HostCompilerVerification
    ProtectedAssembly=$protected; ProtectedSha256=(Get-CompilerFileHash $protected)
    ClaimedAssembly=$claimed; ClaimedSha256=(Get-CompilerFileHash $claimed); ClaimedTargetMatches=$matched
    FormalVerified=$false; SignedReceipt=$null
}
$path=Join-Path $compiled.Artifacts 'protected-producer.json'
$evidence | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $path -Encoding UTF8
if(-not $matched) { throw "Submitted build target differs from independent trusted csc output. No producer accepted. Evidence: $path" }
[pscustomobject]@{ Evidence=$path; ProtectedAssembly=$protected; SourceAndCompilerObservation=$evidence; FormalVerified=$false }
