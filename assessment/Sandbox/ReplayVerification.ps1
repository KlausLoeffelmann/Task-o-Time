$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'CompilerPlan.ps1')
. (Join-Path $PSScriptRoot 'OutputEvidence.ps1')

function Get-ReplayTree([string]$root) {
    $root=(Resolve-Path -LiteralPath $root).Path.TrimEnd('\')
    $files=New-Object 'Collections.Generic.SortedDictionary[string,string]' ([StringComparer]::Ordinal)
    $budget=@{ Bytes=[long]0 }
    function Visit-ReplayTree([string]$directory) {
        if((Get-Item -LiteralPath $directory -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw 'Replay tree reparse point rejected.'
        }
        foreach($item in Get-ChildItem -LiteralPath $directory -Force) {
            if($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Replay file reparse point rejected.' }
            if($item.PSIsContainer) { Visit-ReplayTree $item.FullName }
            else {
                $budget.Bytes+=$item.Length
                if($files.Count -ge 100000 -or $budget.Bytes -gt 1073741824) { throw 'Replay tree inspection budget exceeded.' }
                $files[$item.FullName.Substring($root.Length).TrimStart('\')]=Get-CompilerFileHash $item.FullName
            }
        }
    }
    Visit-ReplayTree $root
    return ,$files
}

function Get-ReplayTreeHash($files) {
    $lines=@($files.Keys | ForEach-Object { $_+':'+$files[$_] })
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))))
}

function Assert-FrozenReplayOutput([string]$actual,[string]$expected,[string]$evidenceFile,
    [string]$statusProperty='Status',[string]$successValue='succeeded') {
    $actualPath=(Resolve-Path -LiteralPath $actual).Path.TrimEnd('\')
    $expectedPath=(Resolve-Path -LiteralPath $expected).Path.TrimEnd('\')
    if($actualPath.Equals($expectedPath,[StringComparison]::OrdinalIgnoreCase) -or
        $actualPath.StartsWith($expectedPath+'\',[StringComparison]::OrdinalIgnoreCase) -or
        $expectedPath.StartsWith($actualPath+'\',[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Frozen oracle and observed output must be separate, non-overlapping trees.'
    }
    $wanted=Get-ReplayTree $expected
    $observed=Get-ReplayTree $actual
    $artifact=$null
    if($evidenceFile) {
        if($wanted.ContainsKey($evidenceFile)) { throw 'Evidence exclusion would hide a frozen expected file.' }
        $artifact=Read-ExecutionEvidence $actual $evidenceFile $statusProperty $successValue $observed
        [void]$observed.Remove($evidenceFile)
    }
    if($wanted.Count -eq 0 -or $observed.Count -ne $wanted.Count) { throw 'Frozen replay output file set differs.' }
    foreach($path in $wanted.Keys) {
        if(-not $observed.ContainsKey($path) -or $observed[$path] -cne $wanted[$path]) {
            throw "Frozen replay output differs: $path"
        }
    }
    [pscustomobject]@{ OutputSha256=(Get-ReplayTreeHash $observed); Files=$observed; ExecutionEvidence=$artifact }
}

function Assert-ReplayCheckpoint($binding,[string]$inputRoot,[string]$expectedRoot,$comparison) {
    if($binding.InputRevision -cnotmatch '^[0-9a-f]{40}$' -or $binding.ExpectedRevision -cnotmatch '^[0-9a-f]{40}$' -or
        $binding.InputSha256 -cnotmatch '^[0-9A-F]{64}$' -or $binding.ExpectedSha256 -cnotmatch '^[0-9A-F]{64}$') {
        throw 'Checkpoint requires exact frozen revisions and tree hashes.'
    }
    if((Get-ReplayTreeHash (Get-ReplayTree $inputRoot)) -cne $binding.InputSha256 -or
        (Get-ReplayTreeHash (Get-ReplayTree $expectedRoot)) -cne $binding.ExpectedSha256 -or
        $comparison.OutputSha256 -cne $binding.ExpectedSha256) {
        throw 'Frozen checkpoint reconciliation failed.'
    }
    [pscustomobject]@{
        InputRevision=$binding.InputRevision; ExpectedRevision=$binding.ExpectedRevision
        InputSha256=$binding.InputSha256; ExpectedSha256=$binding.ExpectedSha256
        BaselineCheckpointVerified=$true; FormalVerified=$false
        Scope='exact-predeclared-tree-reconciliation-not-historical-execution'
    }
}

function Assert-ReplayBehavior($specification,[int]$exitCode,[string]$stdout,[string]$stderr) {
    if($specification.ExpectedExitCode -isnot [long] -and $specification.ExpectedExitCode -isnot [int]) {
        throw 'Behavior contract requires an integer expected exit.'
    }
    if($specification.ExpectedStandardOutput -isnot [string] -or $specification.ExpectedStandardError -isnot [string] -or
        $exitCode -ne $specification.ExpectedExitCode -or $stdout -cne $specification.ExpectedStandardOutput -or
        $stderr -cne $specification.ExpectedStandardError) { throw 'Isolated observable behavior differs from the predeclared contract.' }
}
