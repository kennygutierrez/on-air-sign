# On-Air Door Sign

Turns a physical "On Air" LED sign on and off automatically based on whether you're in a **Microsoft Teams call or a Zoom meeting**. A background PowerShell script watches Teams (via its log) and Zoom (via its meeting processes) and toggles a TP-Link Kasa smart plug through TP-Link's cloud API — no Python or external CLI required. The sign is on whenever you're in *either* a Teams or Zoom call.

## Quick start

1. Buy an LED "On Air" sign (~$20) and a TP-Link Kasa smart plug (~$15). Pair the plug in the Kasa app and note its alias.
2. Copy the credentials template and fill it in:
   ```powershell
   Copy-Item on-air.local.ps1.example on-air.local.ps1
   ```
   Set your TP-Link account email/password and the plug's alias. **`on-air.local.ps1` is gitignored — never commit it.**
3. Run it:
   ```powershell
   pwsh -File .\on-air.ps1
   ```
4. For auto-start on login and the full design/troubleshooting write-up, see [on-air-sign-plan.md](on-air-sign-plan.md).

## Files

| File | Purpose |
|---|---|
| `on-air.ps1` | The watcher: polls the Teams log + Zoom meeting processes, toggles the plug |
| `on-air.local.ps1.example` | Credentials template (copy to `on-air.local.ps1`) |
| `register-on-air-task.ps1` | Helper to register a scheduled task for auto-start |
| `on-air-sign-plan.md` | Full plan, protocol investigation, and setup notes |

## Requirements

- Windows with **PowerShell 7** (`pwsh`) — will not run under Windows PowerShell 5.1
- Microsoft Teams (new / MSIX client) and/or the Zoom desktop client
- A TP-Link Kasa smart plug + a TP-Link cloud account

## Notes

- Control goes through TP-Link's cloud, so the PC doesn't need to be on the same network as the plug. It does need internet, and there's ~1–2s of relay latency (same as the Kasa phone app).
- No secrets are committed to this repo. Credentials live only in the gitignored `on-air.local.ps1`.
