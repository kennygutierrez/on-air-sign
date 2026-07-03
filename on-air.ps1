param(
    [int] $PollSecs = 8
)

# Credentials live in on-air.local.ps1 (gitignored) — copy from on-air.local.ps1.example.
$localConfig = Join-Path $PSScriptRoot "on-air.local.ps1"
if (Test-Path $localConfig) {
    . $localConfig
} else {
    Write-Error "Missing $localConfig — copy on-air.local.ps1.example and fill in your TP-Link account email/password."
    exit 1
}

#region ── Logging ───────────────────────────────────────────────────────────────
# The script runs hidden (no console), so without this there's no way to tell what
# it actually did after the fact — every "not working" report so far has needed
# forensic timestamp math instead of just reading what happened.
$script:LogFile = Join-Path $PSScriptRoot "on-air.log"
function Write-Log([string]$msg) {
    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $msg" | Add-Content -Path $script:LogFile
}
#endregion

#region ── TP-Link cloud-relay control ──────────────────────────────────────────
# Local KLAP control doesn't work against this account for either device tested
# (an EP10 Mini and an HS103) — the handshake completes but the challenge-response
# never matches, a known unresolved python-kasa/KLAP-v2 compatibility gap. The
# official Kasa app controls the plug fine, via TP-Link's cloud relay, so this does
# the same: log in once, look up the device by alias, then relay set_relay_state
# passthrough commands through TP-Link's cloud API. See on-air-sign-plan.md for
# the full investigation.
$script:CloudToken   = $null
$script:DeviceId     = $null
$script:AppServerUrl = $null

function Connect-KasaCloud {
    $guid = [guid]::NewGuid().ToString()
    $body = @{
        method = "login"
        params = @{
            appType       = "Kasa_Android"
            cloudUserName = $env:KASA_USERNAME
            cloudPassword = $env:KASA_PASSWORD
            terminalUUID  = $guid
        }
    } | ConvertTo-Json -Depth 5
    $resp = Invoke-RestMethod -Uri "https://wap.tplinkcloud.com" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 10
    if ($resp.error_code -ne 0) { throw "TP-Link cloud login failed: $($resp.msg)" }
    $script:CloudToken = $resp.result.token

    $listBody = @{ method = "getDeviceList"; params = @{} } | ConvertTo-Json -Depth 5
    $listResp = Invoke-RestMethod -Uri "https://wap.tplinkcloud.com?token=$($script:CloudToken)" -Method Post -Body $listBody -ContentType "application/json" -TimeoutSec 10
    $device = $listResp.result.deviceList | Where-Object { $_.alias -eq $env:KASA_ALIAS }
    if (-not $device) { throw "No TP-Link cloud device found with alias '$env:KASA_ALIAS'" }
    $script:DeviceId     = $device.deviceId
    $script:AppServerUrl = $device.appServerUrl
}

function Set-KasaPlug([bool]$on) {
    $inner = '{"system":{"set_relay_state":{"state":' + $(if ($on) { '1' } else { '0' }) + '}}}'
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        try {
            if (-not $script:CloudToken) { Connect-KasaCloud }
            $body = @{ method = "passthrough"; params = @{ deviceId = $script:DeviceId; requestData = $inner } } | ConvertTo-Json -Depth 5
            $resp = Invoke-RestMethod -Uri "$($script:AppServerUrl)?token=$($script:CloudToken)" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 10
            if ($resp.error_code -ne 0) { throw "cloud error: $($resp.msg)" }
            return
        } catch {
            Write-Log "Kasa cloud attempt $attempt failed: $_"
            $script:CloudToken = $null   # force re-login/re-lookup next attempt
        }
    }
    Write-Log "Kasa cloud control failed after retry — sign state not updated."
}
#endregion

#region ── Teams call detection via log file ────────────────────────────────────
function Find-TeamsLogs {
    # New Teams (Store/MSIX, 2022+) — main structured log is "MSTeams_<date>_<time>.<seq>.log",
    # not .txt. Sort by name (not LastWriteTime) since a freshly-rotated log can briefly share
    # the same LastWriteTime as the file it replaced, before any content has been appended.
    #
    # Returns the two most recent files (oldest first), not just the newest one. Teams rotates
    # every few minutes, and a state-change marker can land right at the boundary — e.g. "Call
    # ended" gets written to the outgoing file seconds before rotation, and if the next poll
    # only reads the brand-new (marker-less) file, that "ended" event is lost forever and the
    # sign gets stuck in its last state. Checking the previous file too closes that gap.
    foreach ($sub in 'LocalCache', 'LocalState') {
        $p = "$env:LOCALAPPDATA\Packages\MSTeams_8wekyb3d8bbwe\$sub\Microsoft\MSTeams\Logs"
        if (Test-Path $p) {
            $f = Get-ChildItem $p -Filter "MSTeams_*.log" | Sort-Object Name | Select-Object -Last 2
            if ($f) { return @($f.FullName) }
        }
    }
    # Classic Teams fallback
    $classic = "$env:APPDATA\Microsoft\Teams\logs.txt"
    if (Test-Path $classic) { return @($classic) }
    return @()
}

function Get-InCallFromLogs([string[]]$logPaths) {
    # Tail is large (not a small window like 400) because the log gets very noisy during an
    # active call — ~30 lines/sec measured from video-buffer rendering alone (StreamRenderer:
    # GetAvailableBufferOnWV2) — which at an 8s poll interval can push 200+ lines between checks
    # and risk scrolling the actual state-change line out of a smaller window.
    #
    # $logPaths is oldest-first, so concatenating tails preserves chronological order and
    # "last marker across all of them" is still the true last marker even across a rotation.
    $tail = @()
    foreach ($p in $logPaths) {
        $tail += Get-Content $p -Tail 5000 -ErrorAction SilentlyContinue
    }
    if (-not $tail) { return $null }

    # Calibrated 2026-07-03 against a real Teams test call. TeamsCallTracker is a dedicated
    # call-tracking component — these markers are non-ambiguous and paired 1:1 with the same
    # callId, e.g.:
    #   TeamsCallTracker: Call became active: <callId> (total: 1)
    #   TeamsCallTracker: Call ended: <callId> (remaining: 0)
    $onPattern  = 'TeamsCallTracker: Call became active'
    $offPattern = 'TeamsCallTracker: Call ended'

    $last = $tail | Where-Object { $_ -match $onPattern -or $_ -match $offPattern } |
            Select-Object -Last 1

    # $null (not $false) when neither file has any markers at all — the caller keeps the
    # last known state until a real marker shows up, rather than guessing "off".
    if (-not $last)  { return $null }
    return ($last -match $onPattern) -and ($last -notmatch $offPattern)
}
#endregion

#region ── Main loop ─────────────────────────────────────────────────────────────
Write-Log "Started. Plug=$env:KASA_ALIAS PollSecs=$PollSecs"

# Teams rotates its log file every few minutes, so the path is re-resolved every poll
# instead of once at startup — otherwise this ends up tailing a file Teams stopped
# writing to, silently going blind to new calls a few minutes after launch.
$wasInCall = $false
$lastLogSeen = $null
while ($true) {
    $logs = Find-TeamsLogs
    $newest = if ($logs.Count -gt 0) { $logs[-1] } else { $null }
    if ($newest -and $newest -ne $lastLogSeen) {
        Write-Log "Watching log: $newest"
        $lastLogSeen = $newest
    }
    if ($logs.Count -gt 0) {
        $inCall = Get-InCallFromLogs $logs
        if ($null -ne $inCall -and $inCall -ne $wasInCall) {
            Write-Log $(if ($inCall) { 'ON AIR' } else { 'off' })
            Set-KasaPlug -on $inCall
            $wasInCall = $inCall
        }
    } elseif ($lastLogSeen) {
        # Only log the transition into "no log", not every poll — Teams being
        # closed for hours shouldn't fill this file with repeats.
        Write-Log "No Teams log found — waiting for Teams to start."
        $lastLogSeen = $null
    }
    Start-Sleep -Seconds $PollSecs
}
#endregion
