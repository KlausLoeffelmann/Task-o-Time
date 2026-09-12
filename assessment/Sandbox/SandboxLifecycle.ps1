function Get-OwnedSandboxRemoteSession([string]$Configuration) {
    @(Get-CimInstance Win32_Process -Filter "Name='WindowsSandboxRemoteSession.exe'" |
        Where-Object { $_.CommandLine -and $_.CommandLine.EndsWith(' "'+$Configuration+'"',[StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue })
}

function Close-OwnedSandboxSession(
    [string]$Configuration,
    $Launcher,
    [object[]]$RemoteSessions,
    [object[]]$Servers,
    [string]$EvidencePath,
    [string]$OriginalFailure,
    [bool]$RequireServerObservation=$false,
    [ValidateRange(1,60)][int]$TimeoutSeconds=60
) {
    $deadline=[DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $report=[ordered]@{
        Scope='owned-sandbox-failure-cleanup'; Configuration=$Configuration
        OriginalFailure=$OriginalFailure; StoppedClientIds=@(); ObservedServerIds=@($Servers | ForEach-Object Id)
        SandboxStopped=$false; FormalVerified=$false
    }
    try {
        $current=@(Get-OwnedSandboxRemoteSession $Configuration)
        if($RemoteSessions.Count -gt 0) {
            $captured=@($RemoteSessions | ForEach-Object Id)
            $current=@($current | Where-Object { $_.Id -in $captured })
        }
        foreach($client in $current) {
            if(-not $client.HasExited) {
                Stop-Process -Id $client.Id -ErrorAction SilentlyContinue
                $report.StoppedClientIds+=,$client.Id
            }
        }
        if($Launcher -and -not $Launcher.HasExited) { Stop-Process -Id $Launcher.Id -ErrorAction SilentlyContinue }
        if($RequireServerObservation -and $Servers.Count -eq 0) { throw 'Cannot verify post-bootstrap VM termination without an observed server.' }
        $observed=@($RemoteSessions)+$current+@($Servers)
        if($Launcher) { $observed+=,$Launcher }
        foreach($process in $observed) {
            if($process.HasExited) { continue }
            $remaining=[int][Math]::Max(0,($deadline-[DateTime]::UtcNow).TotalMilliseconds)
            if($remaining -eq 0 -or -not $process.WaitForExit($remaining)) {
                throw "Observed process $($process.Id) did not terminate after closing the configuration-bound client."
            }
        }
        $report.SandboxStopped=$true
    }
    catch { $report.CleanupError=$_.Exception.Message; throw }
    finally {
        $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $EvidencePath -Encoding UTF8
    }
}
