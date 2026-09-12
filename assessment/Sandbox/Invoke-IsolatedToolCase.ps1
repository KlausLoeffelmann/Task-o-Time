[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Name,
    [Parameter(Mandatory)][string]$RuntimeManifest,
    [Parameter(Mandatory)][string]$InputRoot,
    [Parameter(Mandatory)][string[]]$CommandArguments,
    [string]$ExpectedOutputRoot,
    [string]$EvidenceFile,
    [string]$EvidenceStatusProperty='Status',
    [string]$EvidenceSuccessValue='succeeded',
    [string]$PublicPackageRoot,
    [string]$PublicFrameworkRoot,
    [string]$CheckpointBindingFile,
    [string[]]$OutputProjects,
    [switch]$Unsupported,
    [switch]$Idempotent,
    [ValidateRange(60,600)][int]$CliTimeoutSeconds=120,
    [ValidateRange(60,900)][int]$TimeoutSeconds=240
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'CompilerPlan.ps1')
. (Join-Path $PSScriptRoot 'ReplayVerification.ps1')
if($Name -notmatch '^[A-Za-z0-9_.-]+$' -or ($Unsupported -and ($ExpectedOutputRoot -or $Idempotent -or $EvidenceFile)) -or
    (-not $Unsupported -and -not $ExpectedOutputRoot)) { throw 'Invalid isolated case contract.' }
if($Unsupported -and ($CheckpointBindingFile -or $OutputProjects)) { throw 'Unsupported cases cannot claim compilation or checkpoint success.' }
$checkpointHash=$(if($CheckpointBindingFile) { Get-CompilerFileHash $CheckpointBindingFile } else { $null })
$checkpoint=$(if($CheckpointBindingFile) { Get-Content -LiteralPath $CheckpointBindingFile -Raw | ConvertFrom-Json } else { $null })
function Snapshot([string]$root) {
    $root=(Resolve-Path -LiteralPath $root).Path.TrimEnd('\')
    if((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Case root reparse point rejected.' }
    $files=New-Object 'Collections.Generic.SortedDictionary[string,string]' ([StringComparer]::Ordinal)
    $bytes=[long]0
    foreach($file in Get-ChildItem -LiteralPath $root -Recurse -Force) {
        if($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Case reparse point rejected.' }
        if($file.PSIsContainer) { continue }
        if($files.Count -ge 100000) { throw 'Case snapshot file limit exceeded.' }
        $bytes+=$file.Length
        if($bytes -gt 1073741824) { throw 'Case snapshot byte limit exceeded.' }
        $files[$file.FullName.Substring($root.Length).TrimStart('\')]=Get-CompilerFileHash $file.FullName
    }
    return ,$files
}
function SnapshotHash($files) {
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($files | ConvertTo-Json -Compress))))
}
$runtime=Get-Content -LiteralPath $RuntimeManifest -Raw | ConvertFrom-Json
$binary=Snapshot $runtime.BinaryRoot
if($binary.Count -ne @($runtime.Files.PSObject.Properties).Count) { throw 'Protected runtime file set changed.' }
foreach($file in $binary.Keys) {
    if($runtime.Files.PSObject.Properties[$file].Value.Sha256 -cne $binary[$file]) { throw 'Protected runtime hash changed.' }
}
$inputHash=SnapshotHash (Snapshot $InputRoot)
$binaryHash=SnapshotHash $binary
$expectedHash=$(if($ExpectedOutputRoot) { SnapshotHash (Snapshot $ExpectedOutputRoot) } else { $null })
$runRoot=Join-Path (Split-Path $PSScriptRoot -Parent) ('Artifacts\isolated-case-'+$Name+'-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot | Out-Null
$runs=New-Object 'Collections.Generic.List[object]'
$parameters=@{
    BinaryRoot=$runtime.BinaryRoot; EntryAssembly=$runtime.EntryAssembly; CommandArguments=$CommandArguments
    ExpectedOutputRoot=$ExpectedOutputRoot; EvidenceFile=$EvidenceFile; EvidenceStatusProperty=$EvidenceStatusProperty
    EvidenceSuccessValue=$EvidenceSuccessValue; PublicPackageRoot=$PublicPackageRoot; PublicFrameworkRoot=$PublicFrameworkRoot
    TimeoutSeconds=$TimeoutSeconds; CliTimeoutSeconds=$CliTimeoutSeconds
}
$report=[ordered]@{
    Protocol='isolated-toolcase-observation-v2'
    Name=$Name; Scope='isolated-cli-output-checks-only'; FormalVerified=$false; OutputChecksPassed=$false
    OutputCompilationVerified=$false; BehaviorVerified=$false; BaselineCheckpointVerified=$false
    InputSha256=$inputHash; RuntimeSha256=$binaryHash; ExpectedSha256=$expectedHash
    RuntimeManifestSha256=(Get-CompilerFileHash $RuntimeManifest); Runs=$runs
    CheckpointBindingSha256=$checkpointHash
    CliTimeoutSeconds=$CliTimeoutSeconds
    InputRoot=(Resolve-Path $InputRoot).Path; RuntimeRoot=$runtime.BinaryRoot
    ExpectedRoot=$(if($ExpectedOutputRoot) { (Resolve-Path $ExpectedOutputRoot).Path } else { $null })
    Command=@{ EntryAssembly=$runtime.EntryAssembly; Arguments=$CommandArguments }
    EvidenceContract=@{ FileName=$EvidenceFile; StatusProperty=$EvidenceStatusProperty; SuccessValue=$EvidenceSuccessValue }
}
try {
    $initialOutput=$null; $initialHash=$null
    $passes=$(if($Unsupported) { @('unsupported') } elseif($Idempotent) { @('initial','repeat','idempotent') } else { @('initial','repeat') })
    foreach($pass in $passes) {
        $input=$(if($pass -eq 'idempotent') { $initialOutput } else { $InputRoot })
        $passInputHash=SnapshotHash (Snapshot $input)
        $result=& (Join-Path $PSScriptRoot 'Invoke-SandboxProbe.ps1') @parameters -InputRoot $input
        if($result.SandboxStopped -ne $true) { throw 'CLI VM termination was not observed.' }
        $execution=$result.HostExecutionVerification
        $actual=Join-Path $execution.Export 'output'
        $actualFiles=$(if(Test-Path -LiteralPath $actual -PathType Container) { Snapshot $actual } else { $null })
        if($Unsupported) {
            $text=Get-Content (Join-Path $execution.Export 'cli.stdout'),(Join-Path $execution.Export 'cli.stderr') -Raw
            if($execution.ExitCode -eq 0 -or [string]::IsNullOrWhiteSpace(($text -join '')) -or
                ($actualFiles -and $actualFiles.Count -gt 0) -or (Test-Path -LiteralPath $actual -PathType Leaf)) {
                throw 'Unsupported input did not fail diagnostically without partial output.'
            }
        }
        else {
            if($execution.HostExpectedBytesMatched -ne $true) { throw 'Host frozen expectation comparison did not pass.' }
            if($EvidenceFile) { [void]$actualFiles.Remove($EvidenceFile) }
            $actualHash=SnapshotHash $actualFiles
            if($pass -eq 'initial') { $initialOutput=$actual; $initialHash=$actualHash }
            elseif($actualHash -cne $initialHash) { throw 'Determinism or idempotence failed.' }
            if($checkpoint) {
                $comparison=Assert-FrozenReplayOutput $actual $ExpectedOutputRoot $EvidenceFile $EvidenceStatusProperty $EvidenceSuccessValue
                $report.Checkpoint=Assert-ReplayCheckpoint $checkpoint $InputRoot $ExpectedOutputRoot $comparison
                if((Get-CompilerFileHash $CheckpointBindingFile) -cne $checkpointHash) { throw 'Checkpoint declaration changed during execution.' }
            }
        }
        if((SnapshotHash (Snapshot $input)) -cne $passInputHash -or
            (SnapshotHash (Snapshot $InputRoot)) -cne $inputHash -or
            (SnapshotHash (Snapshot $runtime.BinaryRoot)) -cne $binaryHash -or
            (Get-CompilerFileHash $RuntimeManifest) -cne $report.RuntimeManifestSha256 -or
            ($ExpectedOutputRoot -and (SnapshotHash (Snapshot $ExpectedOutputRoot)) -cne $expectedHash)) {
            throw 'Host fixture, oracle or producer artifact changed during execution.'
        }
        $runs.Add(@{ Run=$pass; Artifacts=$result.Artifacts; Execution=$execution; SandboxProcessIds=$result.SandboxProcessIds; SandboxStopped=$true })
        Start-Sleep -Seconds 10
    }
    $report.OutputChecksPassed=$true
    if($checkpoint) { $report.BaselineCheckpointVerified=$true }
    if($OutputProjects) {
        $compilation=& (Join-Path $PSScriptRoot 'Invoke-IsolatedOutputCompilation.ps1') -OutputRoot $initialOutput `
            -ExpectedOutputRoot $ExpectedOutputRoot -Projects $OutputProjects -EvidenceFile $EvidenceFile `
            -EvidenceStatusProperty $EvidenceStatusProperty -EvidenceSuccessValue $EvidenceSuccessValue `
            -PublicPackageRoot $PublicPackageRoot -PublicFrameworkRoot $PublicFrameworkRoot -TimeoutSeconds $TimeoutSeconds
        $report.OutputCompilation=$compilation
        $report.OutputCompilationVerified=($compilation.Observation.OutputCompilationVerified -eq $true)
    }
    if($checkpoint -or $OutputProjects) { $report.Scope='isolated-cli-with-explicit-verification'; }
} catch { $report.Error=$_.Exception.Message; throw }
finally { $report | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $runRoot 'case-observation.json') -Encoding UTF8 }
[pscustomobject]@{ Evidence=(Join-Path $runRoot 'case-observation.json'); OutputChecksPassed=$true; FormalVerified=$false }
