function New-OwnedProfilePayload([string]$payload,[string]$sdk,[string]$profile) {
    if($profile -cnotin @('low','medium')) { throw 'Unknown owned diagnostic profile.' }
    $root=Join-Path $payload 'owned-profile'
    New-Item -ItemType Directory -Path $root | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'OwnedProfileProbe.cs') -Destination (Join-Path $root 'Program.cs')
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup><Compile Include="Program.cs"/></ItemGroup></Project>' |
        Set-Content -LiteralPath (Join-Path $root 'OwnedProfile.csproj') -Encoding UTF8
    $log=& (Join-Path $sdk 'dotnet.exe') build (Join-Path $root 'OwnedProfile.csproj') --nologo --verbosity quiet `
        -p:NuGetAudit=false -p:UseSharedCompilation=false -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false 2>&1
    $log | Set-Content -LiteralPath (Join-Path $root 'owned-build.log') -Encoding UTF8
    if($LASTEXITCODE -ne 0) { throw 'Owned profile probe build failed.' }
    if($profile -eq 'medium') {
        $original=Get-Content -LiteralPath (Join-Path $payload 'RestrictedProcess.cs') -Raw
        if([regex]::Matches($original,'S-1-16-4096').Count -ne 2 -or
            [regex]::Matches($original,'public static class RestrictedProcess').Count -ne 1) {
            throw 'Diagnostic clone no longer matches the reviewed low-token implementation.'
        }
        $medium=$original.Replace('public static class RestrictedProcess','public static class OwnedMediumRestrictedProcess').
            Replace('S-1-16-4096','S-1-16-8192').Replace('"Set low integrity"','"Set diagnostic medium integrity"')
        [IO.File]::WriteAllText((Join-Path $root 'OwnedMediumRestrictedProcess.cs'),$medium)
    }
    @{ Profile=$profile; Scope='owned-diagnostic-only'; FormalVerified=$false } | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $payload 'owned-profile.json') -Encoding UTF8
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'OwnedProfileDiagnostic.ps1') -Destination $payload
}

function Invoke-OwnedProfileDiagnostic([string]$low,[int]$controller,[string[]]$environment) {
    $spec=Get-Content 'C:\ProbePayload\owned-profile.json' -Raw | ConvertFrom-Json
    if($spec.Profile -cnotin @('low','medium') -or (Test-Path 'C:\ProbePayload\job.json')) {
        throw 'Owned profile diagnostics cannot execute a submitted job.'
    }
    $assembly='C:\ProbePayload\owned-profile\bin\Debug\net10.0\OwnedProfile.dll'
    $work=Join-Path $low 'owned-profile'
    $stdout=Join-Path $low 'profile.stdout'; $stderr=Join-Path $low 'profile.stderr'
    if($spec.Profile -eq 'medium') {
        Add-Type -Path 'C:\ProbePayload\owned-profile\OwnedMediumRestrictedProcess.cs'
        $null=[OwnedMediumRestrictedProcess]::InitializeWorkerDesktop()
        try { $exit=[OwnedMediumRestrictedProcess]::Run('C:\PublicSdk\dotnet.exe',@($assembly,"$controller",$work),$low,$stdout,$stderr,60000,$environment) }
        finally { [OwnedMediumRestrictedProcess]::CloseWorkerDesktop() }
    } else {
        $exit=[RestrictedProcess]::Run('C:\PublicSdk\dotnet.exe',@($assembly,"$controller",$work),$low,$stdout,$stderr,60000,$environment)
    }
    if($exit -ne 0 -or (Get-Item $stdout).Length -gt 65536 -or (Get-Item $stderr).Length -ne 0) {
        throw "Owned profile probe failed: $(Get-Content $stdout,$stderr -Raw)"
    }
    $probe=Get-Content $stdout -Raw | ConvertFrom-Json
    $child=@(Get-Process -Id $probe.ChildPid -ErrorAction SilentlyContinue)
    if($child.Count -ne 0) { throw 'Owned diagnostic descendant still exists after job completion.' }
    return @{
        Profile=$spec.Profile; Probe=$probe; DescendantStopped=$true; Scope='diagnostic-only'; FormalVerified=$false
        ControllerMarkerUnchanged=((Get-Content 'C:\ProbeWork\controller-marker.txt' -Raw) -ceq 'trusted-controller')
        ReadonlyMarkerAbsent=(-not (Test-Path 'C:\ProbePayload\owned-profile-write.txt'))
    }
}
