# PLC-WebControl

A small always-on web service to control **Siemens S7-PLCSIM Advanced** virtual PLCs from a browser.

Start, RUN, STOP and power off your simulated PLCs from a clean web page, have one (or more) power
on automatically after a server reboot, and rely on built-in safeguards that stop a misconfigured
machine from getting stuck in a freeze/restart loop.

> This is an independent, open-source tool. It is **not** affiliated with Siemens. It uses the
> Siemens PLCSIM Advanced API, which you must install separately under your own Siemens license.
> The proprietary Siemens DLL is **not** included here; the tool locates the one already on your machine.

---

## What it does

- **Browser control** of every PLC in a PLCSIM workspace: power on, RUN, STOP, power off.
- **Power-on limit** — choose how many PLCs may run at once (default **1**). A separate, disk-only
  **hard safety cap** guarantees the limit can never be raised past your machine's real capacity.
- **Auto-start at boot** — restore whatever was running before the last shutdown, or always start one
  chosen PLC. The *service* always runs at startup; auto-starting an *instance* is a separate toggle.
- **Freeze-loop protection** — if the machine keeps failing to stabilize after boot, the service enters
  a clearly-flagged **SAFE MODE** and stops auto-starting, so a bad configuration can't loop forever.
- **Per-PLC IP override**, re-applied on every power-on, so a PLC is reachable on your subnet.
- **Networking** via Softbus (zero-config) or TCP/IP mapped to a host network adapter.

---

## Requirements

- **Windows 10 / Windows Server 2016 or newer** (x64).
- **Siemens S7-PLCSIM Advanced** installed (tested with **V20**). Provides the runtime and the API DLL.
- **.NET Framework 4.x** — built into modern Windows; no Visual Studio or .NET SDK required.
- An **interactive user session**. PLCSIM Advanced cannot do TCP/IP networking from the SYSTEM /
  session-0 context, so the service runs as your user at logon. For unattended boot, enable auto-logon
  (an optional helper is included). See [docs/INSTALL.md](docs/INSTALL.md).

---

## Quick start (no programming needed)

1. **Download** the latest `PLC-WebControl-x.y.z.zip` from the [Releases](../../releases) page and
   extract it anywhere (e.g. `C:\PLC-WebControl`).
2. Open **PowerShell as administrator** in that folder and run:
   ```powershell
   .\scripts\install.ps1
   ```
   The installer builds the executable, detects your PLCSIM Advanced install, asks whether to keep the
   UI local-only (recommended) or expose it to the LAN, and registers an always-on Scheduled Task.
3. Open **http://localhost:8090** in a browser.

That's it. To remove it later, run `.\scripts\uninstall.ps1` as administrator.

> **Heads-up:** the UI has **no password**. Keep it on `localhost` unless you are on a trusted network.

---

## Build from source (for developers)

No IDE needed — it compiles with the in-box .NET Framework compiler:

```powershell
.\scripts\build.ps1      # produces PlcWebControl.exe next to wwwroot\
```

To produce a release ZIP:

```powershell
.\scripts\package.ps1 -Version 1.0.0   # builds + zips into dist\
```

Repository layout:

```
src\PlcWebControl.cs        the whole backend (single file, .NET Framework, HttpListener)
wwwroot\index.html          the web UI (vanilla JS, no build step)
scripts\                    build / install / uninstall / autologon / package
docs\                       INSTALL, CONFIGURATION, TROUBLESHOOTING
appconfig.example.txt       configuration template
```

---

## How the safeguards work

**Power-on limit + hard cap.** `max_powered_on` (editable in the UI) is the operational limit.
`hard_max_powered_on` (in `appconfig.txt`, not editable from the UI) is the real capacity of the
machine. The service always enforces `min(max_powered_on, hard_max_powered_on)`, so raising the limit
in the UI can never push the machine past what it can handle.

**Auto-start mode `last`.** Every power on/off records the set of running PLCs. At boot the service
restores that set (capped to the limit). A hard power cut therefore comes back to where it was.

**Loop-breaker.** Before auto-starting, the service increments an on-disk attempt counter. It is reset
to 0 only after the service stays healthy for a while — verified by repeated HTTP probes to its own
`/health` endpoint, so a *soft freeze* (processes alive but the web layer unresponsive) does **not**
count as healthy. After `boot_fail_limit` consecutive boots that never stabilize, the service enters
**SAFE MODE**: it skips auto-start, shows a banner in the UI, and waits for you to click *Re-enable*.

**Manual safe mode.** A toggle in the UI (or the `SAFEMODE` flag file) suppresses auto-start on the
next boot — handy right before you force-restart a sluggish machine.

See [docs/CONFIGURATION.md](docs/CONFIGURATION.md) for every tunable.

---

## Security

This tool has **no authentication**. Anyone who can reach the web port can power your PLCs on and off.

- It binds to **localhost** by default.
- Only expose it to a network you trust (the installer's LAN option adds a firewall rule + URL
  reservation, nothing more).
- Never expose it directly to the internet. If you must reach it remotely, put it behind a reverse
  proxy / VPN that adds authentication.

---

## Documentation

- [docs/INSTALL.md](docs/INSTALL.md) — install, the interactive-session requirement, auto-logon, LAN access.
- [docs/CONFIGURATION.md](docs/CONFIGURATION.md) — every `appconfig.txt` key.
- [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) — common problems and fixes.

## License

[MIT](LICENSE) © 2026 Marcelo Tapia. Not affiliated with Siemens.
