function Initialize-PrivateControllerEnvironment {
    foreach($directory in @('C:\Controller\owned','C:\Controller\tmp','C:\Controller\home',
        'C:\Controller\profile\AppData\Local','C:\Controller\profile\AppData\Roaming','C:\Controller\packages')) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    $env:TEMP='C:\Controller\tmp'; $env:TMP=$env:TEMP
    $env:USERPROFILE='C:\Controller\profile'; $env:HOME=$env:USERPROFILE
    $env:LOCALAPPDATA='C:\Controller\profile\AppData\Local'
    $env:APPDATA='C:\Controller\profile\AppData\Roaming'
    $env:DOTNET_CLI_HOME='C:\Controller\home'; $env:NUGET_PACKAGES='C:\Controller\packages'
    $env:DOTNET_ROOT='C:\PublicSdk'
    $env:PATH="C:\PublicSdk;$env:WINDIR\System32;$PSHOME"
    $env:PSModulePath="$PSHOME\Modules"
    foreach($name in @('DOTNET_STARTUP_HOOKS','DOTNET_ADDITIONAL_DEPS','DOTNET_SHARED_STORE',
        'NUGET_PLUGIN_PATHS','NUGET_CREDENTIALPROVIDERS_PATH','NUGET_FALLBACK_PACKAGES',
        'TargetFrameworkRootPath','AutomaticallyUseReferenceAssemblyPackages')) {
        [Environment]::SetEnvironmentVariable($name,$null,'Process')
    }
    foreach($name in @('DOTNET_EnableDiagnostics','COMPlus_EnableDiagnostics','COMPlus_PerfEnabled',
        'COR_ENABLE_PROFILING','CORECLR_ENABLE_PROFILING')) { [Environment]::SetEnvironmentVariable($name,'0','Process') }
    $env:DOTNET_CLI_TELEMETRY_OPTOUT='1'; $env:DOTNET_NOLOGO='1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE='false'; $env:MSBUILDDISABLENODEREUSE='1'
    $env:UseSharedCompilation='false'; $env:DOTNET_MULTILEVEL_LOOKUP='0'
    Set-Location 'C:\Controller\owned'
    [Environment]::CurrentDirectory='C:\Controller\owned'
    Import-Module "$PSHOME\Modules\Microsoft.PowerShell.Management\Microsoft.PowerShell.Management.psd1" -ErrorAction Stop
    Import-Module "$PSHOME\Modules\Microsoft.PowerShell.Utility\Microsoft.PowerShell.Utility.psd1" -ErrorAction Stop
    Import-Module "$PSHOME\Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1" -ErrorAction Stop
    $global:PSModuleAutoLoadingPreference='None'
}

function New-ExplicitWorkerEnvironment([string]$root,[string]$windows,[string]$powerShellHome,
    [bool]$packages,[bool]$framework) {
    $values=[ordered]@{
        SystemRoot=$windows; WINDIR=$windows; SystemDrive='C:'; COMSPEC=($windows+'\System32\cmd.exe')
        PATH=("C:\PublicSdk;"+$windows+'\System32;'+$powerShellHome)
        PATHEXT='.COM;.EXE;.BAT;.CMD'; PSModulePath=($powerShellHome+'\Modules')
        ProgramFiles='C:\Program Files'; 'ProgramFiles(x86)'='C:\Program Files (x86)'; ProgramW6432='C:\Program Files'
        CommonProgramFiles='C:\Program Files\Common Files'; 'CommonProgramFiles(x86)'='C:\Program Files (x86)\Common Files'
        CommonProgramW6432='C:\Program Files\Common Files'; ProgramData='C:\ProgramData'; ALLUSERSPROFILE='C:\ProgramData'
        TEMP=($root+'\tmp'); TMP=($root+'\tmp'); USERPROFILE=($root+'\profile'); HOME=($root+'\profile')
        APPDATA=($root+'\profile\AppData\Roaming'); LOCALAPPDATA=($root+'\profile\AppData\Local')
        DOTNET_CLI_HOME=($root+'\home'); NUGET_PACKAGES=($root+'\packages')
        DOTNET_ROOT='C:\PublicSdk'; DOTNET_MULTILEVEL_LOOKUP='0'
        DOTNET_CLI_TELEMETRY_OPTOUT='1'; DOTNET_NOLOGO='1'; DOTNET_GENERATE_ASPNET_CERTIFICATE='false'
        MSBUILDDISABLENODEREUSE='1'; UseSharedCompilation='false'
        DOTNET_EnableDiagnostics='0'; COMPlus_EnableDiagnostics='0'; COMPlus_PerfEnabled='0'
        COR_ENABLE_PROFILING='0'; CORECLR_ENABLE_PROFILING='0'
    }
    if($packages) { $values.NUGET_FALLBACK_PACKAGES='C:\PublicPackages' }
    if($framework) { $values.TargetFrameworkRootPath='C:\PublicFrameworkReferences\'; $values.AutomaticallyUseReferenceAssemblyPackages='false' }
    return ,@($values.GetEnumerator() | ForEach-Object { $_.Key+'='+$_.Value })
}

function Get-ControllerEnvironmentObservation {
    [ordered]@{
        Cwd=[Environment]::CurrentDirectory; PowerShellLocation=(Get-Location).Path
        TEMP=$env:TEMP; TMP=$env:TMP; USERPROFILE=$env:USERPROFILE; APPDATA=$env:APPDATA
        LOCALAPPDATA=$env:LOCALAPPDATA; DOTNET_CLI_HOME=$env:DOTNET_CLI_HOME; NUGET_PACKAGES=$env:NUGET_PACKAGES
        PATH=$env:PATH; PSModulePath=$env:PSModulePath; ModuleAutoload=$global:PSModuleAutoLoadingPreference
    }
}
