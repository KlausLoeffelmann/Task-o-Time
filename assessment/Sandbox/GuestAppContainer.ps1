function Invoke-AppContainerCompatibility($job,[string]$sid,[int]$controller) {
    $root='C:\OwnedAppContainerPublic'
    $work='C:\OwnedAppContainerWork'
    $budget=@{Files=0;Bytes=[long]0}
    $user=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    function Copy-AppContainerMaterial([string]$source,[string]$destination) {
        $item=Get-Item -LiteralPath $source -Force
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'AppContainer material reparse point rejected.' }
        if($item.PSIsContainer) {
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            foreach($entry in Get-ChildItem -LiteralPath $source -Force) { Copy-AppContainerMaterial $entry.FullName (Join-Path $destination $entry.Name) }
        } else {
            $budget.Files++; $budget.Bytes+=$item.Length
            if($budget.Files -gt 30000 -or $budget.Bytes -gt 2147483648 -or $item.Length -gt 268435456) { throw 'AppContainer material budget exceeded.' }
            New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $source -Destination $destination
        }
    }
    foreach($relative in @('dotnet.exe','host\fxr\10.0.12','shared\Microsoft.NETCore.App\10.0.12',
        'sdk\10.0.401','packs\Microsoft.NETCore.App.Ref\10.0.12','packs\Microsoft.NETCore.App.Host.win-x64\10.0.12',
        'packs\Microsoft.WindowsDesktop.App.Ref\10.0.12','packs\Microsoft.AspNetCore.App.Ref\10.0.12')) {
        Copy-AppContainerMaterial (Join-Path 'C:\PublicSdk' $relative) (Join-Path ($root+'\sdk') $relative)
    }
    Copy-AppContainerMaterial 'C:\ProbePayload\binary' ($root+'\binary')
    Copy-AppContainerMaterial 'C:\ProbePayload\input' ($root+'\input')
    Copy-AppContainerMaterial 'C:\ProbePayload\owned-profile\bin\Debug\net10.0' ($root+'\probe')
    foreach($directory in @($work,($work+'\tmp'),($work+'\home'),($work+'\packages'),
        ($work+'\profile\AppData\Local'),($work+'\profile\AppData\Roaming'))) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    foreach($entry in @(@($root,'RX'),@($work,'M'))) {
        & icacls.exe $entry[0] /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' ("*$user"+':(OI)(CI)F') ("*$sid"+':(OI)(CI)'+$entry[1]) | Out-Null
        if($LASTEXITCODE -ne 0) { throw 'Guest-owned AppContainer material ACL failed.' }
        & icacls.exe ($entry[0]+'\*') /reset /T | Out-Null
        if($LASTEXITCODE -ne 0) { throw 'Guest-owned AppContainer descendant ACL inheritance failed.' }
        & icacls.exe $entry[0] /setintegritylevel '(OI)(CI)L' /T | Out-Null
        if($LASTEXITCODE -ne 0) { throw 'Guest-owned AppContainer material label failed.' }
    }
    $entries=New-ExplicitWorkerEnvironment $work $env:WINDIR $PSHOME $false $false
    $environment=@($entries | ForEach-Object { $_.Replace('C:\PublicSdk',$root+'\sdk') })
    $environment+=@('MSBuildEnableWorkloadResolver=false')
    $environment+=@('DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false','RestoreIgnoreFailedSources=true')
    $dotnet=$root+'\sdk\dotnet.exe'
    @($root,$work,$dotnet,($root+'\probe\OwnedProfile.dll')) | ForEach-Object {
        @{Path=$_;Sddl=(Get-Acl -LiteralPath $_).Sddl}
    } | ConvertTo-Json -Depth 5 | Set-Content 'C:\ProbeOutput\appcontainer-material-acls.json' -Encoding UTF8
    $probeExit=[RestrictedProcess]::RunAppContainerDiagnostic($dotnet,
        @(($root+'\probe\OwnedProfile.dll'),"$controller",($work+'\probe-work'),$dotnet,($root+'\input'),
            ((Get-Process -Id $controller).Threads.Id -join ',')),
        $work,$work+'\probe.stdout',$work+'\probe.stderr',60000,$environment)
    $probeText=Get-Content ($work+'\probe.stdout') -Raw
    $probeError=Get-Content ($work+'\probe.stderr') -Raw
    if($probeExit -ne 0) { throw "AppContainer owned runtime probe failed ($probeExit): $probeText $probeError" }
    $probe=$probeText | ConvertFrom-Json
    $unexpectedKernelAccess=@($probe.ControllerKernelAccess.Process.PSObject.Properties |
        Where-Object { $_.Name -ne 'query-limited' -and $_.Value -eq $true }).Count -gt 0
    $unexpectedKernelAccess=$unexpectedKernelAccess -or @($probe.ControllerKernelAccess.Token.PSObject.Properties |
        Where-Object { $_.Name -ne 'query' -and $_.Value -eq $true }).Count -gt 0
    foreach($thread in $probe.ControllerKernelAccess.Threads.PSObject.Properties) {
        if(@($thread.Value.PSObject.Properties | Where-Object Value -eq $true).Count -gt 0) { $unexpectedKernelAccess=$true }
    }
    $unexpectedInputWrite=$false
    foreach($path in $probe.EffectiveFilesystemAccess) {
        if($path.Path.StartsWith($work+'\',[StringComparison]::OrdinalIgnoreCase)) { continue }
        if(@($path.Permissions.PSObject.Properties | Where-Object { $_.Value.Allowed -eq $true }).Count -gt 0) { $unexpectedInputWrite=$true }
    }
    if($probe.Token.IsAppContainer -ne $true -or $probe.Token.Integrity -cne 'S-1-16-4096' -or
        $probe.Token.CapabilityCount -ne 0 -or $probe.Token.Administrator -ne $false -or
        @($probe.Token.EnabledPrivileges | Where-Object { $_ -cne 'SeChangeNotifyPrivilege' }).Count -ne 0 -or
        $probe.ControllerFileRead.Allowed -ne $false -or $probe.ControllerFileWrite.Allowed -ne $false -or
        $probe.ControllerProcessRead -ne $false -or $probe.ControllerProcessWrite -ne $false -or
        $probe.ControllerProcessTerminate -ne $false -or $unexpectedKernelAccess -or $unexpectedInputWrite -or
        $probe.ReadonlySdkWrite.Allowed -ne $false -or $probe.ReadonlyBinaryWrite.Allowed -ne $false -or
        $probe.ReadonlyInputWrite.Allowed -ne $false -or $probe.OwnWorkWrite.Allowed -ne $true -or
        -not $probe.InJob -or -not $probe.ChildInJob -or
        @(Get-Process -Id $probe.ChildPid -ErrorAction SilentlyContinue).Count -ne 0) {
        throw 'AppContainer owned capability/material/job checks did not pass.'
    }
    $diagnosticProjects=@(Get-ChildItem -LiteralPath ($root+'\input') -File -Filter '*.vbproj')
    if($diagnosticProjects.Count -eq 1) {
        $null=[RestrictedProcess]::RunAppContainerDiagnostic($dotnet,
            @('msbuild',$diagnosticProjects[0].FullName,'-getProperty:NetCoreRoot,NetCoreTargetingPackRoot,TargetFramework,NuGetPackageRoot',
                '-nologo','-verbosity:quiet'),$work,($work+'\sdk-evaluation.stdout'),($work+'\sdk-evaluation.stderr'),60000,$environment)
    }
    $arguments=@($root+'\binary\'+$job.EntryAssembly)+@($job.Arguments | ForEach-Object {
        $_.Replace('{input}',$root+'\input').Replace('{output}',$work+'\output')
    })
    $exit=$null; $failure=$null
    try { $exit=[RestrictedProcess]::RunAppContainerDiagnostic($dotnet,$arguments,$work,$work+'\cli.stdout',$work+'\cli.stderr',120000,$environment) }
    catch { $failure=$_.Exception.Message }
    $exportName='appcontainer-'+[guid]::NewGuid().ToString('N')
    $export=Join-Path 'C:\ProbeOutput' $exportName
    New-Item -ItemType Directory -Path $export | Out-Null
    $budget.Files=0; $budget.Bytes=0
    foreach($name in @('probe.stdout','probe.stderr','cli.stdout','cli.stderr','sdk-evaluation.stdout','sdk-evaluation.stderr')) {
        if(Test-Path -LiteralPath (Join-Path $work $name)) { Copy-AppContainerMaterial (Join-Path $work $name) (Join-Path $export $name) }
    }
    if(-not $failure -and (Test-Path -LiteralPath ($work+'\output'))) { Copy-AppContainerMaterial ($work+'\output') (Join-Path $export 'output') }
    foreach($directory in Get-ChildItem -LiteralPath $work -Directory -Filter '*migration*') {
        Copy-AppContainerMaterial $directory.FullName (Join-Path $export ('failed-staging\'+$directory.Name))
    }
    return @{
        Scope='unchanged-producer-appcontainer-compatibility-only'; FormalVerified=$false; AcceptanceProfileApproved=$false
        AppContainerSid=$sid; Capabilities=@(); Probe=$probe; OriginalProducerExit=$exit; Failure=$failure
        ExportDirectory=$exportName; WorkerEnvironment=$environment
        ReadonlyPublicRoot=$root; WritableStorageRoot=$work
        Note='No frozen-output, behavior, deterministic-replay or acceptance verdict is made.'
    }
}
