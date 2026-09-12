$ErrorActionPreference = 'Stop'
if ($env:USERNAME -ne 'WDAGUtilityAccount' -or -not (Test-Path 'C:\ProbePayload\GuestProbe.ps1')) {
    throw 'This owned preflight may run only inside the configured Windows Sandbox.'
}
$result = [ordered]@{ Kind='owned-preflight'; FormalVerified=$false; Success=$false }
$transportNonce=$null
try {
    Add-Type -Path 'C:\ProbePayload\RestrictedProcess.cs'
    New-Item -ItemType Directory -Path 'C:\Controller' -Force | Out-Null
    [RestrictedProcess]::ProtectController('C:\Controller')
    $bytes=New-Object byte[] 32
    $rng=[Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $transportNonce=[Convert]::ToBase64String($bytes)
    [IO.File]::WriteAllText('C:\Controller\secret.txt',$transportNonce)
    @{ TransportNonce=$transportNonce; ControllerPid=$PID } | ConvertTo-Json |
        Set-Content 'C:\ProbeOutput\bootstrap.json' -Encoding UTF8
    $deadline=[DateTime]::UtcNow.AddSeconds(120)
    while (-not (Test-Path 'C:\ProbePayload\continue.flag') -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250 }
    if (-not (Test-Path 'C:\ProbePayload\continue.flag')) { throw 'Host did not acknowledge protected controller bootstrap.' }
    if(Test-Path 'C:\ProbePayload\owned-lifecycle-delay.json') {
        $delay=Get-Content 'C:\ProbePayload\owned-lifecycle-delay.json' -Raw | ConvertFrom-Json
        if($delay.Seconds -lt 1 -or $delay.Seconds -gt 300) { throw 'Invalid owned lifecycle delay.' }
        [IO.File]::WriteAllText('C:\ProbeOutput\owned-controller-active.txt','Owned controller is active after the host acknowledged bootstrap.')
        Start-Sleep -Seconds $delay.Seconds
        throw 'Owned lifecycle negative reached its delay without host cancellation.'
    }
    New-Item -ItemType Directory -Path 'C:\ProbeWork' -Force | Out-Null
    Set-Location 'C:\ProbeWork'
    $env:DOTNET_ROOT = 'C:\PublicSdk'
    $env:PATH = 'C:\PublicSdk;' + $env:PATH
    $env:DOTNET_CLI_HOME = 'C:\ProbeWork\home'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:MSBUILDDISABLENODEREUSE = '1'
    $env:UseSharedCompilation = 'false'
    Copy-Item 'C:\ProbePayload\global.json' '.\global.json'
    $result.SdkVersion = (& 'C:\PublicSdk\dotnet.exe' --version 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw "SDK launch failed: $($result.SdkVersion)" }
    $result.FrameworkClr = [Environment]::Version.ToString()
    Add-Type -AssemblyName PresentationFramework
    $window = New-Object System.Windows.Window
    $result.FrameworkWpf = $window.GetType().FullName
    $result.GuestAdministrator = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    $result.ReadonlyPayload = $false
    try { [IO.File]::WriteAllText('C:\ProbePayload\forbidden-write.txt', 'readonly probe') }
    catch [UnauthorizedAccessException] { $result.ReadonlyPayload = $true }
    $result.DefaultRoutes = @([Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() |
        Where-Object OperationalStatus -eq Up |
        ForEach-Object { $_.GetIPProperties().GatewayAddresses } |
        Where-Object { $_.Address.AddressFamily -eq [Net.Sockets.AddressFamily]::InterNetwork -and
            $_.Address.ToString() -ne '0.0.0.0' }).Count
    @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>
  <ItemGroup><Compile Include="Program.cs"/></ItemGroup>
</Project>
'@ | Set-Content 'Probe.csproj' -Encoding UTF8
    @'
using System;
using System.IO;
using System.Security.Principal;
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class Program {
  [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
  [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
  static bool TryWrite(string path) { try { File.WriteAllText(path,"forged"); return true; } catch(UnauthorizedAccessException) { return false; } }
  static bool TryRead(string path) { try { File.ReadAllText(path); return true; } catch(UnauthorizedAccessException) { return false; } }
  public static int Main(string[] args) {
    if(args.Length > 0 && args[0] == "boundary") {
      File.WriteAllText(@"C:\ProbeWork\restricted\own.txt","owned low-integrity write");
      using var identity=WindowsIdentity.GetCurrent();
      var handle=OpenProcess(0x10,false,uint.Parse(args[1]));
      bool canReadMemory=handle != IntPtr.Zero;
      if(handle != IntPtr.Zero) CloseHandle(handle);
      var data=new {
        Administrator=new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator),
        ControllerWrite=TryWrite(@"C:\ProbeWork\controller-marker.txt"),
        HostOutputWrite=TryWrite(@"C:\ProbeOutput\forbidden-low-write.txt"), OwnWrite=true,
        ControllerRead=TryRead(@"C:\Controller\secret.txt"), ControllerMemoryRead=canReadMemory,
        LocalAppData=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
      };
      File.WriteAllText(@"C:\ProbeOutput\probe.json","{\"Success\":true,\"FormalVerified\":true,\"TransportNonce\":\"forged\"}");
      File.WriteAllText(@"C:\ProbeWork\restricted\boundary.json",System.Text.Json.JsonSerializer.Serialize(data));
      return 7;
    }
    Console.Write("owned-sandbox-canary"); return 0;
  }
}
'@ | Set-Content 'Program.cs' -Encoding UTF8
    $result.BuildLog = (& 'C:\PublicSdk\dotnet.exe' build '.\Probe.csproj' --nologo --verbosity quiet `
        '-p:NuGetAudit=false' '-p:RestoreIgnoreFailedSources=true' '-p:UseSharedCompilation=false' 2>&1 | Out-String)
    $result.BuildExit = $LASTEXITCODE
    if ($result.BuildExit -ne 0) { throw "Owned SDK build failed: $($result.BuildLog)" }
    $result.CanaryOutput = (& 'C:\PublicSdk\dotnet.exe' '.\bin\Debug\net10.0\Probe.dll' 2>&1 | Out-String).Trim()
    $result.CanaryExit = $LASTEXITCODE
    $low='C:\ProbeWork\restricted'
    New-Item -ItemType Directory -Path $low,"$low\tmp","$low\home","$low\profile\AppData\Roaming","$low\profile\AppData\Local","$low\packages" -Force | Out-Null
    & icacls.exe $low /grant '*S-1-5-12:(OI)(CI)M' /T | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot grant restricted-code access to the guest-owned work directory.' }
    & icacls.exe $low /setintegritylevel '(OI)(CI)L' /T | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot label guest-owned work directory low integrity.' }
    [IO.File]::WriteAllText('C:\ProbeWork\controller-marker.txt','trusted-controller')
    $env:TEMP="$low\tmp"; $env:TMP="$low\tmp"; $env:DOTNET_CLI_HOME="$low\home"
    $env:USERPROFILE="$low\profile"; $env:HOME="$low\profile"
    $env:APPDATA="$low\profile\AppData\Roaming"; $env:LOCALAPPDATA="$low\profile\AppData\Local"
    $env:NUGET_PACKAGES="$low\packages"
    if (Test-Path 'C:\PublicPackages') { $env:NUGET_FALLBACK_PACKAGES='C:\PublicPackages' }
    if (Test-Path 'C:\PublicFrameworkReferences') {
        $env:TargetFrameworkRootPath='C:\PublicFrameworkReferences\'
        $env:AutomaticallyUseReferenceAssemblyPackages='false'
    }
    $env:DOTNET_EnableDiagnostics='0'; $env:COMPlus_EnableDiagnostics='0'; $env:COMPlus_PerfEnabled='0'
    $result.BoundaryExit=[RestrictedProcess]::Run('C:\PublicSdk\dotnet.exe',
        @('C:\ProbeWork\bin\Debug\net10.0\Probe.dll','boundary',"$PID"),
        $low,"$low\boundary.stdout","$low\boundary.stderr",60000)
    if ($result.BoundaryExit -ne 7 -or -not (Test-Path "$low\boundary.json")) {
        throw "Restricted controller probe failed with exit $($result.BoundaryExit): $(Get-Content "$low\boundary.stderr" -Raw)"
    }
    $result.Boundary=Get-Content "$low\boundary.json" -Raw | ConvertFrom-Json
    if ($result.Boundary.Administrator -or $result.Boundary.ControllerWrite -or
        $result.Boundary.ControllerRead -or $result.Boundary.ControllerMemoryRead -or
        -not $result.Boundary.OwnWrite -or (Get-Content 'C:\ProbeWork\controller-marker.txt' -Raw) -ne 'trusted-controller') {
        throw 'Restricted process can access protected controller state, or cannot use its own work directory.'
    }
    Copy-Item '.\Probe.csproj','.\Program.cs','.\global.json' $low
    $result.RuntimeWarmup=(& 'C:\PublicSdk\dotnet.exe' 'C:\PublicSdk\sdk\10.0.401\MSBuild.dll' 'C:\ProbeWork\Probe.csproj' `
        -restore -target:Build -nologo -verbosity:quiet -p:NuGetAudit=false -p:UseSharedCompilation=false -nodeReuse:false 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0) { throw "Owned runtime warmup failed: $($result.RuntimeWarmup)" }
    $result.RestrictedBuildExit=[RestrictedProcess]::Run('C:\PublicSdk\dotnet.exe',
        @('C:\PublicSdk\sdk\10.0.401\MSBuild.dll',"$low\Probe.csproj",'-restore','-target:Build','-nologo','-verbosity:quiet','-p:NuGetAudit=false','-p:RestoreIgnoreFailedSources=true','-p:UseSharedCompilation=false','-nodeReuse:false'),
        $low,"$low\build.stdout","$low\build.stderr",120000)
    if ($result.RestrictedBuildExit -ne 0) {
        throw "Restricted SDK build failed: $(Get-Content "$low\build.stdout","$low\build.stderr" -Raw)"
    }
    if (Test-Path 'C:\ProbePayload\job.json') {
        $job=Get-Content 'C:\ProbePayload\job.json' -Raw | ConvertFrom-Json
        if ($job.Kind -notin @('producer','execute','compile')) { throw 'Unsupported controlled job kind.' }
        $producerRoot=Join-Path $low 'producer'
        function Copy-CheckedTree([string]$source,[string]$destination) {
            if ((Get-Item -LiteralPath $source -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Output reparse point rejected.' }
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            foreach($item in Get-ChildItem -LiteralPath $source -Force) {
                if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Output reparse point rejected.' }
                if ($item.PSIsContainer) { Copy-CheckedTree $item.FullName (Join-Path $destination $item.Name) }
                else { Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $destination $item.Name) }
            }
        }
        if ($job.Kind -eq 'execute') {
            $assembly=[IO.Path]::GetFullPath((Join-Path 'C:\ProbePayload\binary' $job.EntryAssembly))
            if (-not $assembly.StartsWith('C:\ProbePayload\binary\',[StringComparison]::OrdinalIgnoreCase)) { throw 'CLI entry escapes readonly binary root.' }
            $target=Join-Path $low 'output'
            $arguments=@($assembly)+@($job.Arguments | ForEach-Object { $_.Replace('{input}','C:\ProbePayload\input').Replace('{output}',$target) })
            $exit=[RestrictedProcess]::Run('C:\PublicSdk\dotnet.exe',$arguments,$low,"$low\cli.stdout","$low\cli.stderr",120000)
            $exportName='execution-'+[guid]::NewGuid().ToString('N')
            $export=Join-Path 'C:\ProbeOutput' $exportName
            New-Item -ItemType Directory -Path $export | Out-Null
            if (Test-Path $target) { Copy-CheckedTree $target (Join-Path $export 'output') }
            Copy-Item "$low\cli.stdout","$low\cli.stderr" $export
            $result.Execution=@{ ExitCode=$exit; ExportDirectory=$exportName }
        }
        elseif ($job.Kind -eq 'compile') {
            Copy-CheckedTree 'C:\ProbePayload\compiler\files' $low
            foreach($property in $job.Outputs.PSObject.Properties) {
                New-Item -ItemType Directory -Path (Split-Path $property.Name -Parent) -Force | Out-Null
            }
            & icacls.exe $low /grant '*S-1-5-12:(OI)(CI)M' /T | Out-Null
            if($LASTEXITCODE -ne 0) { throw 'Compiler work ACL failed.' }
            & icacls.exe $low /setintegritylevel '(OI)(CI)L' /T | Out-Null
            if($LASTEXITCODE -ne 0) { throw 'Compiler work integrity label failed.' }
            $response='C:\ProbeWork\compiler.rsp'
            [IO.File]::WriteAllLines($response,@($job.Arguments | ForEach-Object { '"'+$_+'"' }),[Text.Encoding]::UTF8)
            $exportName='compile-'+[guid]::NewGuid().ToString('N')
            $export=Join-Path 'C:\ProbeOutput' $exportName
            $exits=@()
            function Check-CompilerInputs {
                foreach($binding in $job.Inputs.PSObject.Properties) {
                    if((Get-FileHash -LiteralPath $binding.Name -Algorithm SHA256).Hash -cne $binding.Value.Sha256) {
                        throw 'Protected compiler input hash mismatch.'
                    }
                }
                foreach($binding in $job.CompilerFiles.PSObject.Properties) {
                    $file=Join-Path 'C:\PublicSdk\sdk\10.0.401\Roslyn\bincore' $binding.Name
                    if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -cne $binding.Value) {
                        throw 'Protected compiler implementation hash mismatch.'
                    }
                }
            }
            foreach($pass in @('initial','repeat')) {
                Check-CompilerInputs
                foreach($property in $job.Outputs.PSObject.Properties) {
                    if(Test-Path -LiteralPath $property.Name) { Remove-Item -LiteralPath $property.Name }
                }
                $exit=[RestrictedProcess]::Run('C:\PublicSdk\dotnet.exe',
                    @('C:\PublicSdk\sdk\10.0.401\Roslyn\bincore\csc.dll','/noconfig',('@'+$response)),
                    $job.WorkingDirectory,"$low\compiler-$pass.stdout","$low\compiler-$pass.stderr",120000)
                $exits+=,$exit
                if($exit -ne 0) { throw "Protected csc failed: $(Get-Content "$low\compiler-$pass.stdout","$low\compiler-$pass.stderr" -Raw)" }
                Check-CompilerInputs
                $target=Join-Path $export $pass
                New-Item -ItemType Directory -Path $target -Force | Out-Null
                foreach($property in $job.Outputs.PSObject.Properties) {
                    $relative=$property.Name.Substring('C:\ProbeWork\restricted\producer\'.Length)
                    $destination=Join-Path $target $relative
                    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
                    Copy-Item -LiteralPath $property.Name -Destination $destination
                }
                foreach($file in Get-ChildItem -LiteralPath $job.WorkingDirectory -Recurse -File -Filter '*.pdb') {
                    $relative=$file.FullName.Substring('C:\ProbeWork\restricted\producer\'.Length)
                    $destination=Join-Path $target $relative
                    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
                    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
                }
            }
            $result.Compilation=@{
                Challenge=$job.Challenge; ExportDirectory=$exportName; InitialExit=$exits[0]; RepeatExit=$exits[1]
                InputHashesVerified=$true; CompilerClosureVerified=$true
            }
        }
        else {
        Copy-CheckedTree 'C:\ProbePayload\source' $producerRoot
        & icacls.exe $producerRoot /grant '*S-1-5-12:(OI)(CI)M' /T | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Producer work ACL failed.' }
        & icacls.exe $producerRoot /setintegritylevel '(OI)(CI)L' /T | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Producer integrity label failed.' }
        $records=@()
        foreach($relative in $job.Projects) {
            $project=[IO.Path]::GetFullPath((Join-Path $producerRoot $relative))
            if (-not $project.StartsWith($producerRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Project escapes producer workspace.' }
            $index=$records.Count
            $stdout=Join-Path $low "producer-$index.stdout"
            $stderr=Join-Path $low "producer-$index.stderr"
            $exit=[RestrictedProcess]::Run('C:\PublicSdk\dotnet.exe',
                @('C:\PublicSdk\sdk\10.0.401\MSBuild.dll',$project,'-restore','-target:Build','-nologo','-verbosity:quiet',
                    '-p:NuGetAudit=false','-p:UseSharedCompilation=false','-p:ProvideCommandLineArgs=true',
                    '-p:Configuration=Debug','-p:EmitCompilerGeneratedFiles=true',
                    ('-p:CompilerGeneratedFilesOutputPath='+ (Join-Path (Split-Path $project -Parent) 'obj\protected-generated')),
                    '-nodeReuse:false','-getProperty:TargetPath,SkipCompilerExecution',
                    '-getItem:CscCommandLineArgs,VbcCommandLineArgs'),
                $producerRoot,$stdout,$stderr,120000)
            $outputText=Get-Content $stdout -Raw
            if ($exit -eq 0) {
                $metadata=$outputText | ConvertFrom-Json
                $outputText=@{
                    Properties=$metadata.Properties
                    Items=@{
                        CscCommandLineArgs=@($metadata.Items.CscCommandLineArgs | ForEach-Object { @{ Identity=$_.Identity } })
                        VbcCommandLineArgs=@($metadata.Items.VbcCommandLineArgs | ForEach-Object { @{ Identity=$_.Identity } })
                    }
                } | ConvertTo-Json -Depth 6 -Compress
            }
            $records+=@{ Project=$relative; ExitCode=$exit; StandardOutput=$outputText; StandardError=(Get-Content $stderr -Raw) }
            if ($exit -ne 0) { $result.Producer=$records; throw "Submitted producer build failed: $relative" }
        }
        $exportName='producer-'+[guid]::NewGuid().ToString('N')
        Copy-CheckedTree $producerRoot (Join-Path 'C:\ProbeOutput' $exportName)
        foreach($record in $records) {
            $metadata=$record.StandardOutput | ConvertFrom-Json
            foreach($argument in $metadata.Items.CscCommandLineArgs) {
                if($argument.Identity -match '^/reference:(C:\\ProbeWork\\restricted\\packages\\|C:\\PublicPackages\\)(.+)$') {
                    $prefix=$Matches[1]
                    $reference=[IO.Path]::GetFullPath($argument.Identity.Substring('/reference:'.Length))
                    if(-not $reference.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Package reference escapes approved root.' }
                    $relative=$reference.Substring($prefix.Length)
                    for($ancestor=Get-Item -LiteralPath (Split-Path $reference -Parent) -Force; $ancestor; $ancestor=$ancestor.Parent) {
                        if($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Package reference ancestor reparse point rejected.' }
                    }
                    $destination=Join-Path (Join-Path 'C:\ProbeOutput' $exportName) ('_compiler-references\'+$relative)
                    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
                    if((Get-Item -LiteralPath $reference -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Package reference reparse point rejected.' }
                    Copy-Item -LiteralPath $reference -Destination $destination
                }
            }
        }
        $result.Producer=@{ ExportDirectory=$exportName; Projects=$records }
        }
    }
    $result.Success = $result.ReadonlyPayload -and $result.DefaultRoutes -eq 0 -and
        $result.CanaryExit -eq 0 -and $result.CanaryOutput -eq 'owned-sandbox-canary' -and $result.RestrictedBuildExit -eq 0
}
catch { $result.Error = $_.Exception.ToString() }
finally {
    if ($transportNonce) {
        $result.TransportNonce=$transportNonce
        [RestrictedProcess]::Publish('C:\ProbeOutput\probe.json',[Text.Encoding]::UTF8.GetBytes(($result | ConvertTo-Json -Depth 8)))
    }
    else {
        $result | ConvertTo-Json -Depth 8 | Set-Content 'C:\ProbeOutput\bootstrap-error.json' -Encoding UTF8
    }
    # This is guest-only shutdown, guarded above; no host or other session is stopped.
    & "$env:WINDIR\System32\shutdown.exe" /s /t 3 /f
}
