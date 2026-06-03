# Installation

## Requirements
- Windows 10 / Server 2016+ (x64)
- Siemens **S7-PLCSIM Advanced** (tested with V20) — the installer finds its API DLL automatically
- .NET Framework 4.x (already on modern Windows)

## Install
1. Download the project (green **Code → Download ZIP**; the prebuilt `PlcWebControl.exe` is included)
   and extract it (e.g. `C:\PLCSIM-WebControl`).
2. **PowerShell → Run as administrator**, `cd` into that folder, and run:
   ```powershell
   .\scripts\install.ps1
   ```
   It builds the app, finds the PLCSIM DLL, opens the LAN port (default), creates `appconfig.txt`, and
   registers an always-on task **"PLCSIM WebControl"**. Add `-LocalOnly` to bind to localhost.
3. Open **http://localhost:8090** (or `http://<this-machine-ip>:8090` from another machine).

Manage it:
```powershell
Start-ScheduledTask -TaskName "PLCSIM WebControl"
Stop-ScheduledTask  -TaskName "PLCSIM WebControl"
Get-Content .\webcontrol.log -Tail 30 -Wait
```

## Why it runs at logon (not as a Windows service)
PLCSIM Advanced only does networking inside a **logged-in Windows session** — a user signed in to the
desktop, not a hidden background service (the SYSTEM account). Run as SYSTEM and networking fails
(`-48 CommunicationInterfaceNotAvailable`). So the task runs as your user at logon.

The catch: it runs only while someone is logged in. For a server that must recover on its own after a
reboot, enable auto-logon (below).

## Unattended boot (optional auto-logon)
```powershell
.\scripts\setup-autologon.ps1
```
Logs the chosen account in automatically at boot, which starts the service. It uses Sysinternals
**Autologon** (encrypted password) if available, else the registry (plaintext — secured machines only).
Read the warning at the top of the script first.

## LAN vs localhost
LAN is the default (`http_prefix = http://+:8090/`, plus a URL reservation and firewall rule). For
local-only access, install with `-LocalOnly`. No authentication — keep it on a trusted network.

## Uninstall
```powershell
.\scripts\uninstall.ps1
```
Removes the task and the firewall/URL rule; leaves your files, config, logs, and workspaces. If you set
auto-logon, undo it via Sysinternals Autologon or by clearing `AutoAdminLogon` under
`HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon`.
