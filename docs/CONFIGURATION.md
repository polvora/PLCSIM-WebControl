# Configuration reference

Settings live in `appconfig.txt` next to `PlcsimWebControl.exe` (`key = value`; `#`/`;` = comment; blank =
built-in default). The service rewrites this file when you change settings in the UI, so edit it by hand
only while the service is **stopped**. Start from `appconfig.example.txt`.

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
| `max_powered_on` | `1` | Operational limit for **manual** power-ons, editable from the UI. **Not** capped by the hard cap — raise it to test how many PLCs the machine handles. |
| `hard_max_powered_on` | `4` | Safety cap for **auto-start only** (disk-only, not editable from the UI). After an unattended reboot, auto-start restores at most this many — so a manual test that froze the box still reboots safely. Set it to a number the machine can definitely run unattended. |

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
| `network_mode` | `Softbus` | `Softbus` = zero-config simulation bus (no adapter). `TCPIPSingleAdapter` = real Ethernet over the host's stack, no adapter selection. `TCPIPMultipleAdapter` = real Ethernet via the PLCSIM virtual switch bound to a chosen host adapter. |
| `adapter_index` | `0` | Host adapter `ifIndex`. **Used only in `TCPIPMultipleAdapter` mode** (Softbus and Single Adapter ignore it). Pick it from the UI's adapter list, which only appears in that mode. |
| `adapter_name` | (empty) | Friendly name of that adapter (informational; Multiple Adapter only). |

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
