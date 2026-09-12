[CmdletBinding()]
param(
    [string] $SdkRoot = (Join-Path $env:ProgramFiles 'dotnet'),
    [string] $SourceRoot,
    [string[]] $Projects,
    [string] $PublicPackageRoot,
    [string] $PublicFrameworkRoot,
    [string] $BinaryRoot,
    [string] $EntryAssembly,
    [string] $CompilerJobRoot,
    [string] $InputRoot,
    [string[]] $CommandArguments,
    [string] $ExpectedOutputRoot,
    [string] $EvidenceFile,
    [string] $EvidenceStatusProperty = 'Status',
    [string] $EvidenceSuccessValue = 'succeeded',
    [switch] $PrepareOnly,
    [switch] $SignProducerProof,
    [ValidateRange(0,300)][int] $OwnedControllerDelaySeconds = 0,
    [ValidateRange(60,900)][int] $TimeoutSeconds = 240
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'OutputEvidence.ps1')
. (Join-Path $PSScriptRoot 'SandboxLifecycle.ps1')
if ($SignProducerProof) {
    throw 'Unsupported producer provenance: submitted MSBuild controls both compiler /out and TargetPath. Signing is disabled until compiler outputs are captured outside submitted build control.'
}
if($OwnedControllerDelaySeconds -and ($SourceRoot -or $BinaryRoot -or $CompilerJobRoot)) {
    throw 'The owned lifecycle delay cannot be combined with submitted work.'
}
if (-not $PrepareOnly) {
    $feature = Get-CimInstance Win32_OptionalFeature -Filter "Name='Containers-DisposableClientVM'"
    if ($feature.InstallState -ne 1) { throw 'Windows Sandbox is not enabled. This command never enables or installs it.' }
    if (Get-Process WindowsSandbox,WindowsSandboxClient,WindowsSandboxRemoteSession,WindowsSandboxServer -ErrorAction SilentlyContinue) {
        throw 'An existing Sandbox session is present; refusing to disturb it.'
    }
}
$sdk = (Resolve-Path $SdkRoot).Path
if (-not (Test-Path (Join-Path $sdk 'dotnet.exe'))) { throw 'Public SDK root has no dotnet.exe.' }
if ($CompilerJobRoot) {
    if($SourceRoot -or $BinaryRoot -or $PublicPackageRoot -or $PublicFrameworkRoot -or $PSVersionTable.PSVersion.Major -lt 7) {
        throw 'Protected compiler jobs require a fresh dedicated PowerShell 7 invocation without submitted builds or runtime binaries.'
    }
    . (Join-Path $PSScriptRoot 'CompilerPlan.ps1')
    $compilerPlan=Assert-CompilerPlan ((Resolve-Path $CompilerJobRoot).Path) $sdk
}
$assessment = Split-Path $PSScriptRoot -Parent
foreach($exposedRoot in @($SourceRoot,$BinaryRoot,$InputRoot,$PublicPackageRoot,$PublicFrameworkRoot,$CompilerJobRoot) | Where-Object { $_ }) {
    $exposedPath=(Resolve-Path -LiteralPath $exposedRoot).Path.TrimEnd('\')
    if($assessment.TrimEnd('\').Equals($exposedPath,[StringComparison]::OrdinalIgnoreCase) -or
        $assessment.StartsWith($exposedPath+'\',[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Never expose a root containing the private assessor tree.'
    }
}
$run = Join-Path $assessment ('Artifacts\sandbox-probe-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $run 'payload'
$output = Join-Path $run 'output'
New-Item -ItemType Directory -Path $payload,$output | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'GuestProbe.ps1') $payload
Copy-Item (Join-Path $PSScriptRoot 'RestrictedProcess.cs') $payload
Copy-Item (Join-Path $assessment 'global.json') $payload
if($OwnedControllerDelaySeconds) {
    @{ Seconds=$OwnedControllerDelaySeconds } | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $payload 'owned-lifecycle-delay.json') -Encoding UTF8
}
function Copy-SourceTree([string]$from,[string]$to,[bool]$excludeBuild=$true) {
    if ((Get-Item -LiteralPath $from -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Input reparse points are not accepted.' }
    New-Item -ItemType Directory -Path $to -Force | Out-Null
    foreach($entry in Get-ChildItem -LiteralPath $from -Force) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Input reparse points are not accepted.' }
        if($entry.PSIsContainer) {
            if(-not $excludeBuild -or $entry.Name -notin @('bin','obj','Artifacts','.git')) { Copy-SourceTree $entry.FullName (Join-Path $to $entry.Name) $excludeBuild }
        } else { Copy-Item -LiteralPath $entry.FullName -Destination (Join-Path $to $entry.Name) }
    }
}
if ($SourceRoot -and $BinaryRoot) { throw 'Choose one producer build or CLI execution per fresh Sandbox.' }
if ($CompilerJobRoot) {
    Copy-SourceTree ((Resolve-Path $CompilerJobRoot).Path) (Join-Path $payload 'compiler') $false
    Copy-Item (Join-Path $payload 'compiler\job.json') (Join-Path $payload 'job.json')
}
if ($SourceRoot) {
    if (-not $Projects) { throw 'Producer mode requires explicit relative project paths.' }
    $source=(Resolve-Path $SourceRoot).Path.TrimEnd('\')
    if ($assessment.TrimEnd('\').Equals($source,[StringComparison]::OrdinalIgnoreCase) -or
        $assessment.StartsWith($source+'\',[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Never stage a source root containing the private assessor tree.'
    }
    $sourceCopy=Join-Path $payload 'source'
    foreach($project in $Projects) {
        $full=[IO.Path]::GetFullPath((Join-Path $source $project))
        if (-not $full.StartsWith($source+'\',[StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::IsPathRooted($project) -or -not (Test-Path -LiteralPath $full -PathType Leaf)) {
            throw "Project is not a source-relative existing file: $project"
        }
    }
    Copy-SourceTree $source $sourceCopy
    @{ Kind='producer'; Projects=$Projects; Configuration='Debug' } | ConvertTo-Json -Depth 6 |
        Set-Content (Join-Path $payload 'job.json') -Encoding UTF8
}
if ($BinaryRoot) {
    if (-not $InputRoot -or -not $EntryAssembly -or -not $CommandArguments) { throw 'CLI execution requires binary, entry assembly, input and argument templates.' }
    $binary=(Resolve-Path $BinaryRoot).Path.TrimEnd('\')
    $entry=[IO.Path]::GetFullPath((Join-Path $binary $EntryAssembly))
    if ([IO.Path]::IsPathRooted($EntryAssembly) -or -not $entry.StartsWith($binary+'\',[StringComparison]::OrdinalIgnoreCase) -or
        -not (Test-Path -LiteralPath $entry -PathType Leaf)) { throw 'Invalid managed CLI entry assembly.' }
    if (-not ($CommandArguments -match '\{input\}') -or -not ($CommandArguments -match '\{output\}')) { throw 'Explicit {input}/{output} templates are required.' }
    Copy-SourceTree $binary (Join-Path $payload 'binary') $false
    Copy-SourceTree ((Resolve-Path $InputRoot).Path) (Join-Path $payload 'input') $false
    @{ Kind='execute'; EntryAssembly=$EntryAssembly; Arguments=$CommandArguments } | ConvertTo-Json -Depth 6 |
        Set-Content (Join-Path $payload 'job.json') -Encoding UTF8
}
function Get-TreeSnapshot([string]$root) {
    $snapshot=New-Object 'Collections.Generic.SortedDictionary[string,string]' ([StringComparer]::Ordinal)
    $budget=@{ Bytes=[long]0; Files=0 }
    function Visit-Snapshot([string]$directory) {
        foreach($item in Get-ChildItem -LiteralPath $directory -Force) {
            if($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Snapshot reparse point rejected.' }
            if($item.PSIsContainer) { Visit-Snapshot $item.FullName }
            else {
                $budget.Bytes+=$item.Length; $budget.Files++
                if($item.Length -gt 268435456 -or $budget.Bytes -gt 1073741824 -or $budget.Files -gt 100000) {
                    throw 'Snapshot exceeds bounded inspection limits.'
                }
                $snapshot[$item.FullName.Substring($root.Length).TrimStart('\')]=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
            }
        }
    }
    if ((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Snapshot root reparse point rejected.' }
    Visit-Snapshot $root
    return ,$snapshot
}
$expectedSnapshot=$null
if ($EvidenceFile) {
    if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Evidence JSON validation requires PowerShell 7 or later.' }
    if (-not $ExpectedOutputRoot -or [IO.Path]::GetFileName($EvidenceFile) -cne $EvidenceFile -or
        $EvidenceFile.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
        [IO.Path]::GetExtension($EvidenceFile) -ine '.json' -or
        [string]::IsNullOrWhiteSpace($EvidenceStatusProperty) -or [string]::IsNullOrWhiteSpace($EvidenceSuccessValue)) {
        throw 'Evidence separation requires an exact top-level JSON file, status contract and host expected tree.'
    }
}
if ($ExpectedOutputRoot) {
    if (-not $BinaryRoot) { throw 'Expected output comparison requires a CLI execution job.' }
    $expected=(Resolve-Path $ExpectedOutputRoot).Path.TrimEnd('\')
    foreach($exposed in @($BinaryRoot,$InputRoot,$SdkRoot,$PublicPackageRoot,$PublicFrameworkRoot) | Where-Object { $_ }) {
        $exposed=(Resolve-Path $exposed).Path.TrimEnd('\')
        if($expected -eq $exposed -or $expected.StartsWith($exposed+'\',[StringComparison]::OrdinalIgnoreCase)) {
            throw 'Expected outputs must not be inside any staged or mapped input.'
        }
    }
    $expectedSnapshot=Get-TreeSnapshot $expected
    if ($EvidenceFile -and (Test-Path -LiteralPath (Join-Path $expected $EvidenceFile))) {
        throw 'Evidence declaration would hide an expected source/configuration file.'
    }
    if ($expectedSnapshot.Count -eq 0) { throw 'Expected positive output must not be an empty fixture.' }
}
$document = New-Object Xml.XmlDocument
$configuration = $document.CreateElement('Configuration')
[void]$document.AppendChild($configuration)
foreach ($name in @('Networking','ClipboardRedirection','AudioInput','VideoInput','PrinterRedirection','vGPU')) {
    $element=$document.CreateElement($name); $element.InnerText='Disable'; [void]$configuration.AppendChild($element)
}
$protected=$document.CreateElement('ProtectedClient'); $protected.InnerText='Enable'; [void]$configuration.AppendChild($protected)
$folders=$document.CreateElement('MappedFolders'); [void]$configuration.AppendChild($folders)
$mappings=@(@($payload,'C:\ProbePayload','true'),@($sdk,'C:\PublicSdk','true'),@($output,'C:\ProbeOutput','false'))
if($CompilerJobRoot -and (Test-Path (Join-Path $payload 'compiler\files\packages'))) {
    $mappings+=,@((Join-Path $payload 'compiler\files\packages'),'C:\PublicPackages','true')
}
if ($PublicPackageRoot) { $mappings+=,@((Resolve-Path $PublicPackageRoot).Path,'C:\PublicPackages','true') }
if ($PublicFrameworkRoot) { $mappings+=,@((Resolve-Path $PublicFrameworkRoot).Path,'C:\PublicFrameworkReferences','true') }
foreach ($mapping in $mappings) {
    $folder=$document.CreateElement('MappedFolder'); [void]$folders.AppendChild($folder)
    for($i=0;$i -lt 3;$i++) {
        $element=$document.CreateElement(@('HostFolder','SandboxFolder','ReadOnly')[$i])
        $element.InnerText=$mapping[$i]; [void]$folder.AppendChild($element)
    }
}
$logon=$document.CreateElement('LogonCommand'); [void]$configuration.AppendChild($logon)
$command=$document.CreateElement('Command')
$command.InnerText='powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\ProbePayload\GuestProbe.ps1'
[void]$logon.AppendChild($command)
$config=Join-Path $run 'probe.wsb'
$document.Save($config)
if ($PrepareOnly) {
    [pscustomobject]@{ Artifacts=$run; Configuration=$config; Executed=$false; FormalVerified=$false }
    return
}
$process=$null
$remoteSessions=@()
$servers=@()
$bootstrapObserved=$false
$sandboxStopped=$false
$runFailure=$null
try {
$process=Start-Process (Join-Path $env:WINDIR 'System32\WindowsSandbox.exe') -ArgumentList ('"' + $config + '"') -PassThru
$deadline=[DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$bootstrap=Join-Path $output 'bootstrap.json'
while (-not (Test-Path $bootstrap) -and [DateTime]::UtcNow -lt $deadline) {
    if (Test-Path (Join-Path $output 'bootstrap-error.json')) {
        throw "Owned bootstrap failed (not acceptance evidence): $(Get-Content (Join-Path $output 'bootstrap-error.json') -Raw)"
    }
    Start-Sleep -Milliseconds 250
}
if (-not (Test-Path $bootstrap)) {
    throw "Protected controller did not bootstrap; no receipt produced. Artifacts: $run"
}
$bootstrapObserved=$true
$remoteSessions=@(Get-OwnedSandboxRemoteSession $config)
$servers=@(Get-Process WindowsSandboxServer -ErrorAction SilentlyContinue)
if($remoteSessions.Count -ne 1 -or $servers.Count -ne 1) { throw 'Cannot establish a unique owned Sandbox lifecycle.' }
$lifecycle=@($process)+$remoteSessions+$servers
$handshake=Get-Content $bootstrap -Raw | ConvertFrom-Json
if ([Convert]::FromBase64String($handshake.TransportNonce).Length -ne 32) { throw 'Invalid protected-controller handshake.' }
Remove-Item $bootstrap
[IO.File]::WriteAllText((Join-Path $payload 'continue.flag'),'host-acknowledged')
$deadline=[DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
$resultPath=Join-Path $output 'probe.json'
$result=$null
while (-not $result -and [DateTime]::UtcNow -lt $deadline) {
    if ((Get-Item -LiteralPath $output -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw 'Guest modified the export root into a reparse point.'
    }
    if (Test-Path -LiteralPath $resultPath -PathType Leaf) {
        $file=Get-Item -LiteralPath $resultPath -Force
        if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Untrusted result reparse point.' }
        if ($file.Length -le 1048576) {
            try {
                $candidate=Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
                if ($candidate.TransportNonce -ceq $handshake.TransportNonce) { $result=$candidate }
            } catch [ArgumentException] {} catch [System.Management.Automation.RuntimeException] {}
        }
    }
    if (-not $result) { Start-Sleep -Milliseconds 250 }
}
if (-not $result) {
    throw "Owned Sandbox preflight timed out; no receipt or acceptance produced. Config/artifacts: $run"
}
$shutdownDeadline=[DateTime]::UtcNow.AddSeconds(60)
foreach($owned in $lifecycle) {
    if (-not $owned.HasExited) {
        $remaining=[int][Math]::Max(0,($shutdownDeadline-[DateTime]::UtcNow).TotalMilliseconds)
        if($remaining -eq 0 -or -not $owned.WaitForExit($remaining)) {
            throw "Observed Sandbox process $($owned.Id) did not terminate after its authenticated result; no acceptance is possible."
        }
    }
}
$sandboxStopped=$true
if ((Get-Item -LiteralPath $output -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Untrusted export root after shutdown.' }
if (Test-Path (Join-Path $payload 'forbidden-write.txt')) { throw 'Readonly mapping failed; no Sandbox acceptance is possible.' }
if (-not $result.Success) { throw ('Owned Sandbox preflight failed: ' + ($result | ConvertTo-Json -Depth 8)) }
$producerObservation=$null
$executionVerification=$null
$signedProducerProof=$null
$compilerVerification=$null
if($CompilerJobRoot) {
    if($result.Compilation.Challenge -cne $compilerPlan.Challenge -or $result.Compilation.ExportDirectory -cnotmatch '^compile-[0-9a-f]{32}$' -or
        $result.Compilation.InitialExit -ne 0 -or $result.Compilation.RepeatExit -ne 0 -or
        $result.Compilation.InputHashesVerified -ne $true -or $result.Compilation.CompilerClosureVerified -ne $true) {
        throw 'Missing request-bound successful protected compiler runs.'
    }
    $export=Join-Path $output $result.Compilation.ExportDirectory
    $initial=Get-TreeSnapshot (Join-Path $export 'initial')
    $repeat=Get-TreeSnapshot (Join-Path $export 'repeat')
    if($initial.Count -eq 0 -or $initial.Count -ne $repeat.Count) { throw 'Protected compiler output file set differs.' }
    foreach($path in $initial.Keys) {
        if(-not $repeat.ContainsKey($path) -or $initial[$path] -cne $repeat[$path]) { throw 'Protected compilation is not deterministic.' }
    }
    $null=Assert-CompilerPlan (Join-Path $payload 'compiler') $sdk
    $compilerVerification=@{
        Protocol='protected-csc-v1'; Challenge=$compilerPlan.Challenge; Export=$export
        CompilerSha256=$compilerPlan.CompilerSha256; Deterministic=$true
            CompilerFiles=$compilerPlan.CompilerFiles; SandboxStopped=$true
        PlanSha256=(Get-FileHash (Join-Path $payload 'compiler\job.json') -Algorithm SHA256).Hash
        Outputs=$initial; Scope='independent-compilation-of-hash-bound-captured-inputs'
        FormalVerified=$false
    }
}
if ($SourceRoot) {
    if ($result.Producer.ExportDirectory -notmatch '^producer-[0-9a-f]{32}$') { throw 'Invalid producer export identity.' }
    $export=Join-Path $output $result.Producer.ExportDirectory
    $files=New-Object 'Collections.Generic.List[IO.FileInfo]'
    function Inspect-Export([string]$directory) {
        if ((Get-Item -LiteralPath $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Untrusted export reparse point.' }
        foreach($item in Get-ChildItem -LiteralPath $directory -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Untrusted export reparse point.' }
            if ($item.PSIsContainer) { Inspect-Export $item.FullName }
            else {
                if ($item.Length -gt 268435456) { throw 'Producer export exceeds per-file inspection limit.' }
                $files.Add($item)
            }
        }
    }
    Inspect-Export $export
    if (($files | Measure-Object -Property Length -Sum).Sum -gt 1073741824) { throw 'Producer export exceeds total inspection limit.' }
    foreach($file in Get-ChildItem -LiteralPath $sourceCopy -Recurse -File -Force) {
        $relative=$file.FullName.Substring($sourceCopy.Length).TrimStart('\')
        $returned=Join-Path $export $relative
        if (-not (Test-Path -LiteralPath $returned -PathType Leaf) -or
            (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $returned -Algorithm SHA256).Hash) { throw "Submitted source changed during the build: $relative" }
    }
    function Map-GuestOutput([string]$path,[string]$relativeProject) {
        $guestRoot='C:\ProbeWork\restricted\producer'
        $base=Split-Path (Join-Path $guestRoot $relativeProject) -Parent
        $full=[IO.Path]::GetFullPath($(if([IO.Path]::IsPathRooted($path)) { $path } else { Join-Path $base $path }))
        if (-not $full.StartsWith($guestRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Compiler output escapes the guest source workspace.' }
        return Join-Path $export $full.Substring($guestRoot.Length).TrimStart('\')
    }
    $observedTargets=@()
    if (@($result.Producer.Projects).Count -ne $Projects.Count) { throw 'Producer project count changed.' }
    foreach($project in $Projects) {
        $records=@($result.Producer.Projects | Where-Object Project -CEQ $project)
        if ($records.Count -ne 1 -or $records[0].ExitCode -ne 0) { throw "No successful authenticated build for $project" }
        $metadata=$records[0].StandardOutput | ConvertFrom-Json
        if ($metadata.Properties.SkipCompilerExecution -eq 'true') { throw 'Compiler execution was skipped.' }
        $arguments=@($metadata.Items.CscCommandLineArgs)+@($metadata.Items.VbcCommandLineArgs)
        $outputs=@($arguments | Where-Object { $_.Identity -like '/out:*' })
        if ($outputs.Count -ne 1) { throw 'No unique managed compiler producer.' }
        $compiled=Map-GuestOutput ($outputs[0].Identity.Substring(5).Trim('"')) $project
        $target=Map-GuestOutput $metadata.Properties.TargetPath $project
        $compiledHash=(Get-FileHash -LiteralPath $compiled -Algorithm SHA256).Hash
        $targetHash=(Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
        if ($compiledHash -ne $targetHash) { throw 'Reported build output files are inconsistent; neither is a trusted compiler capture.' }
        $observedTargets+=@{ Project=$project; Target=$target; Sha256=$targetHash }
    }
    $producerObservation=@{
        SourceUnchanged=$true; ConsistentBuildTargets=$observedTargets; Export=$export
        CompilerProvenanceVerified=$false
        Reason='Both observed output files and compiler arguments remain submitted-build-controlled; consistency is not compiler provenance.'
    }
}
if ($BinaryRoot) {
    if($result.Execution.ExportDirectory -notmatch '^execution-[0-9a-f]{32}$') { throw 'Invalid CLI export identity.' }
    $export=Join-Path $output $result.Execution.ExportDirectory
    $exportSnapshot=Get-TreeSnapshot $export
    $actual=Join-Path $export 'output'
    $matched=$false
    $executionEvidence=$null
    if($null -ne $expectedSnapshot) {
        if($result.Execution.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $actual -PathType Container)) {
            throw 'CLI did not successfully emit the expected output.'
        }
        $actualSnapshot=Get-TreeSnapshot $actual
        if ($EvidenceFile) {
            $matches=@($actualSnapshot.Keys | Where-Object { $_ -ieq $EvidenceFile })
            if($matches.Count -ne 1) { throw 'Missing or ambiguous declared execution evidence.' }
            $executionEvidence=Read-ExecutionEvidence $actual $EvidenceFile $EvidenceStatusProperty $EvidenceSuccessValue $actualSnapshot
            [void]$actualSnapshot.Remove($matches[0])
            if($actualSnapshot.Count -eq 0) { throw 'Evidence-only output is not a transformation.' }
        }
        if($actualSnapshot.Count -ne $expectedSnapshot.Count) { throw 'CLI emitted a different file set.' }
        foreach($path in $expectedSnapshot.Keys) {
            if(-not $actualSnapshot.ContainsKey($path) -or $actualSnapshot[$path] -ne $expectedSnapshot[$path]) {
                throw "CLI output differs from the host-only expected snapshot: $path"
            }
        }
        $matched=$true
    }
    $executionVerification=@{
        ExitCode=$result.Execution.ExitCode; Export=$export; HostExpectedBytesMatched=$matched
        ExecutionEvidence=$executionEvidence
        OutputHashBasis=$(if($EvidenceFile) { 'all-emitted-files-except-declared-evidence:'+$EvidenceFile } else { 'all-emitted-files' })
    }
}
[pscustomobject]@{
    Artifacts=$run; SdkVersion=$result.SdkVersion; FrameworkClr=$result.FrameworkClr
    FrameworkWpf=$result.FrameworkWpf; GuestAdministrator=$result.GuestAdministrator
    ReadonlyPayload=$result.ReadonlyPayload; DefaultRoutes=$result.DefaultRoutes
    BuildExit=$result.BuildExit; CanaryExit=$result.CanaryExit
    RestrictedBuildExit=$result.RestrictedBuildExit; Boundary=$result.Boundary
    Producer=$result.Producer
    Execution=$result.Execution
    HostProducerVerification=$null
    HostProducerObservation=$producerObservation
    HostCompilerVerification=$compilerVerification
    HostExecutionVerification=$executionVerification
    SignedProducerProof=$signedProducerProof
    SandboxStopped=$true
    SandboxProcessIds=@($lifecycle | ForEach-Object Id)
    FormalVerified=$false
    Note='Diagnostic execution only. Producer compiler provenance and full replay acceptance are unverified; producer signing is disabled.'
}
}
catch { $runFailure=$_.Exception.Message; throw }
finally {
    if($process -and -not $sandboxStopped) {
        if($servers.Count -eq 0) { $servers=@(Get-Process WindowsSandboxServer -ErrorAction SilentlyContinue) }
        Close-OwnedSandboxSession -Configuration $config -Launcher $process -RemoteSessions $remoteSessions `
            -Servers $servers -EvidencePath (Join-Path $run 'lifecycle-cleanup.json') -OriginalFailure $runFailure `
            -RequireServerObservation $bootstrapObserved
    }
}
