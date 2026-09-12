[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BinaryRoot,
    [Parameter(Mandatory)][string]$InputRoot,
    [Parameter(Mandatory)][string]$BehaviorFile,
    [ValidateRange(60,900)][int]$TimeoutSeconds=240
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'ReplayVerification.ps1')
$behaviorPath=(Resolve-Path -LiteralPath $BehaviorFile).Path
foreach($exposedRoot in @($BinaryRoot,$InputRoot,(Join-Path $env:ProgramFiles 'dotnet'))) {
    $exposed=(Resolve-Path -LiteralPath $exposedRoot).Path.TrimEnd('\')
    if($behaviorPath.StartsWith($exposed+'\',[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Behavior expectations must not be inside a staged or mapped root.'
    }
}
$specificationHash=Get-CompilerFileHash $BehaviorFile
$specification=Get-Content -LiteralPath $BehaviorFile -Raw | ConvertFrom-Json
if($specification.Runtime -cnotin @('net10','framework472') -or
    $specification.EntryAssembly -isnot [string] -or
    @($specification.Arguments).Count -eq 0 -or
    @($specification.Arguments | Where-Object { $_ -isnot [string] }).Count -gt 0) {
    throw 'Unsupported isolated behavior contract.'
}
# Only arguments and the entry identity enter the VM. Expected observations stay on host.
Assert-ReplayBehavior $specification $specification.ExpectedExitCode $specification.ExpectedStandardOutput $specification.ExpectedStandardError
$binaryFiles=Get-ReplayTree $BinaryRoot
$binaryHash=Get-ReplayTreeHash $binaryFiles
$inputHash=Get-ReplayTreeHash (Get-ReplayTree $InputRoot)
$result=& (Join-Path $PSScriptRoot 'Invoke-SandboxProbe.ps1') -BinaryRoot $BinaryRoot -InputRoot $InputRoot `
    -EntryAssembly $specification.EntryAssembly -ExecutionRuntime $specification.Runtime `
    -CommandArguments @($specification.Arguments) -TimeoutSeconds $TimeoutSeconds
if($result.SandboxStopped -ne $true) { throw 'Behavior VM termination was not observed.' }
$execution=$result.HostExecutionVerification
$stdoutFile=Join-Path $execution.Export 'cli.stdout'; $stderrFile=Join-Path $execution.Export 'cli.stderr'
$stdout=[IO.File]::ReadAllText($stdoutFile); $stderr=[IO.File]::ReadAllText($stderrFile)
Assert-ReplayBehavior $specification $execution.ExitCode $stdout $stderr
if((Get-ReplayTreeHash (Get-ReplayTree $BinaryRoot)) -cne $binaryHash -or
    (Get-ReplayTreeHash (Get-ReplayTree $InputRoot)) -cne $inputHash -or
    (Get-CompilerFileHash $BehaviorFile) -cne $specificationHash) {
    throw 'Behavior binary, input or host contract changed during execution.'
}
$report=[ordered]@{
    Scope='isolated-predeclared-observable-behavior-only'; BehaviorVerified=$true; FormalVerified=$false
    BinarySha256=$binaryHash; InputSha256=$inputHash; BehaviorSha256=$specificationHash
    BinaryFiles=$binaryFiles; Runtime=$specification.Runtime; FrameworkClr=$result.FrameworkClr
    ExitCode=$execution.ExitCode; StandardOutput=$stdout; StandardError=$stderr
    StandardOutputSha256=(Get-CompilerFileHash $stdoutFile); StandardErrorSha256=(Get-CompilerFileHash $stderrFile)
    SandboxStopped=$true; SandboxProcessIds=$result.SandboxProcessIds; Artifacts=$result.Artifacts
    Note='Source/compiler provenance must separately bind this exact binary tree before formal import.'
}
$path=Join-Path $result.Artifacts 'behavior-observation.json'
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $path -Encoding UTF8
[pscustomobject]@{ Evidence=$path; Observation=$report; FormalVerified=$false }
