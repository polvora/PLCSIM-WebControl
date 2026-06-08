# Installation

## Requirements
- Windows 10 / Server 2016+ (x64)
- Siemens **S7-PLCSIM Advanced** (tested with V20) — the installer finds its API DLL automatically
- .NET Framework 4.x (already on modern Windows)

## Install
1. Download the project — green **Code → Download ZIP** — which gives **`PLCSIM-AutoStart-main.zip`**
   (the prebuilt `PlcsimAutoStart.exe` is inside). Extract it; you get a `PLCSIM-AutoStart-main` folder.
2. **Double-click `Install.cmd`** and accept the UAC prompt. It finds the PLCSIM DLL, opens the LAN
   port, creates `appconfig.txt`, and installs the **Windows Service "PLCSIM AutoStart"** (you can
   Start/Stop it from `services.msc` / Task Manager). Add `-AsTask` to install a Scheduled Task instead.

   > Because it was downloaded from the internet, Windows may first show a security warning ("Windows
   > protected your PC" / "Open File - Security Warning"). Choose **More info → Run anyway** — it's
   > your own downloaded file.

   Command-line alternative: from an elevated PowerShell, run `.\scripts\install.ps1` (add `-LocalOnly`
   to bind to localhost).
3. Open **http://localhost:8090** (or `http://<this-machine-ip>:8090` from another machine).

Manage it (or use `services.msc` / Task Manager → Services):
```powershell
Start-Service "PLCSIM AutoStart"
Stop-Service  "PLCSIM AutoStart"
Get-Content .\autostart.log -Tail 30 -Wait
```

## How the service works (the session-0 catch)
PLCSIM Advanced only does networking inside a **logged-in Windows session** — a user signed in to the
desktop, not the hidden SYSTEM / session-0 context a normal service runs in (run there and networking
fails with `-48 CommunicationInterfaceNotAvailable`).

So the installed service is a **launcher**: it runs as LocalSystem (so it shows up in `services.msc` and
you can Start/Stop it), but on start it launches the actual web app inside the logged-in user's
interactive session, with that user's elevated token — which is where PLCSIM works.

The catch: there must be a logged-in session for it to launch into. For a server that must recover on
its own after a reboot, enable auto-logon (below). The `-AsTask` option skips the service and runs the
app directly from a Scheduled Task at logon instead.

## Unattended boot (auto-logon)
PLCSIM only runs while a user is signed in, so for a server that must recover on its own after a
reboot, enable auto-logon. **The installer offers this as a step** and asks for the account's password.
To set it up later or on its own:
```powershell
.\scripts\setup-autologon.ps1
```
It logs the account in automatically at boot, which starts the service. It uses Sysinternals
**Autologon** (encrypted password) if available, else the registry (plaintext — secured machines only).
Read the warning at the top of the script first.

## LAN vs localhost
LAN is the default (`http_prefix = http://+:8090/`, plus a URL reservation and firewall rule). For
local-only access, install with `-LocalOnly`. No authentication — keep it on a trusted network.

## Uninstall
```powershell
.\scripts\uninstall.ps1
```
Removes the service (or task) and the firewall/URL rule; leaves your files, config, logs, and workspaces. If you set
auto-logon, undo it via Sysinternals Autologon or by clearing `AutoAdminLogon` under
`HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon`.
