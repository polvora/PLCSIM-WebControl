# PLCSIM Auto-Start

**Automatic startup (and remote web control) for Siemens S7-PLCSIM Advanced virtual PLCs.**

![PLCSIM Auto-Start web interface](docs/ui.png)

PLCSIM Auto-Start is a small always-on web app that **extends** S7-PLCSIM Advanced — it does **not**
replace it. You still create and configure your virtual PLCs in the Siemens PLCSIM Advanced GUI as
usual; PLCSIM Auto-Start reads that workspace and adds what the GUI doesn't give you:

- 🔄 **Automatic startup** — your PLCs come back up on their own after a server reboot, completely
  unattended. This is the headline: the Siemens GUI has no way to do it.
- 🌐 **Remote control from a browser** — power on, RUN, STOP and power off your PLCs from any machine
  on the network. Drive a simulation host from your own desktop or another VM, with no Siemens GUI and
  no remote-desktop session required.
- 💾 **Persistent by default** — every instance is registered against PLCSIM's persistent storage, so a
  PLC's downloaded program survives a restart. Combined with auto-start, a PLC comes back on its own
  after a reboot — nothing to re-open, nothing to re-download from TIA.

> Independent, open-source tool — **not** affiliated with Siemens. It uses the Siemens PLCSIM Advanced
> API, which you install separately under your own Siemens license. The proprietary Siemens DLL is
> **not** included here; the tool locates the one already on your machine.

---

## Other features

Beyond remote control and auto-start:

- **Power-on limit** (default **1**, freely adjustable in the UI to test capacity) plus a disk-only
  **hard cap** that bounds the **auto-start** count — the freeze protection for unattended reboots.
- **Per-PLC IP override**, re-applied on every power-on, so a PLC stays reachable on your subnet.
- **Network mode**: Softbus (zero-config) or TCP/IP mapped to a host adapter.
- **Installs as a Windows Service** you Start/Stop from `services.msc` / Task Manager. (Because PLCSIM
  needs an interactive session, the service is a launcher that runs the app in the logged-in session;
  `-AsTask` uses a Scheduled Task instead. See [docs/INSTALL.md](docs/INSTALL.md#how-the-service-works-the-session-0-catch).)
- **Unattended-boot safeguards** that keep auto-start from getting a machine stuck in a freeze/restart
  loop ([details below](#safeguards-for-unattended-operation)).

---

## Requirements

- **Windows 10 / Windows Server 2016 or newer** (x64).
- **Siemens S7-PLCSIM Advanced** installed (tested with **V20**). Provides the runtime and the API DLL.
- **.NET Framework 4.x** — built into modern Windows; no Visual Studio or .NET SDK required.
- A **logged-in Windows session** — i.e. a user signed in to the desktop, not the hidden SYSTEM
  context. PLCSIM needs this for networking, so the Windows Service runs as a *launcher* that starts the
  app inside the logged-in session. For a server to recover on its own after a reboot, enable auto-logon
  (the installer offers it; see [docs/INSTALL.md](docs/INSTALL.md)).

---

## Quick start (no programming needed)

1. **Download** the project — green **Code** button → **Download ZIP**. You get
   **`PLCSIM-AutoStart-main.zip`** (the prebuilt `PlcsimAutoStart.exe` is inside). Extract it anywhere;
   it produces a `PLCSIM-AutoStart-main` folder.
2. **Double-click `Install.cmd`** and accept the admin prompt (UAC). It sets everything up: detects
   your PLCSIM Advanced install, makes the UI reachable from the LAN (no authentication; it opens the
   firewall for the port), creates `appconfig.txt`, installs an always-on **Windows Service** (Start/Stop
   it from `services.msc` / Task Manager), and offers to enable **auto-logon** for fully unattended boot.
   *(Command-line alternative: run `scripts\install.ps1` from an elevated PowerShell; add `-LocalOnly`
   to bind to localhost.)*
3. Open the UI:
   - on this machine: **http://localhost:8090**
   - from another machine: **http://&lt;this-machine-ip&gt;:8090**

That's it. To remove it later, run `.\scripts\uninstall.ps1` as administrator.

---

## For developers

The prebuilt `PlcsimAutoStart.exe` ships in the repo. Only if you change the backend
(`src\PlcsimAutoStart.cs`) or UI (`wwwroot\index.html`), rebuild with `.\scripts\build.ps1` — no IDE
needed, just the in-box .NET Framework compiler.

---

## Safeguards for unattended operation

Auto-start runs with no one watching, so a few guardrails stop a bad setup from looping. They run in
the background; you normally never see them.

**Power-on limit + hard cap.** `max_powered_on` (UI-editable) is the operational limit for *manual*
power-ons — it is **not** capped by the hard cap, so you can raise it to discover how many PLCs your
machine really handles. `hard_max_powered_on` (disk-only) limits only the **auto-start** count: after
an unattended reboot, auto-start restores at most that many — so even if a manual test froze the box,
the next boot stays within the safe number.

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
