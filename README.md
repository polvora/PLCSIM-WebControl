# PLCSIM-WebControl

**Remote web control and automatic startup for Siemens S7-PLCSIM Advanced virtual PLCs.**

PLCSIM-WebControl is a small always-on web app that **extends** S7-PLCSIM Advanced — it does **not**
replace it. You still create and configure your virtual PLCs in the Siemens PLCSIM Advanced GUI as
usual; PLCSIM-WebControl reads that workspace and adds what the GUI doesn't give you:

- 🌐 **Remote control from a browser** — power on, RUN, STOP and power off your PLCs from any machine
  on the network. Drive a simulation host from your own desktop or another VM, with no Siemens GUI and
  no remote-desktop session required.
- 🔄 **Automatic startup** — have your PLCs come back up on their own after a server reboot,
  completely unattended. The original GUI can't do this.
- 💾 **Persistent by default** — every instance is registered against PLCSIM's persistent storage, so a
  PLC's downloaded program survives a restart. Combined with auto-start, a PLC comes back on its own
  after a reboot — nothing to re-open, nothing to re-download from TIA.

> Independent, open-source tool — **not** affiliated with Siemens. It uses the Siemens PLCSIM Advanced
> API, which you install separately under your own Siemens license. The proprietary Siemens DLL is
> **not** included here; the tool locates the one already on your machine.

---

## Other features

Beyond remote control and auto-start:

- **Power-on limit** (default **1**) with a separate, disk-only **hard safety cap**, so you never start
  more PLCs than the machine can actually handle.
- **Per-PLC IP override**, re-applied on every power-on, so a PLC stays reachable on your subnet.
- **Network mode**: Softbus (zero-config) or TCP/IP mapped to a host adapter.
- **Unattended-boot safeguards** that keep auto-start from getting a machine stuck in a freeze/restart
  loop ([details below](#safeguards-for-unattended-operation)).

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

1. **Download** the latest `PLCSIM-WebControl-x.y.z.zip` from the [Releases](../../releases) page and
   extract it anywhere (e.g. `C:\PLCSIM-WebControl`).
2. Open **PowerShell as administrator** in that folder and run:
   ```powershell
   .\scripts\install.ps1
   ```
   The installer builds the executable, detects your PLCSIM Advanced install, makes the UI reachable
   from the LAN by default (no authentication; it opens the firewall for the port), and registers an
   always-on Scheduled Task. Pass `-LocalOnly` to bind it to localhost instead.
3. Open the UI:
   - on this machine: **http://localhost:8090**
   - from another machine: **http://&lt;this-machine-ip&gt;:8090**

That's it. To remove it later, run `.\scripts\uninstall.ps1` as administrator.

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

## Safeguards for unattended operation

Powering PLCs on automatically, with no human watching, needs a few guardrails. These run quietly in
the background — in normal use you never notice them — but they are what makes auto-start safe to rely
on, so they are documented here.

**Power-on limit + hard cap.** `max_powered_on` (editable in the UI) is the operational limit.
`hard_max_powered_on` (in `appconfig.txt`, not editable from the UI) is the real capacity of the
machine. The service always enforces `min(max_powered_on, hard_max_powered_on)`, so raising the limit
in the UI can never push the machine past what it can handle.

**Auto-start mode `last`.** Every power on/off records the set of running PLCs. At boot the service
restores that set (capped to the limit). A reboot therefore comes back to where it was.

**Loop-breaker.** Before auto-starting, the service increments an on-disk attempt counter. It is reset
to 0 only after the service stays healthy for a while — verified by repeated HTTP probes to its own
`/health` endpoint, so a *soft freeze* (processes alive but the web layer unresponsive) does **not**
count as healthy. After `boot_fail_limit` consecutive boots that never stabilize, the service enters
**SAFE MODE**: it skips auto-start, shows a banner in the UI, and waits for you to click *Re-enable*.

**Manual safe mode.** A toggle in the UI (or the `SAFEMODE` flag file) suppresses auto-start on the
next boot — handy right before you force-restart a sluggish machine.

See [docs/CONFIGURATION.md](docs/CONFIGURATION.md) for every tunable.

---

## Documentation

- [docs/INSTALL.md](docs/INSTALL.md) — install, the interactive-session requirement, auto-logon, LAN access.
- [docs/CONFIGURATION.md](docs/CONFIGURATION.md) — every `appconfig.txt` key.
- [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) — common problems and fixes.

## License

[MIT](LICENSE) © 2026 Marcelo Tapia. Not affiliated with Siemens.
