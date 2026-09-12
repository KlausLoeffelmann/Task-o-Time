[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$project = Join-Path $PSScriptRoot 'FixtureRunner\FixtureRunner.csproj'
$run = Join-Path $PSScriptRoot ('artifacts\api-selection-' + [Guid]::NewGuid().ToString('N'))
$results = @()
foreach ($case in @(
    @{ name = 'legacy'; files = @('AdminMasterDataService.cs'); select = ''; expected = 'MasterData'; succeeds = $true },
    @{ name = 'renamed'; files = @('AdminMainDataService.cs'); select = ''; expected = 'MainData'; succeeds = $true },
    @{ name = 'ambiguous'; files = @('AdminMasterDataService.cs', 'AdminMainDataService.cs'); select = ''; expected = 'Missing or ambiguous'; succeeds = $false },
    @{ name = 'explicit-ambiguous'; files = @('AdminMasterDataService.cs', 'AdminMainDataService.cs'); select = 'MainData'; expected = 'MainData'; succeeds = $true },
    @{ name = 'missing'; files = @(); select = ''; expected = 'Missing or ambiguous'; succeeds = $false },
    @{ name = 'unknown'; files = @('AdminMainDataService.cs'); select = 'Unknown'; expected = 'ValidationDataApi must be'; succeeds = $false },
    @{ name = 'mismatch'; files = @('AdminMasterDataService.cs'); select = 'MainData'; expected = 'requires AdminMainDataService.cs'; succeeds = $false }
)) {
    $root = Join-Path $run $case.name
    $service = Join-Path $root 'TaskOTime.AppServer\Services'
    New-Item -ItemType Directory -Path $service -Force | Out-Null
    New-Item -ItemType File -Path (Join-Path $root 'TaskOTime.AppServer\TaskOTime.AppServer.csproj') | Out-Null
    foreach ($file in $case.files) {
        New-Item -ItemType File -Path (Join-Path $service $file) | Out-Null
    }
    $arguments = @('msbuild', $project, '-nologo', '-verbosity:minimal', '-target:ValidateValidationDataApi', "-p:ApplicationRoot=$root")
    if ($case.select) { $arguments += "-p:ValidationDataApi=$($case.select)" }
    $log = Join-Path $root 'selection.log'
    & dotnet @arguments *> $log
    $code = $LASTEXITCODE
    $passed = (($code -eq 0) -eq $case.succeeds) -and (Select-String -Path $log -Pattern $case.expected -SimpleMatch -Quiet)
    $results += [pscustomobject]@{ name = $case.name; exitCode = $code; passed = $passed }
}
$results | ConvertTo-Json | Set-Content (Join-Path $run 'results.json')
$results | Format-Table -AutoSize
if (@($results | Where-Object { !$_.passed }).Count) { throw "API selection guardrails failed. See $run" }
Write-Host "Seven synthetic API-selection checks passed; SQL/application code NOT RUN. Evidence: $run"
