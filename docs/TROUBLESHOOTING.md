# Troubleshooting

Check the log first:
```powershell
Get-Content .\autostart.log -Tail 40 -Wait
```

## "runtime OFF" / runtime manager not reachable
- Is **S7-PLCSIM Advanced** installed?
- The service must run in a **logged-in Windows session** (a signed-in desktop, not the hidden SYSTEM
  account). If it started as SYSTEM, reinstall so the task runs as your user at logon.
- Log says `API DLL: NOT FOUND`? Set `api_dll_path` in `appconfig.txt` (or the `PLCSIM_API_DLL` env var)
  to `Siemens.Simatic.Simulation.Runtime.Api.x64.dll`, then restart.

## `-48 CommunicationInterfaceNotAvailable` / `NetInterfaces = 0`
PLCSIM can't do networking from the hidden SYSTEM context — it needs a **logged-in session**.
- Reinstall with `scripts\install.ps1` (runs as your user at logon).
- For unattended boot, enable auto-logon (`scripts\setup-autologon.ps1`).
- If networking stays stuck, end `s7opnsim.exe` / `S7SimHost.exe` (or reboot) to release the virtual switch.

## PLC powers on but stays in STOP / "has NO program"
Error `IsEmpty (-52)`: no program. **Download it once from TIA Portal.** With `storage_layout = default`
it's stored on disk and survives reboots.

## Port already in use / `HttpListenerException`
Something else owns port 8090. Change `http_prefix` in `appconfig.txt` (service stopped) or pass `-Port`
to the installer.

## `Access is denied` binding to the LAN
Binding to `http://+:8090/` needs a URL reservation. The installer adds it automatically; by hand
(elevated):
```powershell
netsh http add urlacl url=http://+:8090/ user="DOMAIN\User"
```

## Rebuild fails: `CS0016 ... being used by another process`
The running app locks `PlcsimAutoStart.exe`. Stop it first (elevated):
```powershell
Stop-Service "PLCSIM AutoStart"            # or: Stop-ScheduledTask -TaskName "PLCSIM AutoStart"
Get-Process PlcsimAutoStart | Stop-Process -Force
.\scripts\build.ps1
Start-Service "PLCSIM AutoStart"           # or: Start-ScheduledTask -TaskName "PLCSIM AutoStart"
```

## Red "SAFE MODE" banner
Several boots in a row didn't come up working, so the tool skipped auto-start on purpose. If the machine
is overloaded, lower the **auto-start cap** (`hard_max_powered_on`), then click **Re-enable auto-start**.

## Official control panel / TIA says "no connection to PLCSIM Advanced"
The service holds a live connection to the PLCSIM runtime, and the official tools can't connect at the
same time. Don't stop the service — click **Enter maintenance mode** in the UI. It releases PLCSIM (PLCs
that are powered on keep running) so the control panel / TIA can connect, e.g. to **add a new instance**.
Click **Resume** when you're done to reconnect.

## A second PLC won't power on
The limiter. Raise **Max powered on** in the UI (it can't exceed `hard_max_powered_on`) or power one off.

## Can't change a PLC's IP
The PLC must be **powered on** first, then use the **IP…** button. The IP is re-applied on every
power-on. If the TIA program defines its own IP, that applies too.

## An instance powers off by itself after a while
This tool never powers an instance off on its own (only your explicit **Power off** does — and it's
logged). A simulation that powers off after some minutes is **PLCSIM Advanced** itself, typically the
**trial / unlicensed** time limit. Check your S7-PLCSIM Advanced license (Automation License Manager);
a valid license removes the limit.

## Network mode looks like Softbus / "didn't persist"
Your setting in `appconfig.txt` **does** persist across reboots. Two things to know:
- The PLCSIM runtime reverts its live `NetworkMode` to Softbus when idle (no instances on). The tool
  re-applies your configured mode at startup and before powering on an instance, so PLCs still come up
  in the right mode.
- A **fresh re-install** creates a new `appconfig.txt` (network defaults to Softbus), because the
  config isn't shipped in the ZIP. Re-pick your network mode in the UI after a reinstall.
