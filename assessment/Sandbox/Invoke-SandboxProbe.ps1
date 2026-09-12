[CmdletBinding()]
param(
    [string] $SdkRoot = (Join-Path $env:ProgramFiles 'dotnet'),
    [string] $SourceRoot,
    [string[]] $Projects,
    [string] $PublicPackageRoot,
    [string] $PublicFrameworkRoot,
    [string] $BinaryRoot,
    [string] $EntryAssembly,
    [string] $InputRoot,
    [string[]] $CommandArguments,
    [string] $ExpectedOutputRoot,
    [string] $EvidenceFile,
    [string] $EvidenceStatusProperty = 'Status',
    [string] $EvidenceSuccessValue = 'succeeded',
    [switch] $PrepareOnly,
    [switch] $SignProducerProof,
    [ValidateRange(60,900)][int] $TimeoutSeconds = 240
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'OutputEvidence.ps1')
if ($SignProducerProof -and (-not $SourceRoot -or $PrepareOnly -or $PSVersionTable.PSVersion.Major -lt 7)) {
    throw 'Signing an actual producer proof requires producer mode, execution, and PowerShell 7 or later.'
}
if (-not $PrepareOnly) {
    $feature = Get-CimInstance Win32_OptionalFeature -Filter "Name='Containers-DisposableClientVM'"
    if ($feature.InstallState -ne 1) { throw 'Windows Sandbox is not enabled. This command never enables or installs it.' }
    if (Get-Process WindowsSandbox,WindowsSandboxClient -ErrorAction SilentlyContinue) {
        throw 'An existing Sandbox session is present; refusing to disturb it.'
    }
}
$sdk = (Resolve-Path $SdkRoot).Path
if (-not (Test-Path (Join-Path $sdk 'dotnet.exe'))) { throw 'Public SDK root has no dotnet.exe.' }
$assessment = Split-Path $PSScriptRoot -Parent
$run = Join-Path $assessment ('Artifacts\sandbox-probe-' + [guid]::NewGuid().ToString('N'))
$payload = Join-Path $run 'payload'
$output = Join-Path $run 'output'
New-Item -ItemType Directory -Path $payload,$output | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'GuestProbe.ps1') $payload
Copy-Item (Join-Path $PSScriptRoot 'RestrictedProcess.cs') $payload
Copy-Item (Join-Path $assessment 'global.json') $payload
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
    if (-not $process.HasExited) { Stop-Process -Id $process.Id }
    throw "Protected controller did not bootstrap; no receipt produced. Artifacts: $run"
}
$handshake=Get-Content $bootstrap -Raw | ConvertFrom-Json
if ([Convert]::FromBase64String($handshake.TransportNonce).Length -ne 32) { throw 'Invalid protected-controller handshake.' }
Remove-Item $bootstrap
[IO.File]::WriteAllText((Join-Path $payload 'continue.flag'),'host-acknowledged')
$resultPath=Join-Path $output 'probe.json'
$result=$null
while (-not $result -and [DateTime]::UtcNow -lt $deadline) {
    if ((Get-Item -LiteralPath $output -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id }
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
    if (-not $process.HasExited) { Stop-Process -Id $process.Id }
    throw "Owned Sandbox preflight timed out; no receipt or acceptance produced. Config/artifacts: $run"
}
if (-not $process.HasExited) {
    if (-not $process.WaitForExit(60000)) { Stop-Process -Id $process.Id; throw 'Owned Sandbox did not terminate after its authenticated result.' }
}
if ((Get-Item -LiteralPath $output -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Untrusted export root after shutdown.' }
if (Test-Path (Join-Path $payload 'forbidden-write.txt')) { throw 'Readonly mapping failed; no Sandbox acceptance is possible.' }
if (-not $result.Success) { throw ('Owned Sandbox preflight failed: ' + ($result | ConvertTo-Json -Depth 8)) }
$producerVerification=$null
$executionVerification=$null
$signedProducerProof=$null
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
    $verified=@()
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
        if ($compiledHash -ne $targetHash) { throw 'Target bytes do not match the compiler-produced bytes.' }
        $verified+=@{ Project=$project; Target=$target; Sha256=$targetHash }
    }
    $producerVerification=@{ SourceUnchanged=$true; CompilerTargets=$verified; Export=$export }
    if ($SignProducerProof) {
        $proof=[ordered]@{
            Policy='taskotime-sandbox-producer-v1'
            Scope='producer-source-build-only'
            FormalVerified=$false
            SourceFiles=(Get-TreeSnapshot $sourceCopy)
            CompilerTargets=$verified
            SdkVersion=$result.SdkVersion
            RestrictedBuildExit=$result.RestrictedBuildExit
            ControllerBoundary=$result.Boundary
            ReadonlyPayload=$result.ReadonlyPayload
            DefaultRoutes=$result.DefaultRoutes
            ExpiresAt=[DateTimeOffset]::UtcNow.AddMinutes(30).ToString('O')
        }
        $bytes=[Text.Encoding]::UTF8.GetBytes(($proof | ConvertTo-Json -Depth 12 -Compress))
        $key=[Security.Cryptography.RSA]::Create(3072)
        try {
            $signature=$key.SignData($bytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)
            if(-not $key.VerifyData($bytes,$signature,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)) {
                throw 'Host producer signature self-verification failed.'
            }
            # Created only after the VM has stopped; this private key never leaves this host process.
            $publicKey=Join-Path $run 'producer-public.pem'
            [IO.File]::WriteAllText($publicKey,$key.ExportSubjectPublicKeyInfoPem())
            $receipt=Join-Path $run 'producer-proof.json'
            @{ PayloadBase64=[Convert]::ToBase64String($bytes); SignatureBase64=[Convert]::ToBase64String($signature) } |
                ConvertTo-Json | Set-Content -LiteralPath $receipt -Encoding UTF8
            $signedProducerProof=@{
                Receipt=$receipt; PublicKey=$publicKey
                PublicKeySha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($key.ExportSubjectPublicKeyInfo()))
                Scope='producer-source-build-only'; FormalVerified=$false
            }
        }
        finally { $key.Dispose() }
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
    HostProducerVerification=$producerVerification
    HostExecutionVerification=$executionVerification
    SignedProducerProof=$signedProducerProof
    FormalVerified=$false
    Note='Owned feasibility probe only. Guest controller isolation and host verification are still required for formal grading.'
}
