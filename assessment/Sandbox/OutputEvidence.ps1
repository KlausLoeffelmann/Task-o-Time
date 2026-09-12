function Read-ExecutionEvidence(
    [string]$Directory, [string]$FileName, [string]$StatusProperty,
    [string]$SuccessValue, $Snapshot
) {
    $matches=@($Snapshot.Keys | Where-Object { $_ -ieq $FileName })
    if($matches.Count -ne 1) { throw 'Missing or ambiguous declared execution evidence.' }
    $manifest=[Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText((Join-Path $Directory $matches[0])))
    try {
        if($manifest.RootElement.ValueKind -ne [Text.Json.JsonValueKind]::Object) { throw 'Evidence must be a JSON object.' }
        $status=@($manifest.RootElement.EnumerateObject() | Where-Object { $_.Name -ceq $StatusProperty })
        if($status.Count -ne 1 -or $status[0].Value.ValueKind -ne [Text.Json.JsonValueKind]::String -or
            $status[0].Value.GetString() -cne $SuccessValue) { throw 'Declared execution evidence status failed.' }
        return @{
            FileName=$FileName; Sha256=$Snapshot[$matches[0]]; Status=$status[0].Value.GetString()
        }
    }
    finally { $manifest.Dispose() }
}
