[CmdletBinding()]
param(
    [switch] $IncludeSql,
    [switch] $IncludeIdeal,
    [string] $ApplicationRoot = '..\..\src\TaskOTime',
    [string] $ValidationFramework = 'net10.0-windows'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Push-Location $PSScriptRoot
try {
    $run = Join-Path 'artifacts' ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $run -Force | Out-Null
    $results = [System.Collections.Generic.List[object]]::new()
    function Invoke-Validation([string] $name, [string[]] $arguments, [int] $expectedExit = 0, [string] $expectedText = '') {
        $log = Join-Path $run "$name.log"
        & dotnet @arguments *> $log
        $code = $LASTEXITCODE
        $passed = $code -eq $expectedExit -and (!$expectedText -or (Select-String -Path $log -Pattern $expectedText -SimpleMatch -Quiet))
        $results.Add([pscustomobject]@{ name = $name; exitCode = $code; expectedExit = $expectedExit; passed = $passed; expectedText = $expectedText; arguments = $arguments })
        Write-Host "$name : exit $code (expected $expectedExit), passed=$passed"
    }

    $source = (Resolve-Path $ApplicationRoot).Path
    $stageArguments = @("-p:ApplicationRoot=$source", "-p:ValidationFramework=$ValidationFramework")
    Invoke-Validation 'stage-prerequisites' (@(
        'msbuild', 'FixtureRunner\FixtureRunner.csproj', '-nologo',
        '-target:ValidateValidationFramework'
    ) + $stageArguments)
    if (!$results[$results.Count - 1].passed) {
        throw "ApplicationRoot and ValidationFramework do not match. See $run\stage-prerequisites.log."
    }
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
    Invoke-Validation 'startup-driver-synthetic' @('run', '--no-build', '--project', 'StaSmoke', '--', '--startup-self-test', 'core')
    foreach ($scenario in @(
        @{ name = 'invalid-login'; message = 'Login did not enforce the seeded temporary-password flow.' },
        @{ name = 'early-exit'; message = 'Application exited before startup validation completed successfully.' },
        @{ name = 'missing-ideal'; message = 'TaskOTime.ViewModel.Localization.LocalizationService' },
        @{ name = 'timeout'; message = 'Startup timed out waiting in phase Login.' }
    )) {
        Invoke-Validation "startup-driver-$($scenario.name)" @('run', '--no-build', '--project', 'StaSmoke', '--', '--startup-self-test', $scenario.name) 1 $scenario.message
    }
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
    if (@($results | Where-Object { !$_.passed }).Count) { exit 1 }
}
finally {
    Pop-Location
}
