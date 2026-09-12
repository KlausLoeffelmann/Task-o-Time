$ErrorActionPreference='Stop'

function Assert-UniqueJsonProperties([Text.Json.JsonElement]$element) {
    if($element.ValueKind -eq [Text.Json.JsonValueKind]::Object) {
        $names=New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
        foreach($property in $element.EnumerateObject()) {
            if(-not $names.Add($property.Name)) { throw 'Duplicate diagnostic JSON property.' }
            Assert-UniqueJsonProperties $property.Value
        }
    } elseif($element.ValueKind -eq [Text.Json.JsonValueKind]::Array) {
        foreach($item in $element.EnumerateArray()) { Assert-UniqueJsonProperties $item }
    }
}

function Read-UnsupportedContract([string]$path,[string]$name,[string]$inputHash) {
    if((Get-Item -LiteralPath $path).Length -gt 65536) { throw 'Unsupported contract exceeds its size limit.' }
    $document=[Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($path))
    try {
        if($document.RootElement.ValueKind -ne [Text.Json.JsonValueKind]::Object) { throw 'Unsupported contract must be a JSON object.' }
        Assert-UniqueJsonProperties $document.RootElement
        $contract=$document.RootElement.GetRawText() | ConvertFrom-Json
    } finally { $document.Dispose() }
    if($contract.Name -cne $name -or $contract.InputSha256 -cne $inputHash -or
        $inputHash -cnotmatch '^[0-9A-F]{64}$' -or
        ($contract.ExpectedExitCode -isnot [int] -and $contract.ExpectedExitCode -isnot [long]) -or
        $contract.ExpectedExitCode -lt 1 -or $contract.ExpectedExitCode -gt 255 -or $contract.ExpectedExitCode -eq 124 -or
        $contract.DiagnosticCode -cnotmatch '^[A-Za-z][A-Za-z0-9_-]+$' -or
        $contract.ExpectedStandardError -isnot [string]) {
        throw 'Unsupported contract must bind the case/input, exact nonzero exit and specific diagnostic.'
    }
    if($contract.Format -ceq 'exact-streams') {
        if($contract.ExpectedStandardOutput -isnot [string]) { throw 'Exact unsupported streams are required.' }
        $pattern='\A'+[regex]::Escape($contract.DiagnosticCode)+': [^\r\n]+(?:\r?\n)?\z'
        if(-not (($contract.ExpectedStandardOutput -cmatch $pattern -and $contract.ExpectedStandardError -ceq '') -or
            ($contract.ExpectedStandardError -cmatch $pattern -and $contract.ExpectedStandardOutput -ceq ''))) {
            throw 'Unsupported text contract requires one exact code-prefixed diagnostic and an empty other stream.'
        }
    } elseif($contract.Format -ceq 'json-diagnostics') {
        if($contract.ExpectedStandardError -cne '' -or
            $contract.StatusProperty -isnot [string] -or $contract.StatusValue -isnot [string] -or
            $contract.StatusProperty -cnotmatch '^[A-Za-z][A-Za-z0-9_]+$' -or
            [string]::IsNullOrWhiteSpace($contract.StatusValue) -or
            $contract.ExpectedDiagnostic.Code -cne $contract.DiagnosticCode -or
            $contract.ExpectedDiagnostic.Severity -cne 'error' -or
            [string]::IsNullOrWhiteSpace($contract.ExpectedDiagnostic.Project) -or
            [string]::IsNullOrWhiteSpace($contract.ExpectedDiagnostic.Message) -or
            @($contract.ExpectedDiagnostic.PSObject.Properties).Count -ne 4 -or
            @($contract.ExpectedDiagnostic.PSObject.Properties | Where-Object { $_.Name -cnotin @('Severity','Code','Project','Message') }).Count -gt 0 -or
            @($contract.AllowedProperties).Count -eq 0 -or
            $contract.StatusProperty -cnotin $contract.AllowedProperties -or
            'Diagnostics' -cnotin $contract.AllowedProperties) {
            throw 'Unsupported JSON contract requires an exact status and one complete case-specific error diagnostic.'
        }
    } else { throw 'Unsupported diagnostic contract format.' }
    return $contract
}

function Assert-UnsupportedEvidence($contract,[int]$exitCode,[string]$stdout,[string]$stderr,[bool]$hasOutput) {
    if($exitCode -ne $contract.ExpectedExitCode -or $hasOutput -or $stderr -cne $contract.ExpectedStandardError) {
        throw 'Unsupported case exit, stderr or no-output contract failed.'
    }
    if($contract.Format -ceq 'exact-streams') {
        if($stdout -cne $contract.ExpectedStandardOutput) { throw 'Unsupported case exact diagnostic differs.' }
    } elseif($contract.Format -ceq 'json-diagnostics') {
        if([Text.Encoding]::UTF8.GetByteCount($stdout) -gt 1048576) { throw 'Unsupported diagnostic JSON exceeds its limit.' }
        $document=[Text.Json.JsonDocument]::Parse($stdout)
        try {
            Assert-UniqueJsonProperties $document.RootElement
            if($document.RootElement.ValueKind -ne [Text.Json.JsonValueKind]::Object) { throw 'Unsupported diagnostic must be a JSON object.' }
            foreach($property in $document.RootElement.EnumerateObject()) {
                if($property.Name -cnotin $contract.AllowedProperties) { throw 'Unexpected unsupported diagnostic property.' }
            }
            $status=$document.RootElement.GetProperty($contract.StatusProperty)
            if($status.ValueKind -ne [Text.Json.JsonValueKind]::String -or $status.GetString() -cne $contract.StatusValue) {
                throw 'Unsupported diagnostic status differs.'
            }
            $diagnostics=$document.RootElement.GetProperty('Diagnostics')
            if($diagnostics.ValueKind -ne [Text.Json.JsonValueKind]::Array -or $diagnostics.GetArrayLength() -ne 1) {
                throw 'Unsupported case must report exactly the expected diagnostic, without infrastructure errors.'
            }
            $diagnostic=$diagnostics[0]
            if($diagnostic.ValueKind -ne [Text.Json.JsonValueKind]::Object -or
                @($diagnostic.EnumerateObject()).Count -ne 4) { throw 'Unsupported diagnostic schema differs.' }
            foreach($name in @('Severity','Code','Project','Message')) {
                $value=$diagnostic.GetProperty($name)
                if($value.ValueKind -ne [Text.Json.JsonValueKind]::String -or
                    $value.GetString() -cne $contract.ExpectedDiagnostic.$name) { throw 'Unsupported diagnostic does not match this case.' }
            }
        } finally { $document.Dispose() }
    } else { throw 'Unsupported diagnostic contract format.' }
}
