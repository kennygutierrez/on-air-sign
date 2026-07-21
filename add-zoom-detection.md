# Handoff → HOME PC: add Zoom meeting detection to the tray version

## Context

The **work PC** and the **home PC** built the on-air sign in two divergent directions:

- **Home PC** added the **system-tray icon** (Force On / Force Off / Auto / View Log /
  Exit) — but detects **Teams only**.
- **Work PC** added **Zoom meeting detection** (`Get-ZoomInCall`), calibrated against a
  live meeting (git commit `e78b0b7`) — but on the older headless loop.

On 2026-07-21 the work PC **merged** the two: the tray version now also lights the sign
for Zoom meetings. This note brings that Zoom capability to the home PC's tray copy.

The home PC session has no memory of the work-PC merge — everything needed is below.
(Note: unlike the original tray handoff, **this change has no external URLs**, so there's
no Gmail link-rewrite mangling to watch for when this doc is emailed/pasted.)

---

## Path A — take the merged file wholesale (simplest)

If the home PC can reach the same repo the work PC uses
(`github.com/kennygutierrez/on-air-sign`, branch `optimizely-sales-bell`, commit
`d6ac73f`), the work PC's `on-air.ps1` is already a **strict superset** of the home
PC's tray version — same tray code, plus Zoom. Just:

1. `git fetch && git checkout optimizely-sales-bell -- on-air.ps1` (or copy that file over).
2. Do the **Zoom calibration** and **verification** steps at the bottom — the only
   machine-specific part.
3. Leave `on-air.local.ps1` (credentials) and the `-STA` launch untouched — both already
   correct on the home PC.

If the home PC's tray copy has diverged in ways you want to keep, use Path B instead.

---

## Path B — apply the four edits by hand

All edits are against the home PC's current **tray** `on-air.ps1` (the Teams-only one).

### Edit 1 — add the Zoom detection function

Paste this as a new region, right after the `#region ── Teams call detection …`
region ends (i.e. after `Get-InCallFromLogs`):

```powershell
#region ── Zoom meeting detection via host processes ─────────────────────────────
# Zoom doesn't reliably write a parseable log (%APPDATA%\Zoom\logs is usually empty),
# so detect a meeting by its dedicated host processes, which run only WHILE a meeting
# is active. Idle Zoom = just "Zoom.exe"; joining a meeting spawns CptHost.exe. A window
# titled "Zoom Meeting"/"Zoom Webinar" is an independent second confirmation. Either
# signal => in a meeting; no creds needed. NOTE: which host process appears for a plain
# meeting can vary by Zoom version — CALIBRATE against a real meeting on THIS PC (see the
# calibration step below).
function Get-ZoomInCall {
    if (Get-Process -Name 'CptHost','airhost','aomhost64' -ErrorAction SilentlyContinue) { return $true }
    $win = Get-Process -Name 'Zoom' -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowTitle -match '^Zoom (Meeting|Webinar)' }
    return [bool]$win
}
#endregion
```

### Edit 2 — add two state variables

Find the block that initializes the tray state (near `$script:AutoState = $false`) and
add these two lines next to `$script:LastLogSeen = $null`:

```powershell
$script:TeamsState = $false   # last known Teams state (log markers can scroll out => $null = keep)
$script:ZoomState  = $false   # last known Zoom state (process-based, always definite)
```

### Edit 3 — replace the timer tick so it ORs Teams and Zoom

Replace the whole `$timer.Add_Tick({ … })` body.

**Before (Teams-only):**

```powershell
$timer.Add_Tick({
    $logs = Find-TeamsLogs
    $newest = if ($logs.Count -gt 0) { $logs[-1] } else { $null }
    if ($newest -and $newest -ne $script:LastLogSeen) {
        Write-Log "Watching log: $newest"
        $script:LastLogSeen = $newest
    }
    if ($logs.Count -gt 0) {
        $inCall = Get-InCallFromLogs $logs
        if ($null -ne $inCall) { $script:AutoState = $inCall }
    } elseif ($script:LastLogSeen) {
        Write-Log "No Teams log found — waiting for Teams to start."
        $script:LastLogSeen = $null
    }
    Set-EffectiveState
})
```

**After (Teams OR Zoom):**

```powershell
$timer.Add_Tick({
    # --- Teams (log-based) ---
    $logs = Find-TeamsLogs
    $newest = if ($logs.Count -gt 0) { $logs[-1] } else { $null }
    if ($newest -and $newest -ne $script:LastLogSeen) {
        Write-Log "Watching Teams log: $newest"
        $script:LastLogSeen = $newest
    }
    if ($logs.Count -gt 0) {
        $t = Get-InCallFromLogs $logs
        if ($null -ne $t) { $script:TeamsState = $t }   # $null = keep last state (marker scrolled out of tail)
    } elseif ($script:LastLogSeen) {
        # Teams fully closed (no log at all) — can't be in a Teams call.
        Write-Log "No Teams log found — waiting for Teams to start."
        $script:LastLogSeen = $null
        $script:TeamsState  = $false
    }

    # --- Zoom (process-based) ---
    $script:ZoomState = Get-ZoomInCall

    # --- Combine: sign ON (in Auto) if in a Teams call OR a Zoom meeting ---
    $script:AutoState = $script:TeamsState -or $script:ZoomState
    Set-EffectiveState
})
```

### Edit 4 (optional, recommended) — log which signal drove the change

This mirrors the old headless log line (`ON AIR (teams=True zoom=False)`) so "why did
the sign turn on" stays answerable. Replace `Set-EffectiveState`:

```powershell
function Set-EffectiveState {
    $effective = if ($null -ne $script:Override) { $script:Override } else { $script:AutoState }
    if ($effective -ne $script:PlugState) {
        # In Auto, log the teams/zoom breakdown; manual overrides log their own reason line.
        if ($null -eq $script:Override) {
            Write-Log ("{0}  (teams={1} zoom={2})" -f $(if ($effective) { 'ON AIR' } else { 'off' }), $script:TeamsState, $script:ZoomState)
        } else {
            Write-Log $(if ($effective) { 'ON AIR' } else { 'off' })
        }
        Set-KasaPlug -on $effective
        $script:PlugState = $effective
    }
    Update-TrayUI
}
```

### Edit 5 (optional, cosmetic) — menu wording

The menu still says "Teams detection". If you like, update the label and the Auto-click
log line to mention Zoom:

```powershell
$itemAuto = $menu.Items.Add("Auto (resume Teams/Zoom detection)")
```
```powershell
# inside $itemAuto.Add_Click({ ... })
Write-Log "Manual override cleared - resuming Auto (Teams/Zoom detection)"
```

---

## Calibrate Zoom detection on the HOME PC (do NOT skip)

The host-process name for a plain meeting varies by Zoom build, so the process list in
`Get-ZoomInCall` must be confirmed on *this* machine. During a **real Zoom meeting** on
the home PC, run:

```powershell
Get-Process | Where-Object { $_.ProcessName -match 'Zoom|CptHost|airhost|aomhost|CpHost' } |
    Select-Object ProcessName, MainWindowTitle
```

- If you see `CptHost` (or `airhost`/`aomhost64`) → you're covered, no change needed.
- If the host process has a different name on this PC → add that name to the
  `Get-Process -Name '…'` list in `Get-ZoomInCall`.
- Confirm the Zoom window title starts with `Zoom Meeting` or `Zoom Webinar` (the
  second, independent signal). If your locale/version differs, widen the regex.

(On the work PC `CptHost.exe` was the reliable signal — verified live, commit `e78b0b7`.)

---

## Verify

1. Restart the tray process (Exit from the tray menu, then relaunch the Startup shortcut
   — it already carries `-STA`).
2. `on-air.log` shows a fresh `Started (tray mode). Plug=<alias> PollSecs=8`.
3. **Zoom:** join a meeting → within ~8s the log shows `ON AIR  (teams=False zoom=True)`,
   the tray icon goes red, and the physical sign lights. Leave → back to `off`.
4. **Teams:** confirm a Teams call still lights the sign (existing behavior, unchanged).
5. **Override still works:** Force On / Force Off hold regardless of Teams/Zoom; Auto
   resumes detection.
