[CmdletBinding()]
param(
    [switch] $IncludeSql,
    [switch] $IncludeIdeal,
    [string] $ApplicationRoot = '..\..\src\TaskOTime',
    [string] $ValidationFramework = 'net472'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Push-Location $PSScriptRoot
try {
    $run = Join-Path 'artifacts' ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $run -Force | Out-Null
    $results = [System.Collections.Generic.List[object]]::new()
    function Invoke-Validation([string] $name, [string[]] $arguments) {
        & dotnet @arguments *> (Join-Path $run "$name.log")
        $code = $LASTEXITCODE
        $results.Add([pscustomobject]@{ name = $name; exitCode = $code; arguments = $arguments })
        Write-Host "$name : exit $code"
    }

    $source = (Resolve-Path $ApplicationRoot).Path
    $stageArguments = @("-p:ApplicationRoot=$source", "-p:ValidationFramework=$ValidationFramework")
    Invoke-Validation 'appserver' @(
        'test', "$source\TaskOTime.AppServer.Tests\TaskOTime.AppServer.Tests.csproj",
        '--logger', 'trx;LogFileName=appserver.trx', '--results-directory', $run
    )
    Invoke-Validation 'core-workflows' @(
        'test', "$source\TaskOTime.TimeTrackingServices.Tests\TaskOTime.TimeTrackingServices.Tests.vbproj",
        '--logger', 'trx;LogFileName=core-workflows.trx', '--results-directory', $run
    )
    $filter = if ($IncludeSql) { 'FullyQualifiedName~TaskOTime.AppServer.IntegrationTests' } else { 'FullyQualifiedName~LocalDbGuardrailTests' }
    Invoke-Validation 'integration' @(
        'test', "$source\TaskOTime.AppServer.IntegrationTests\TaskOTime.AppServer.IntegrationTests.csproj",
        '--filter', $filter, '--logger', 'trx;LogFileName=integration.trx', '--results-directory', $run
    )
    Invoke-Validation 'sta-host' @('run', '--project', 'StaSmoke', '--', '--self-test')
    Invoke-Validation 'fixture-guardrails' (@('run', '--project', 'FixtureRunner') + $stageArguments + @('--', 'self-test'))
    if ($IncludeSql) {
        Invoke-Validation 'fixture-setup' (@('run', '--project', 'FixtureRunner', '--no-build') + $stageArguments + @('--', 'fixture-check'))
    }
    if ($IncludeIdeal) {
        Invoke-Validation 'ideal-correctness' (@(
            'test', 'IdealRegression.Tests', '--logger', 'trx;LogFileName=ideal-correctness.trx', '--results-directory', $run
        ) + $stageArguments)
    }
    [pscustomobject]@{
        timestampUtc = [DateTime]::UtcNow.ToString('o')
        commit = (& git rev-parse HEAD)
        workingTreeChanges = @(& git status --porcelain)
        sdk = (& dotnet --version)
        localDb = if (Get-Command SqlLocalDB -ErrorAction SilentlyContinue) { @(& SqlLocalDB info MSSQLLocalDB) } else { @('LocalDB tool is unavailable') }
        includeSql = [bool] $IncludeSql
        includeIdeal = [bool] $IncludeIdeal
        applicationRoot = $source
        validationFramework = $ValidationFramework
        results = $results
    } | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $run 'manifest.json')
    Write-Host "Private evidence: $run"
    if (@($results | Where-Object exitCode -ne 0).Count) { exit 1 }
}
finally {
    Pop-Location
}
