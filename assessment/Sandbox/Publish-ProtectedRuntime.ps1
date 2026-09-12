[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ProducerEvidence,
    [Parameter(Mandatory)][string]$Destination,
    [string]$PublicPackageRoot,
    [string]$PackageBindings
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'CompilerPlan.ps1')
if(-not [IO.Path]::IsPathRooted($Destination)) { $Destination=Join-Path (Get-Location).Path $Destination }
if(Test-Path -LiteralPath $Destination) { throw 'Protected runtime requires a fresh output directory.' }
$producer=Get-Content -LiteralPath $ProducerEvidence -Raw | ConvertFrom-Json
if($producer.Protocol -cne 'protected-producer-observation-v1' -or $producer.ClaimedTargetMatches -ne $true -or
    $producer.CompilerVerification.SandboxStopped -ne $true -or $producer.CompilerVerification.Deterministic -ne $true -or
    (Get-CompilerFileHash $producer.ProtectedAssembly) -cne $producer.ProtectedSha256) {
    throw 'Expected a completed matching independent compiler artifact.'
}
$assembly=[IO.Path]::GetFileName($producer.ProtectedAssembly)
$name=[IO.Path]::GetFileNameWithoutExtension($assembly)
$buildDirectory=Split-Path $producer.ClaimedAssembly -Parent
$depsPath=Join-Path $buildDirectory ($name+'.deps.json')
$null=Get-CompilerFileHash $depsPath
if((Get-Item -LiteralPath $depsPath).Length -gt 4194304) { throw 'Runtime graph exceeds the inspection limit.' }
$deps=Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json -Depth 32
$targetName='.NETCoreApp,Version=v10.0'
if($deps.runtimeTarget.name -cne $targetName -or @($deps.targets.PSObject.Properties).Count -ne 1) {
    throw 'Only portable net10.0 console runtime graphs are supported.'
}
$packages=$null
if($PackageBindings -or $PublicPackageRoot) {
    if(-not $PackageBindings -or -not $PublicPackageRoot) { throw 'Supply both the curated package root and its locked archive bindings.' }
    $packages=(Get-Content -LiteralPath $PackageBindings -Raw | ConvertFrom-Json).Packages
}
$mainCount=0
foreach($library in $deps.libraries.PSObject.Properties) {
    if($library.Value.type -eq 'project') {
        if($library.Name -notmatch ('^'+[regex]::Escape($name)+'/[0-9.]+$')) { throw 'Unprotected project dependency in runtime graph.' }
        $mainCount++
    }
    elseif($library.Value.type -eq 'package') {
        $key=$library.Name.ToLowerInvariant()
        $binding=$packages.PSObject.Properties[$key]
        if(-not $binding -or $library.Value.sha512 -cne ('sha512-'+$binding.Value.NuGetContentHash) -or
            $library.Value.path -cne $key) { throw 'Runtime dependency differs from the approved package closure.' }
    }
    else { throw 'Unsupported runtime library kind.' }
}
if($mainCount -ne 1) { throw 'No unique protected runtime entry project.' }
function Check-DependencyPaths($node) {
    if($node -is [string]) {
        if($node.Contains('\') -or $node.Contains(':') -or $node -match '(^|/)\.\.(/|$)') { throw 'Escaping runtime dependency metadata.' }
    }
    elseif($node -is [pscustomobject]) {
        foreach($property in $node.PSObject.Properties) {
            if($property.Name.Contains('\') -or $property.Name.Contains(':') -or $property.Name -match '(^|/)\.\.(/|$)') {
                throw 'Escaping runtime dependency path.'
            }
            Check-DependencyPaths $property.Value
        }
    }
    elseif($node -is [array]) { foreach($value in $node) { Check-DependencyPaths $value } }
}
Check-DependencyPaths $deps
$approvedFiles=New-Object 'Collections.Generic.Dictionary[string,string]' ([StringComparer]::OrdinalIgnoreCase)
if($packages) {
    foreach($package in $packages.PSObject.Properties) {
        $path=Join-Path $PublicPackageRoot $package.Name.Replace('/','\')
        $zipName=$package.Name.Replace('/','.')+'.nupkg'
        if((Get-CompilerFileHash (Join-Path $path $zipName)) -cne $package.Value.ArchiveSha256) {
            throw 'Curated dependency archive changed since approval.'
        }
        $archive=[IO.Compression.ZipFile]::OpenRead((Join-Path $path $zipName))
        try {
            foreach($entry in $archive.Entries) {
                if(-not $entry.FullName.EndsWith('.dll',[StringComparison]::OrdinalIgnoreCase)) { continue }
                $relative=$entry.FullName.Replace('/','\')
                $file=[IO.Path]::GetFullPath((Join-Path $path $relative))
                if(-not $file.StartsWith([IO.Path]::GetFullPath($path).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or
                    $relative.Contains(':')) { throw 'Escaping package DLL path.' }
                $stream=$entry.Open()
                try { $sha=[Security.Cryptography.SHA256]::Create(); $hash=[Convert]::ToHexString($sha.ComputeHash($stream)) }
                finally { $stream.Dispose(); $sha.Dispose() }
                if((Get-CompilerFileHash $file) -cne $hash) { throw 'Curated runtime DLL differs from the approved archive.' }
                $approvedFiles[[IO.Path]::GetFileName($file)+'|'+$hash]=$file
            }
        } finally { $archive.Dispose() }
    }
}
New-Item -ItemType Directory -Path $Destination | Out-Null
$bindings=New-Object 'Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
foreach($file in Get-ChildItem -LiteralPath $buildDirectory -Recurse -File) {
    $relative=$file.FullName.Substring($buildDirectory.Length).TrimStart('\')
    if($relative -ceq $assembly) { continue }
    if($file.Extension -ine '.dll') {
        if($relative -in @(($name+'.exe'),($name+'.pdb'),($name+'.deps.json'),($name+'.runtimeconfig.json'),'global.json')) { continue }
        throw "Unsupported runtime sidecar: $relative"
    }
    $hash=Get-CompilerFileHash $file.FullName
    $key=$file.Name+'|'+$hash
    if(-not $approvedFiles.ContainsKey($key)) { throw "Runtime DLL is not a hash-matched approved dependency: $relative" }
    $copy=Join-Path $Destination $relative
    New-Item -ItemType Directory -Path (Split-Path $copy -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $approvedFiles[$key] -Destination $copy
    $bindings[$relative]=@{ Sha256=$hash; Origin=$approvedFiles[$key] }
}
Copy-Item -LiteralPath $producer.ProtectedAssembly -Destination (Join-Path $Destination $assembly)
Copy-Item -LiteralPath $depsPath -Destination (Join-Path $Destination ($name+'.deps.json'))
$bindings[$assembly]=@{ Sha256=$producer.ProtectedSha256; Origin='protected-csc' }
$bindings[$name+'.deps.json']=@{ Sha256=(Get-CompilerFileHash $depsPath); Origin='validated-runtime-graph' }
[ordered]@{
    runtimeOptions=[ordered]@{
        tfm='net10.0'; framework=[ordered]@{ name='Microsoft.NETCore.App'; version='10.0.12' }
        rollForward='Disable'
    }
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Destination ($name+'.runtimeconfig.json')) -Encoding UTF8
[ordered]@{ sdk=[ordered]@{version='10.0.401';rollForward='disable';allowPrerelease=$false} } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Destination 'global.json') -Encoding UTF8
foreach($file in @(($name+'.runtimeconfig.json'),'global.json')) {
    $bindings[$file]=@{ Sha256=(Get-CompilerFileHash (Join-Path $Destination $file)); Origin='trusted-runtime-policy' }
}
[pscustomobject]@{ BinaryRoot=$Destination; EntryAssembly=$assembly; ProducerEvidence=$ProducerEvidence; Files=$bindings; FormalVerified=$false }
