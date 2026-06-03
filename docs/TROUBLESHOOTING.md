# Troubleshooting

Check the log first — it explains most problems:
```powershell
Get-Content .\webcontrol.log -Tail 40 -Wait
```

## "runtime OFF" in the UI / `runtime manager not reachable`

The service can't reach the PLCSIM Advanced Runtime Manager.

- Make sure **S7-PLCSIM Advanced** is installed.
- The service must run in an **interactive session** (see below). If it was started as SYSTEM /
  session 0, networking fails — reinstall so the task runs as your user at logon.
- The log line `PLCSIM Advanced API DLL: NOT FOUND` means the DLL wasn't located. Set `api_dll_path`
  in `appconfig.txt` to the full path of `Siemens.Simatic.Simulation.Runtime.Api.x64.dll`, or set the
  `PLCSIM_API_DLL` environment variable, then restart the service.

## `-48 CommunicationInterfaceNotAvailable` / `NetInterfaces = 0`

PLCSIM Advanced cannot do TCP/IP networking from the **SYSTEM / session-0** context. The service must
run inside an **interactive user session**.

- Reinstall with `scripts\install.ps1` (it registers the task to run as your user at logon, not SYSTEM).
- For unattended boot, enable auto-logon with `scripts\setup-autologon.ps1` so a user session exists.
- If networking is stuck, the background processes `s7opnsim.exe` / `S7SimHost.exe` may be holding the
  virtual switch; ending them (or rebooting) clears it.

## A PLC powers on but stays in STOP / "has NO program"

PLCSIM error `IsEmpty (-52)`: the instance has no program. **Download the program to it once from TIA
Portal.** With `storage_layout = default` the program is stored on disk and survives reboots, so you
only need to download it once.

## Port already in use / `HttpListenerException`

Another program owns the port (8090 by default).

- Change `http_prefix` in `appconfig.txt` (e.g. to `:8091`) while the service is stopped, or pass
  `-Port` to the installer.

## `Access is denied` when binding to the LAN

Binding to `http://+:8090/` requires a URL reservation for the account running the service. The
installer's `-Lan` option does this. To do it manually (elevated):
```powershell
netsh http add urlacl url=http://+:8090/ user="DOMAIN\User"
```

## Rebuild fails with `CS0016 ... being used by another process`

`PlcWebControl.exe` is locked by the running service. Stop it first (elevated), then build:
```powershell
Stop-ScheduledTask -TaskName "PLCSIM WebControl"
Get-Process PlcWebControl | Stop-Process -Force
.\scripts\build.ps1
Start-ScheduledTask -TaskName "PLCSIM WebControl"
```

## The UI shows a red "SAFE MODE" banner

The loop-breaker detected repeated boots that never stabilized, or the manual `SAFEMODE` flag is set,
so auto-start was skipped on purpose.

- Investigate why the machine wasn't stabilizing (too many instances for its capacity? lower
  `max_powered_on` or `hard_max_powered_on`).
- Click **Re-enable auto-start** in the banner (or delete the `SAFEMODE` file and reset the counter).

## A second PLC won't power on ("only one PLC powered on at a time")

That's the limiter working. Raise **Max powered on** in the UI (it can't exceed `hard_max_powered_on`,
which you set in `appconfig.txt` to your machine's real capacity), or power off the running one first.

## I can't change a PLC's IP

Set IP requires the instance to be **powered on**. Power it on, then use the **IP…** button. The IP is
saved and re-applied on every power-on. Note: if the program downloaded from TIA defines its own IP,
that program value applies on each power-on too.
