[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('original', 'vb-net472', 'csharp-net472', 'csharp-net10')]
    [string] $Stage,

    [Parameter(Mandatory)]
    [string] $PromptPath,

    [Parameter(Mandatory)]
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$refs = @{
    'original' = 'modernization-original'
    'vb-net472' = 'candidate-vb-net472'
    'csharp-net472' = 'candidate-csharp-net472'
    'csharp-net10' = 'candidate-csharp-net10'
}
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
$prompt = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PromptPath)
if (Test-Path -LiteralPath $output) {
    throw "Output already exists; refusing to overwrite: $output"
}
if (-not (Test-Path -LiteralPath $prompt -PathType Leaf)) {
    throw "Candidate prompt not found: $prompt"
}
$commit = & git -C $repository rev-parse --verify "$($refs[$Stage])^{commit}"
if ($LASTEXITCODE -ne 0) {
    throw "Stage reference is unavailable: $($refs[$Stage])"
}
$commit = $commit.Trim()
$rootFiles = @(& git -C $repository ls-tree --name-only $commit)
if ($LASTEXITCODE -ne 0) {
    throw "Could not inspect candidate commit $commit."
}
$exportPaths = @('src', 'README.md', '.gitignore')
if ($rootFiles -contains 'global.json') {
    $exportPaths += 'global.json'
}
$archive = Join-Path ([IO.Path]::GetTempPath()) ("taskotime-candidate-" + [Guid]::NewGuid().ToString('N') + '.zip')
try {
    & git -C $repository archive --format=zip "--output=$archive" $commit -- @exportPaths
    if ($LASTEXITCODE -ne 0) {
        throw "Could not export candidate stage $Stage."
    }
    Expand-Archive -LiteralPath $archive -DestinationPath $output
    Copy-Item -LiteralPath $prompt -Destination (Join-Path $output 'Candidate-Prompt.md')

    $files = @(Get-ChildItem -LiteralPath $output -Recurse -Force -File)
    $manifestFiles = foreach ($file in $files) {
        $relative = $file.FullName.Substring($output.TrimEnd('\').Length + 1).Replace('\', '/')
        if ($relative -match '(^|/)(assessment|tools|tooling|\.git|bin|obj)(/|$)') {
            throw "Private or generated material appeared in the candidate export: $relative"
        }
        [ordered]@{
            path = $relative
            sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    [ordered]@{
        schemaVersion = 1
        stage = $Stage
        sourceCommit = $commit
        files = @($manifestFiles | Sort-Object { $_.path })
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'candidate-manifest.json') -Encoding utf8
    Write-Output "Exported $Stage ($commit) to $output without Git history, preparation tools, or assessment files."
}
finally {
    if (Test-Path -LiteralPath $archive) {
        Remove-Item -LiteralPath $archive
    }
}
