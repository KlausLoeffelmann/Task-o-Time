$ErrorActionPreference = 'Stop'

function Get-CompilerFileHash([string]$path) {
    $item=Get-Item -LiteralPath $path -Force
    if ($item.PSIsContainer -or $item.Length -gt 268435456) { throw 'Invalid or oversized compiler input.' }
    for($current=$item; $current; $current=$current.Parent) {
        if($current.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Compiler input reparse point rejected.' }
        if($current -is [IO.FileInfo]) { $current=$current.Directory; if($current.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Compiler input reparse point rejected.' } }
    }
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}

function Resolve-CompilerPath([string]$path,[string]$base) {
    $path=$path.Replace('/','\')
    if($path -match '["\r\n@*?]' -or $path.StartsWith('\\') -or $path -match '^[A-Za-z]:[^\\]') {
        throw 'Unsupported compiler path syntax.'
    }
    $full=[IO.Path]::GetFullPath($(if([IO.Path]::IsPathRooted($path)) { $path } else { Join-Path $base $path }))
    if($full.Substring(2).Contains(':')) { throw 'Compiler alternate data stream rejected.' }
    return $full
}

# The compiler receives only these data-bearing arguments, never MSBuild, plugins,
# response files, signing providers, native resources, modules or arbitrary hooks.
function ConvertTo-CompilerArguments([string[]]$arguments,[string]$projectBase,[scriptblock]$inputFile,[scriptblock]$outputFile) {
    $result=New-Object 'Collections.Generic.List[string]'
    $sourceCount=0; $outCount=0; $deterministic=$false; $target=$false
    foreach($raw in $arguments) {
        if([string]::IsNullOrWhiteSpace($raw) -or $raw -match '[\x00-\x1f\x7f@]' -or $raw.Length -gt 8192) { throw 'Unsupported compiler argument syntax.' }
        $arg=$raw
        if($arg -eq '/features:"InterceptorsNamespaces=;Microsoft.Extensions.Validation.Generated"') {
            $arg='/features:InterceptorsNamespaces=;Microsoft.Extensions.Validation.Generated'
        }
        if($arg.Contains('"')) { throw 'Unsupported embedded compiler quoting.' }
        if($arg -match '^/(reference|resource|analyzerconfig):(.+)$') {
            $kind=$Matches[1]; $value=$Matches[2]; $suffix=''
            if($kind -eq 'resource') {
                $parts=$value.Split(',')
                if($parts.Count -gt 3 -or ($parts.Count -ge 2 -and $parts[1] -notmatch '^[A-Za-z0-9_.-]+$') -or
                    ($parts.Count -eq 3 -and $parts[2] -notin @('public','private'))) { throw 'Unsupported managed resource syntax.' }
                $value=$parts[0]; if($parts.Count -gt 1) { $suffix=','+($parts[1..($parts.Count-1)] -join ',') }
            }
            $path=Resolve-CompilerPath $value $projectBase
            & $inputFile $path $kind
            $result.Add('/'+$kind+':'+$path+$suffix)
        }
        elseif($arg -match '^/(out|refout|pdb):(.+)$') {
            $kind=$Matches[1]; $path=Resolve-CompilerPath $Matches[2] $projectBase
            if($kind -eq 'out') { $outCount++ }
            & $outputFile $path $kind
            $result.Add('/'+$kind+':'+$path)
        }
        elseif($arg.StartsWith('/')) {
            if($arg -notmatch '^/(noconfig|unsafe[-+]|checked[-+]|fullpaths|nostdlib\+|platform:AnyCPU|subsystemversion:6\.00|errorreport:(prompt|none)|warn:[0-9]+|nowarn:[A-Za-z0-9_,]+|define:[A-Za-z0-9_;]+|highentropyva[-+]|features:InterceptorsNamespaces=;Microsoft\.Extensions\.Validation\.Generated|debug\+|debug:portable|filealign:512|optimize[-+]|target:exe|warnaserror[-+]|warnaserror[-+]?:[A-Za-z0-9_,]+|utf8output|deterministic\+|langversion:[0-9.]+|nullable:(enable|disable|warnings|annotations))$') {
                throw "Unsupported compiler feature: $arg"
            }
            if($arg -eq '/deterministic+') { $deterministic=$true }
            if($arg -eq '/target:exe') { $target=$true }
            $result.Add($arg)
        }
        else {
            $path=Resolve-CompilerPath $arg $projectBase
            if([IO.Path]::GetExtension($path) -ine '.cs') { throw 'Only explicit C# source files are supported.' }
            & $inputFile $path 'source'
            $sourceCount++; $result.Add($path)
        }
    }
    if($sourceCount -eq 0 -or $outCount -ne 1 -or -not $deterministic -or -not $target) {
        throw 'Protected compiler requires C# sources, one output, deterministic compilation and a console executable.'
    }
    return ,$result.ToArray()
}

function New-CompilerPlan([object]$build,[string]$project,[string]$sdk,[string]$packages,[string]$destination,[string]$framework) {
    if(Test-Path -LiteralPath $destination) { throw 'Compiler capture requires a fresh destination.' }
    $guestRoot='C:\ProbeWork\restricted\producer'
    $export=$build.HostProducerObservation.Export
    if(-not $export -or $build.FormalVerified -or $build.SandboxStopped -ne $true -or $build.SdkVersion -cne '10.0.401') {
        throw 'Expected pinned-SDK build observation with independently observed VM termination.'
    }
    $records=@($build.Producer.Projects | Where-Object Project -CEQ $project)
    if($records.Count -ne 1 -or $records[0].ExitCode -ne 0) { throw 'No unique successful build record.' }
    $metadata=$records[0].StandardOutput | ConvertFrom-Json
    if(@($metadata.Items.VbcCommandLineArgs).Count -ne 0) { throw 'VB compiler capture is not implemented.' }
    $base=Split-Path (Join-Path $guestRoot $project) -Parent
    $files=New-Object 'Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    $outputs=New-Object 'Collections.Generic.SortedDictionary[string,string]' ([StringComparer]::Ordinal)
    $omitted=New-Object 'Collections.Generic.List[string]'
    $originalSources=Join-Path $build.Artifacts 'payload\source'
    $sourceBindings=New-Object 'Collections.Generic.SortedDictionary[string,string]' ([StringComparer]::Ordinal)
    foreach($file in Get-ChildItem -LiteralPath $originalSources -Recurse -File -Force) {
        $sourceBindings[$file.FullName.Substring($originalSources.Length).TrimStart('\')]=Get-CompilerFileHash $file.FullName
    }
    function Bind-Input([string]$path,[string]$kind) {
        $relative=$null; $from=$null; $origin=$null
        if($path.StartsWith($guestRoot+'\',[StringComparison]::OrdinalIgnoreCase)) {
            if($kind -eq 'reference') { throw 'Submitted project/local assembly references require a separate protected dependency compilation.' }
            $relative=$path.Substring($guestRoot.Length).TrimStart('\')
            $from=Join-Path $export $relative
            $origin=$(if($sourceBindings.ContainsKey($relative)) { 'authored' } else { 'generated-build-input' })
            if($origin -eq 'generated-build-input' -and $relative -notmatch '(^|\\)obj\\') { throw 'Unbound generated compiler input outside obj.' }
            $hash=Get-CompilerFileHash $from
            if($sourceBindings.ContainsKey($relative) -and $sourceBindings[$relative] -ne $hash) { throw 'Authored compiler input changed.' }
            $copy=Join-Path $destination ('files\producer\'+$relative)
        }
        elseif($path.StartsWith('C:\PublicSdk\',[StringComparison]::OrdinalIgnoreCase)) {
            $relative=$path.Substring('C:\PublicSdk\'.Length)
            if($kind -notin @('reference','analyzerconfig') -or $relative -notmatch '^(packs\\Microsoft\.NETCore\.App\.Ref\\[^\\]+\\ref\\net10\.0\\[^\\]+\.dll|sdk\\10\.0\.401\\Sdks\\Microsoft\.NET\.Sdk\\analyzers\\build\\config\\[^\\]+\.globalconfig)$') {
                throw 'Compiler SDK input is outside the supported reference/configuration surface.'
            }
            $from=Join-Path $sdk $relative; $hash=Get-CompilerFileHash $from; $copy=$null; $origin='readonly-sdk'
        }
        elseif($path.StartsWith('C:\PublicFrameworkReferences\',[StringComparison]::OrdinalIgnoreCase)) {
            $relative=$path.Substring('C:\PublicFrameworkReferences\'.Length)
            if(-not $framework -or $kind -ne 'reference' -or
                $relative -notmatch '^\.NETFramework\\v4\.7\.2\\(Facades\\)?[^\\]+\.dll$') {
                throw 'Unsupported Framework compiler reference.'
            }
            $from=Join-Path $framework $relative; $hash=Get-CompilerFileHash $from
            $copy=Join-Path $destination ('files\framework\'+$relative); $origin='readonly-framework472'
        }
        elseif($path.StartsWith('C:\ProbeWork\restricted\packages\',[StringComparison]::OrdinalIgnoreCase) -or
               $path.StartsWith('C:\PublicPackages\',[StringComparison]::OrdinalIgnoreCase)) {
            if($kind -ne 'reference' -or -not $packages) { throw 'Only approved package assembly references are supported.' }
            $prefix=$(if($path.StartsWith('C:\PublicPackages\',[StringComparison]::OrdinalIgnoreCase)) { 'C:\PublicPackages\' } else { 'C:\ProbeWork\restricted\packages\' })
            $relative=$path.Substring($prefix.Length)
            $from=Join-Path $packages $relative; $hash=Get-CompilerFileHash $from
            $returned=Join-Path $export ('_compiler-references\'+$relative)
            if((Get-CompilerFileHash $returned) -ne $hash) { throw 'Build used package metadata differing from approved package bytes.' }
            $copy=Join-Path $destination ('files\packages\'+$relative); $origin='approved-package'
        }
        else { throw "Compiler input escapes approved roots: $path" }
        if($files.ContainsKey($path)) { return }
        if($copy) {
            New-Item -ItemType Directory -Path (Split-Path $copy -Parent) -Force | Out-Null
            Copy-Item -LiteralPath $from -Destination $copy
        }
        $files[$path]=@{ Sha256=$hash; Kind=$kind; Origin=$origin }
    }
    function Bind-Output([string]$path,[string]$kind) {
        if(-not $path.StartsWith($base+'\obj\',[StringComparison]::OrdinalIgnoreCase) -or
            ([IO.Path]::GetExtension($path) -notin @('.dll','.pdb') -and
                -not ($framework -and [IO.Path]::GetExtension($path) -eq '.exe')) -or $outputs.ContainsKey($path)) {
            throw 'Compiler output must be a unique managed artifact inside the project obj directory.'
        }
        $outputs[$path]=$kind
    }
    $args=New-Object 'Collections.Generic.List[string]'
    foreach($item in $metadata.Items.CscCommandLineArgs) {
        $arg=[string]$item.Identity
        if($arg.StartsWith('/analyzer:',[StringComparison]::OrdinalIgnoreCase)) {
            # Plugins are never loaded in the second VM. Their generated sources must
            # instead be captured explicitly below, and byte comparison must still pass.
            $omitted.Add($arg); continue
        }
        if($arg.StartsWith('/generatedfilesout:',[StringComparison]::OrdinalIgnoreCase)) { continue }
        $args.Add($arg)
    }
    $generated=Join-Path $export (([IO.Path]::GetRelativePath($guestRoot,$base))+'\obj\protected-generated')
    if(Test-Path -LiteralPath $generated) {
        foreach($file in Get-ChildItem -LiteralPath $generated -Recurse -File -Force) {
            if($file.Extension -ine '.cs') { throw 'Unsupported generator output.' }
            $args.Add((Join-Path $guestRoot $file.FullName.Substring($export.Length).TrimStart('\')))
        }
    }
    $safe=ConvertTo-CompilerArguments $args.ToArray() $base ${function:Bind-Input} ${function:Bind-Output}
    foreach($path in $outputs.Keys) { if($files.ContainsKey($path)) { throw 'Compiler input/output collision.' } }
    $compilerFiles=New-Object 'Collections.Generic.SortedDictionary[string,string]' ([StringComparer]::Ordinal)
    foreach($file in Get-ChildItem -LiteralPath (Join-Path $sdk 'sdk\10.0.401\Roslyn\bincore') -File) {
        $compilerFiles[$file.Name]=Get-CompilerFileHash $file.FullName
    }
    $plan=[ordered]@{
        Kind='compile'; Protocol='protected-csc-v1'; Challenge=[guid]::NewGuid().ToString('N')
        Project=$project; WorkingDirectory=$base; Arguments=$safe; Inputs=$files; Outputs=$outputs
        AuthoredSourceFiles=$sourceBindings; OmittedBuildPlugins=$omitted.ToArray()
        BuildArtifacts=$build.Artifacts; SdkVersion='10.0.401'
        BuildSandboxStopped=$build.SandboxStopped; BuildSandboxProcessIds=$build.SandboxProcessIds
        CompilerSha256=(Get-CompilerFileHash (Join-Path $sdk 'sdk\10.0.401\Roslyn\bincore\csc.dll'))
        CompilerFiles=$compilerFiles
        ReferenceSurface=$(if($framework) { 'framework472-console' } else { 'net10-console' })
    }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $plan | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $destination 'job.json') -Encoding UTF8
    return $plan
}

function Assert-CompilerPlan([string]$root,[string]$sdk) {
    $plan=Get-Content -LiteralPath (Join-Path $root 'job.json') -Raw | ConvertFrom-Json
    if($plan.Protocol -cne 'protected-csc-v1' -or $plan.Kind -cne 'compile' -or $plan.SdkVersion -cne '10.0.401' -or
       $plan.BuildSandboxStopped -ne $true -or @($plan.BuildSandboxProcessIds).Count -lt 2 -or
       $plan.Challenge -cnotmatch '^[0-9a-f]{32}$' -or
       $plan.CompilerSha256 -cne (Get-CompilerFileHash (Join-Path $sdk 'sdk\10.0.401\Roslyn\bincore\csc.dll'))) {
        throw 'Invalid protected compiler plan or pinned compiler.'
    }
    $compilerFiles=@(Get-ChildItem -LiteralPath (Join-Path $sdk 'sdk\10.0.401\Roslyn\bincore') -File)
    if($compilerFiles.Count -ne @($plan.CompilerFiles.PSObject.Properties).Count) { throw 'Pinned compiler closure differs.' }
    foreach($file in $compilerFiles) {
        if($plan.CompilerFiles.PSObject.Properties[$file.Name].Value -cne (Get-CompilerFileHash $file.FullName)) {
            throw 'Pinned compiler closure hash differs.'
        }
    }
    $base=Resolve-CompilerPath $plan.WorkingDirectory 'C:\ProbeWork\restricted\producer'
    if($base -ne 'C:\ProbeWork\restricted\producer' -and -not $base.StartsWith('C:\ProbeWork\restricted\producer\',[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Compiler working directory escapes source root.'
    }
    $seen=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    function Check-Input([string]$path,[string]$kind) {
        $record=$plan.Inputs.PSObject.Properties[$path]
        if(-not $record -or $record.Value.Kind -cne $kind) { throw 'Unbound compiler input.' }
        if($path.StartsWith('C:\ProbeWork\restricted\producer\',[StringComparison]::OrdinalIgnoreCase)) {
            $file=Join-Path $root ('files\producer\'+$path.Substring('C:\ProbeWork\restricted\producer\'.Length))
        } elseif($path.StartsWith('C:\ProbeWork\restricted\packages\',[StringComparison]::OrdinalIgnoreCase)) {
            $file=Join-Path $root ('files\packages\'+$path.Substring('C:\ProbeWork\restricted\packages\'.Length))
        } elseif($path.StartsWith('C:\PublicPackages\',[StringComparison]::OrdinalIgnoreCase)) {
            $file=Join-Path $root ('files\packages\'+$path.Substring('C:\PublicPackages\'.Length))
        } elseif($path.StartsWith('C:\PublicSdk\',[StringComparison]::OrdinalIgnoreCase)) {
            $file=Join-Path $sdk $path.Substring('C:\PublicSdk\'.Length)
        } elseif($path.StartsWith('C:\PublicFrameworkReferences\',[StringComparison]::OrdinalIgnoreCase)) {
            $relative=$path.Substring('C:\PublicFrameworkReferences\'.Length)
            if($plan.ReferenceSurface -cne 'framework472-console' -or $kind -ne 'reference' -or
                $relative -notmatch '^\.NETFramework\\v4\.7\.2\\(Facades\\)?[^\\]+\.dll$') {
                throw 'Unsupported captured Framework reference.'
            }
            $file=Join-Path $root ('files\framework\'+$relative)
        } else { throw 'Unapproved compiler input root.' }
        if((Get-CompilerFileHash $file) -cne $record.Value.Sha256) { throw 'Compiler snapshot hash mismatch.' }
        [void]$seen.Add($path)
    }
    $seenOutputs=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    function Check-Output([string]$path,[string]$kind) {
        if(-not $path.StartsWith($base+'\obj\',[StringComparison]::OrdinalIgnoreCase) -or
           ([IO.Path]::GetExtension($path) -notin @('.dll','.pdb') -and
                -not ($plan.ReferenceSurface -ceq 'framework472-console' -and [IO.Path]::GetExtension($path) -eq '.exe')) -or
           $plan.Outputs.PSObject.Properties[$path].Value -cne $kind -or -not $seenOutputs.Add($path)) {
            throw 'Unbound or escaping compiler output.'
        }
    }
    $checked=ConvertTo-CompilerArguments @($plan.Arguments) $base ${function:Check-Input} ${function:Check-Output}
    if($seen.Count -ne @($plan.Inputs.PSObject.Properties).Count -or $seenOutputs.Count -ne @($plan.Outputs.PSObject.Properties).Count) {
        throw 'Unconsumed compiler plan binding.'
    }
    foreach($path in $seenOutputs) { if($seen.Contains($path)) { throw 'Compiler input/output collision.' } }
    return $plan
}
