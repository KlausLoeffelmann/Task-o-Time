[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$LockFile,
    [Parameter(Mandatory)][string]$Destination,
    [string]$PackageCache=$env:NUGET_PACKAGES,
    [string]$SdkRoot=(Join-Path $env:ProgramFiles 'dotnet')
)
$ErrorActionPreference='Stop'
if(-not [IO.Path]::IsPathRooted($Destination)) { $Destination=Join-Path (Get-Location).Path $Destination }
$Destination=[IO.Path]::GetFullPath($Destination)
if(Test-Path -LiteralPath $Destination) { throw 'Use a fresh curated package destination.' }
if(-not $PackageCache) { throw 'An explicit existing public package cache is required.' }
. (Join-Path $PSScriptRoot 'CompilerPlan.ps1')
foreach($assembly in @('NuGet.Frameworks.dll','NuGet.Common.dll','NuGet.Packaging.dll')) {
    Add-Type -Path (Join-Path $SdkRoot ('sdk\10.0.401\'+$assembly))
}
$lock=Get-Content -LiteralPath $LockFile -Raw | ConvertFrom-Json
if($lock.version -ne 1 -or -not $lock.dependencies.'net10.0') { throw 'Only a locked net10.0 producer closure is supported.' }
$bindings=[ordered]@{}
foreach($dependency in $lock.dependencies.'net10.0'.PSObject.Properties) {
    $id=$dependency.Name.ToLowerInvariant(); $version=$dependency.Value.resolved
    if($id -notmatch '^[a-z0-9][a-z0-9_.-]*$' -or $version -notmatch '^[0-9][a-z0-9_.-]*$' -or -not $dependency.Value.contentHash) {
        throw 'Invalid locked package identity.'
    }
    $source=Join-Path $PackageCache "$id\$version"
    $target=Join-Path $Destination "$id\$version"
    $archive=Join-Path $source "$id.$version.nupkg"
    $archiveHash=Get-CompilerFileHash $archive
    $stream=[IO.File]::OpenRead($archive)
    try {
        $reader=[NuGet.Packaging.PackageArchiveReader]::new($stream)
        try {
            if($reader.GetContentHash([Threading.CancellationToken]::None) -cne $dependency.Value.contentHash) {
                throw "NuGet canonical archive hash differs from lock: $id"
            }
        } finally { $reader.Dispose() }
    } finally { $stream.Dispose() }
    $zip=[IO.Compression.ZipFile]::OpenRead($archive)
    try {
        foreach($entry in $zip.Entries) {
            if($entry.FullName.EndsWith('/')) { continue }
            $relative=$entry.FullName.Replace('/','\')
            $file=[IO.Path]::GetFullPath((Join-Path $source $relative))
            if(-not $file.StartsWith([IO.Path]::GetFullPath($source).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or
                $relative.Contains(':') -or $entry.Length -gt 268435456) { throw 'Invalid archive path or size.' }
            $copy=Join-Path $target $relative
            New-Item -ItemType Directory -Path (Split-Path $copy -Parent) -Force | Out-Null
            $input=$entry.Open()
            try {
                $output=[IO.File]::Open($copy,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
                try { $input.CopyTo($output) } finally { $output.Dispose() }
            } finally { $input.Dispose() }
        }
    } finally { $zip.Dispose() }
    $metadataPath=Join-Path $source '.nupkg.metadata'
    $null=Get-CompilerFileHash $metadataPath
    $metadata=Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    $feed=[uri]$metadata.source
    if($metadata.contentHash -cne $dependency.Value.contentHash -or $feed.Scheme -ne 'https' -or $feed.UserInfo -or $feed.Query) {
        throw 'Public installed package metadata is not bound to the lock or contains private feed credentials.'
    }
    foreach($name in @("$id.$version.nupkg","$id.$version.nupkg.sha512",'.nupkg.metadata')) {
        $null=Get-CompilerFileHash (Join-Path $source $name)
        Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $target $name)
    }
    $bindings["$id/$version"]=@{ NuGetContentHash=$dependency.Value.contentHash; ArchiveSha256=$archiveHash }
}
[pscustomobject]@{ Root=[IO.Path]::GetFullPath($Destination); Packages=$bindings; LockSha256=(Get-CompilerFileHash $LockFile) }
