[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputRoot,
    [Parameter(Mandatory)][string]$ExpectedOutputRoot,
    [Parameter(Mandatory)][string[]]$Projects,
    [string]$EvidenceFile,
    [string]$EvidenceStatusProperty='Status',
    [string]$EvidenceSuccessValue='succeeded',
    [string]$PublicPackageRoot,
    [string]$PublicFrameworkRoot,
    [ValidateRange(60,900)][int]$TimeoutSeconds=300
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'ReplayVerification.ps1')
$comparison=Assert-FrozenReplayOutput $OutputRoot $ExpectedOutputRoot $EvidenceFile $EvidenceStatusProperty $EvidenceSuccessValue
$outputHash=Get-ReplayTreeHash (Get-ReplayTree $OutputRoot)
$expectedHash=Get-ReplayTreeHash (Get-ReplayTree $ExpectedOutputRoot)
$found=@($comparison.Files.Keys | Where-Object { [IO.Path]::GetExtension($_) -in @('.csproj','.vbproj') })
if($Projects.Count -eq 0 -or $found.Count -ne $Projects.Count -or
    @($Projects | Select-Object -Unique).Count -ne $Projects.Count -or
    @($found | Where-Object { $_ -cnotin $Projects }).Count -gt 0) {
    throw 'Output compilation must explicitly cover every emitted project exactly once.'
}
if(@($found | Where-Object { [IO.Path]::GetExtension($_) -ine '.csproj' }).Count -gt 0) {
    throw 'Protected output compilation currently supports C# console projects only; VB/WPF/project-dependency graphs require a corresponding trusted compiler pipeline.'
}
$run=Join-Path (Split-Path $PSScriptRoot -Parent) ('Artifacts\output-compilation-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$records=New-Object 'Collections.Generic.List[object]'
$report=[ordered]@{
    Scope='isolated-source-bound-emitted-output-compilation'; FormalVerified=$false; OutputCompilationVerified=$false
    OutputRoot=(Resolve-Path $OutputRoot).Path; OutputSha256=$comparison.OutputSha256
    FullOutputSha256=$outputHash; ExpectedSha256=$expectedHash; Projects=$records
}
try {
    foreach($project in $Projects) {
        $result=& (Join-Path $PSScriptRoot 'Invoke-ProtectedProducer.ps1') -SourceRoot $OutputRoot -Project $project `
            -PublicPackageRoot $PublicPackageRoot -PublicFrameworkRoot $PublicFrameworkRoot -TimeoutSeconds $TimeoutSeconds
        $proof=$result.SourceAndCompilerObservation
        if($proof.ClaimedTargetMatches -ne $true -or $proof.CompilerVerification.SandboxStopped -ne $true -or
            $proof.CompilerVerification.Deterministic -ne $true -or
            (Get-CompilerFileHash $proof.ProtectedAssembly) -cne $proof.ProtectedSha256) {
            throw 'Emitted output did not produce a matching protected compiler artifact.'
        }
        $records.Add(@{ Project=$project; Evidence=$result.Evidence; EvidenceSha256=(Get-CompilerFileHash $result.Evidence)
            Assembly=$proof.ProtectedAssembly; AssemblySha256=$proof.ProtectedSha256 })
        if((Get-ReplayTreeHash (Get-ReplayTree $OutputRoot)) -cne $outputHash -or
            (Get-ReplayTreeHash (Get-ReplayTree $ExpectedOutputRoot)) -cne $expectedHash) {
            throw 'Output or frozen oracle changed during compilation.'
        }
    }
    $report.OutputCompilationVerified=$true
} catch { $report.Error=$_.Exception.Message; throw }
finally { $report | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath (Join-Path $run 'compilation-observation.json') -Encoding UTF8 }
[pscustomobject]@{ Evidence=(Join-Path $run 'compilation-observation.json'); Observation=$report; FormalVerified=$false }
