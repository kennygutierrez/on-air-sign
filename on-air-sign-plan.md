# On-Air Door Sign — Plan

Detects active Microsoft Teams calls and toggles a physical "On Air" LED sign via a TP-Link Kasa smart plug.

## Hardware shopping list

| Item | Approx. cost |
|---|---|
| LED "On Air" sign (the kind streamers use) | ~$20 (Amazon) |
| TP-Link Kasa smart plug (EP10, EP25, or similar) | ~$15 |

## How it works

1. A PowerShell script (`on-air.ps1`) runs in the background on the PC.
2. Every 8 seconds it checks whether you're in a call — **Microsoft Teams** (from its log file) or **Zoom** (from its meeting processes). The sign is on whenever *either* says you're in a call.
3. On call start → turns the Kasa plug ON (sign lights up).
4. On call end → turns the Kasa plug OFF.
5. Plug control goes through **TP-Link's cloud API** (passthrough relay) — see below for why local control doesn't work here. Pure PowerShell, no Python/external CLI needed.

### Protocol investigation (why not local control)

The original plan assumed the old plaintext-XOR-over-TCP:9999 protocol (no auth, no cloud). That's not what showed up:

1. The first plug — a **Kasa Smart Wi-Fi Plug Mini (EP10)** — only had port 80 open, not 9999: it speaks TP-Link's newer **KLAP** local protocol, which needs the TP-Link account email+password to derive a local session key. Hand-rolling KLAP crypto in PowerShell wasn't worth it, so control was first routed through the `python-kasa` CLI (which implements KLAP).
2. `python-kasa` (0.10.2, latest as of 2026-07) rejected the account's confirmed-correct credentials with "Device response did not match our challenge" on **every** device tried — two EP10 Minis (including one re-paired from scratch) and a legacy **HS103** that had also been silently upgraded to KLAP by firmware. Two different device models failing identically ruled out a device-specific issue — the account password was confirmed valid at `id.tplinkcloud.com`, and the official Kasa app could control every device fine. This matches multiple open, unresolved `python-kasa` GitHub issues (e.g. [#1604](https://github.com/python-kasa/python-kasa/issues/1604)) about KLAP v2 challenge-response mismatches with correct credentials — a real library/protocol-version gap, not user error.
3. Since the Kasa app clearly reaches these devices some other way, the fix was to use the same path: **TP-Link's cloud API** (unofficial but widely reverse-engineered — `wap.tplinkcloud.com`). Login once with account email/password → `getDeviceList` to find the device by alias → `passthrough` relays the same legacy `set_relay_state` JSON payload through the cloud to the device. Verified working end-to-end (`error_code: 0`, plug visibly blinked on/off).

This drops the Python/`python-kasa` dependency entirely — `on-air.ps1` now only needs `Invoke-RestMethod`, built into PowerShell. Tradeoff versus the original "no cloud" goal: this needs internet at runtime and has ~1–2s of relay latency, same as the phone app.

## Credentials file

Copy `on-air.local.ps1.example` → `on-air.local.ps1` (gitignored — never commit this) and fill in:
```powershell
$env:KASA_USERNAME = "<your TP-Link account email>"
$env:KASA_PASSWORD = "<your TP-Link account password>"
$env:KASA_ALIAS    = "On Air Lamp"   # must match the device's name in the Kasa app
```
`on-air.ps1` dot-sources this file automatically if present.

## The script

The full script lives at `on-air.ps1` (kept as a file rather than inline here so it stays in sync). A polling loop tails the Teams log; `Set-KasaPlug` relays on/off through TP-Link's cloud.

## Setup steps

1. **Hardware/pairing.** Pair the plug in the Kasa app and give it an alias (this project uses `On Air Lamp`). Because control is via the cloud, the plug's LAN IP is not used.

2. **Set up credentials** — copy `on-air.local.ps1.example` to `on-air.local.ps1` and fill in the TP-Link account email/password + the device alias (see above).

3. **Log patterns** — calibrated against a real Teams test call:
   - **Log file discovery.** The new Teams client doesn't write `.txt` files — the real log is `MSTeams_<date>_<time>.<seq>.log` in the `Logs` folder.
   - **Call markers.** Guessed patterns like `InACall` / `callAccepted` never appear. The real, unambiguous markers come from a dedicated `TeamsCallTracker` component: `TeamsCallTracker: Call became active` (start) / `TeamsCallTracker: Call ended` (end) — paired 1:1 by callId.
   - **Tail window.** An active call is very noisy (~30 log lines/sec, mostly `StreamRenderer` video-buffer chatter), so the tail window is large (`Tail 5000`) to avoid scrolling the state-change line out of range between 8-second polls.

   Verified end-to-end: ran `on-air.ps1` in the background, started a real Teams test call, and watched it print `ON AIR` / `off` at the right moments with the physical sign blinking on/off in sync.

4. **Run the script:**
   ```powershell
   .\on-air.ps1
   ```

5. **Auto-start on login.** Try the scripted scheduled-task path first:
   ```powershell
   pwsh -NoProfile -File "<repo>\register-on-air-task.ps1" -ScriptPath "<repo>\on-air.ps1"
   ```
   (`register-on-air-task.ps1` wraps `Register-ScheduledTask` and takes the target script path as a parameter, so it's reusable on any machine.)

   **Endpoint security often blocks this** with "Access is denied" — from both `Register-ScheduledTask` and `schtasks.exe`. Many AV/EDR products block programmatic scheduled-task registration by default as an anti-persistence hardening rule (scheduled tasks created via script are a classic malware technique). This is expected security behavior, not a bug — don't try to bypass it. Two fallbacks that aren't blocked the same way:

   **Fallback A — Startup-folder shortcut (scriptable, no admin, no manual clicks).** A per-user Startup shortcut is a *different* persistence mechanism that endpoint security typically does **not** block (those rules target programmatic *scheduled-task* registration specifically), and it can be created entirely from PowerShell:
   ```powershell
   $pwsh   = (Get-Command pwsh).Source
   $script = "<full path to on-air.ps1 on this machine>"
   $lnk    = Join-Path ([Environment]::GetFolderPath('Startup')) 'OnAirLight.lnk'
   $sh = New-Object -ComObject WScript.Shell
   $s  = $sh.CreateShortcut($lnk)
   $s.TargetPath       = $pwsh
   $s.Arguments        = "-WindowStyle Hidden -NoProfile -File `"$script`""
   $s.WindowStyle      = 7   # 7 = minimized / no visible window
   $s.WorkingDirectory = Split-Path $script
   $s.Save()
   ```
   It runs on every login. To start it immediately without logging out, launch the shortcut *through Explorer* so it detaches from the current shell (a process spawned directly from a script dies with that script):
   ```powershell
   Start-Process explorer.exe "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\OnAirLight.lnk"
   ```

   **Fallback B — Task Scheduler GUI:**
   1. Win key → "Task Scheduler" (or Win+R → `taskschd.msc`)
   2. **Create Basic Task...**
   3. Name: `OnAirLight` → Next
   4. Trigger: **When I log on** → Next
   5. Action: **Start a program** → Next
   6. Program/script: `pwsh`
   7. Add arguments: `-WindowStyle Hidden -File "<full path to on-air.ps1>"`
   8. Finish

## Zoom detection

Zoom is handled differently from Teams. Its `%APPDATA%\Zoom\logs` folder is typically empty (no reliable, parseable call markers), so instead the script watches for the **meeting-only host processes** that Zoom spawns when you join a meeting:

- An **idle** Zoom client runs only `Zoom.exe`.
- **Joining a meeting** starts `CptHost.exe` (the conference/share host); `airhost.exe` / `aomhost64.exe` may also appear.
- As a second, independent signal, a window titled **"Zoom Meeting"** / **"Zoom Webinar"** exists only during a meeting.

`Get-ZoomInCall` returns `$true` if any of those host processes is running *or* a Zoom meeting window is present. This needs no credentials or config — it's purely local process inspection. The main loop ORs it with the Teams state: `inCall = teamsState -or zoomState`.

**Calibrated 2026-07-03 against a real meeting.** A live "New Meeting" was watched end-to-end: `CptHost.exe` and `aomhost64.exe` were present for the *entire* meeting and `Get-ZoomInCall` stayed `True` throughout; the watcher logged `ON AIR (teams=False zoom=True)` on join and `off` on leave, with the physical sign following in sync. Notably the **"Zoom Meeting" window title flickered** (some samples showed only "Zoom Workplace"), so the **process signal is the reliable one** — the window-title check is only a secondary fallback. Which host process appears may still vary by Zoom version; if a plain meeting ever fails to trigger, re-check with a probe (sample `Get-Process` for `zoom|CptHost|airhost|aomhost` while in a meeting) and adjust the process list.

## Setting up on another machine

The sign is controlled entirely through TP-Link's cloud, so any machine running Teams can drive it — it doesn't need to be on any particular network. Only `on-air.ps1` (plus your `on-air.local.ps1`) needs to exist there.

1. **Copy the files** (the git repo isn't required, just the files):
   - `on-air.ps1`
   - `on-air.local.ps1` — **not in git** (gitignored, contains the TP-Link password). Copy it securely, or recreate it from `on-air.local.ps1.example`.
2. **PowerShell 7 (`pwsh`)** must be installed — the script won't run under Windows PowerShell 5.1.
3. **Log path may differ.** `Find-TeamsLogs` assumes the new Teams MSIX client at `%LOCALAPPDATA%\Packages\MSTeams_8wekyb3d8bbwe\...`. A managed or classic Teams install could use a different path — if the script logs "No Teams log found," check what's installed and adjust `Find-TeamsLogs`.
4. **Corporate endpoint security is often stricter.** A managed machine may block scripted scheduled-task registration (see step 5), or may not allow arbitrary PowerShell scripts to run at all (execution policy, application allow-listing). If `pwsh -File on-air.ps1` won't run at all, that's a different failure than the Task Scheduler block and may need IT.
5. Same auto-start setup as step 5 above — if scheduled-task registration is blocked, use the Startup-folder shortcut.

## Upgrade path (if log parsing is unreliable)

Teams 2.0 has a **local WebSocket API** on `ws://localhost:8124` that emits structured JSON events (`isInMeeting: true/false`). Enable it under Teams Settings → Privacy → "Third-party app API". Replacing `Get-InCallFromLogs` with a WebSocket listener would remove all pattern-matching guesswork.
