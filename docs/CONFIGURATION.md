# Configuration reference

All settings live in `appconfig.txt` next to `PlcWebControl.exe`. It is a simple `key = value` file
(`#` or `;` start a comment). A **blank value means "use the built-in default"**.

> The service **rewrites** `appconfig.txt` whenever you change settings from the web UI. Edit it by
> hand only while the service is **stopped**, or your edits may be overwritten.

Start from `appconfig.example.txt`.

## Web server

| Key | Default | Meaning |
|-----|---------|---------|
| `http_prefix` | `http://+:8090/` | Listen URL. `http://+:8090/` = all interfaces / LAN (the default; remote control is the main feature). Use `http://localhost:8090/` for local-only access. The installer reserves the URL (`netsh http add urlacl`) and opens the firewall when binding to the LAN. |

## Workspace

| Key | Default | Meaning |
|-----|---------|---------|
| `workspace_root` | `<Documents>\PLCSIM` | Folder containing your PLCSIM workspaces (of the account running the service). |
| `workspace` | auto | Active workspace folder. Blank = auto-detect the most recently modified one. Also settable from the UI. |

## Instance auto-start

| Key | Default | Meaning |
|-----|---------|---------|
| `autostart_enabled` | `false` | Whether any instance is powered on automatically at boot. |
| `autostart_mode` | `last` | `last` = restore whatever was powered on before the last shutdown. `fixed` = always start `autostart_instance`. |
| `autostart_instance` | (empty) | Instance name used when `autostart_mode = fixed`. |

## Power-on limit

| Key | Default | Meaning |
|-----|---------|---------|
| `max_powered_on` | `1` | Operational limit of how many PLCs may be powered on at once. Editable from the UI. |
| `hard_max_powered_on` | `4` | **Hard safety cap = the real capacity of this machine.** Editable on disk **only**; the UI can never exceed it. The effective limit is `min(max_powered_on, hard_max_powered_on)`. Set this to how many PLCs your machine can run without freezing. |

## Boot protections

| Key | Default | Meaning |
|-----|---------|---------|
| `boot_fail_limit` | `2` | Consecutive boots that never stabilize before the service enters SAFE MODE and skips auto-start. Set to `1` to trip after a single failed boot (most conservative). |
| `stable_seconds` | `90` | Stability window after auto-start before a boot is declared "clean" (counter reset to 0). |
| `start_stagger_ms` | `4000` | Pause between power-ons during the staggered auto-start. |
| `health_probe_interval_ms` | `15000` | How often the self `/health` probe runs during the stability window. |
| `health_timeout_ms` | `3000` | Strict timeout per probe. A probe slower than this counts as unhealthy (detects a soft freeze). |
| `last_running` | (managed) | Internal: the last powered-on set, used by `autostart_mode = last`. Leave blank. |

## Network

| Key | Default | Meaning |
|-----|---------|---------|
| `network_mode` | `Softbus` | `Softbus` = zero-config simulation bus. `TCPIPSingleAdapter` / `TCPIPMultipleAdapter` = real Ethernet via a host adapter. |
| `adapter_index` | `0` | Host adapter `ifIndex` (only for TCP/IP modes). Pick it from the UI's adapter list. |
| `adapter_name` | (empty) | Friendly name of that adapter (informational). |

## Advanced

| Key | Default | Meaning |
|-----|---------|---------|
| `storage_layout` | `default` | `default` uses PLCSIM's native persistent storage (recommended; the downloaded program survives reboots). |
| `poweron_timeout` | `60000` | Power-on timeout (ms). |
| `run_timeout` | `60000` | RUN/STOP timeout (ms). |
| `connect_wait_seconds` | `120` | How long to wait for the PLCSIM runtime manager at startup. |
| `api_dll_path` | auto | Explicit path to `Siemens.Simatic.Simulation.Runtime.Api.x64.dll`. Blank = auto-detect the installed PLCSIM Advanced. Can also be set via the `PLCSIM_API_DLL` environment variable. |
| `log` | `webcontrol.log` (next to the exe) | Log file path. |
| `ip.<InstanceName>` | (none) | Saved IP override for a PLC, format `ip,mask,gateway`, re-applied on every power-on. Created when you set an IP from the UI. |
