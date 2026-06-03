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
- A **logged-in Windows session** — i.e. a user signed in to the desktop, not a hidden background
  service. PLCSIM needs this for networking, so the service runs as your user at logon. For a server to
  recover on its own after a reboot, enable auto-logon (helper included; see [docs/INSTALL.md](docs/INSTALL.md)).

---

## Quick start (no programming needed)

1. **Download** the project — green **Code** button → **Download ZIP** (the prebuilt
   `PlcWebControl.exe` is included) — and extract it anywhere (e.g. `C:\PLCSIM-WebControl`).
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

## For developers

The prebuilt `PlcWebControl.exe` ships in the repo. Only if you change the backend
(`src\PlcWebControl.cs`) or UI (`wwwroot\index.html`), rebuild with `.\scripts\build.ps1` — no IDE
needed, just the in-box .NET Framework compiler.

---

## Safeguards for unattended operation

Auto-start runs with no one watching, so a few guardrails stop a bad setup from looping. They run in
the background; you normally never see them.

**Power-on limit + hard cap.** `max_powered_on` (UI-editable) is the operational limit;
`hard_max_powered_on` (disk-only) is the machine's real capacity. The service enforces the smaller of
the two, so the UI can't push the machine past what it can handle.

**Loop-breaker.** A counter is bumped before each auto-start and reset only after the service passes
repeated `/health` probes for a while — so a *soft freeze* (alive but unresponsive) won't clear it.
After `boot_fail_limit` boots that never stabilize, the service enters **SAFE MODE**: no auto-start, a
red banner in the UI, and a *Re-enable* button. You can also arm it yourself before forcing a restart
(UI toggle or the `SAFEMODE` file).

Every value is tunable in [docs/CONFIGURATION.md](docs/CONFIGURATION.md).

---

## Documentation

- [docs/INSTALL.md](docs/INSTALL.md) — install, auto-logon, LAN access.
- [docs/CONFIGURATION.md](docs/CONFIGURATION.md) — every `appconfig.txt` key.
- [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) — common problems and fixes.

## License

[MIT](LICENSE) © 2026 Marcelo Tapia. Not affiliated with Siemens.
