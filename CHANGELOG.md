# Changelog

All notable changes to this project are documented here.
This project adheres to [Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-06-03

First public release.

### Added
- Web UI to list and control the PLCs of a Siemens S7-PLCSIM Advanced workspace
  (power on, RUN, STOP, power off) from a browser.
- Configurable **power-on limit** with a separate, disk-only **hard safety cap** that the
  web UI can never exceed (protects machines that can only run a few instances at once).
- **Instance auto-start** at boot, with two modes: `last` (restore whatever was running before
  the last shutdown) and `fixed` (always start one chosen instance).
- **Boot protections** against a freeze/restart loop:
  - boot-attempt counter that enters SAFE MODE after N non-stabilizing boots,
  - a "clean boot" check gated on repeated self `/health` probes (detects a soft freeze where
    processes stay alive but the web layer becomes unresponsive),
  - staggered start (one instance at a time, abort on failure),
  - a manual `SAFEMODE` flag file / UI toggle to suppress auto-start on the next boot.
- **Per-PLC IP override** that is re-applied on every power-on.
- Networking via **Softbus** (default, zero-config) or **TCP/IP** mapped to a host adapter.
- Self-contained: builds with the in-box .NET Framework `csc.exe` (no Visual Studio / .NET SDK),
  auto-detects the Siemens PLCSIM Advanced API DLL, and runs as an always-on Scheduled Task.
- Guided installer, uninstaller, and an optional auto-logon helper for unattended boot.
