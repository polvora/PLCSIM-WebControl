# Installation guide

## 1. Prerequisites

- Windows 10 / Windows Server 2016 or newer (x64).
- Siemens **S7-PLCSIM Advanced** installed (tested with V20). The installer auto-detects its API DLL.
- .NET Framework 4.x (already present on modern Windows).

## 2. Install

1. Download `PLCSIM-WebControl-x.y.z.zip` from the Releases page and extract it to a folder you control,
   e.g. `C:\PLCSIM-WebControl`.
2. Right-click **PowerShell → Run as administrator**, change into that folder, and run:
   ```powershell
   .\scripts\install.ps1
   ```
   The script will:
   - build `PlcWebControl.exe` if it isn't present,
   - confirm the PLCSIM Advanced API DLL was found,
   - make the UI reachable from the **LAN by default** (opens the firewall for the port; pass
     `-LocalOnly` to bind to localhost instead),
   - create `appconfig.txt` from the template,
   - register an always-on Scheduled Task named **"PLCSIM WebControl"**,
   - optionally start the service immediately.
3. Browse to **http://localhost:8090**.

Useful commands:
```powershell
Start-ScheduledTask -TaskName "PLCSIM WebControl"
Stop-ScheduledTask  -TaskName "PLCSIM WebControl"
Get-Content .\webcontrol.log -Tail 30 -Wait
```

## 3. The interactive-session requirement (important)

PLCSIM Advanced's Runtime Manager must run inside an **interactive Windows session** to do TCP/IP
networking. Running as SYSTEM / session 0 fails (you'll see `NetInterfaces = 0` and error `-48
CommunicationInterfaceNotAvailable`). That is why the Scheduled Task runs **as your user at logon**,
not as a Windows Service / SYSTEM.

Consequence: the service only runs while a user is logged on. For a server that must come back on its
own after a reboot, enable **auto-logon**.

## 4. Unattended boot (optional auto-logon)

Run, as administrator:
```powershell
.\scripts\setup-autologon.ps1
```
It configures Windows to log the chosen account on automatically at boot, which then triggers the
Scheduled Task and starts the service.

- Prefer **Sysinternals Autologon** (stores the password encrypted). If `autologon64.exe` /
  `autologon.exe` is on the PATH or next to the script, the helper uses it automatically.
- Otherwise it falls back to the registry method, which stores the password in plaintext — only use
  it on a physically secured machine.

Read the security warning at the top of the script before running it.

## 5. LAN access (default) vs localhost-only

By default the installer makes the UI reachable from the LAN: it sets `http_prefix = http://+:8090/`,
reserves the URL for your account (`netsh http add urlacl`), and opens an inbound firewall rule for
TCP 8090. Other machines then reach it at `http://<this-machine-ip>:8090`. Remote control is the main
feature, so this is the default.

To bind to localhost only instead:
```powershell
.\scripts\install.ps1 -LocalOnly
```

> The UI has no authentication; it is meant for trusted, closed networks. Don't place it on an
> untrusted network or expose it directly to the internet.

## 6. Uninstall

```powershell
.\scripts\uninstall.ps1
```
Removes the Scheduled Task and the LAN URL reservation / firewall rule. It leaves your program files,
`appconfig.txt`, logs, and PLCSIM workspaces untouched. If you enabled auto-logon, undo it with
Sysinternals Autologon or by clearing `AutoAdminLogon` under
`HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon`.
