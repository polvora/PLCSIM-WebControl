# Configuration reference

Settings live in `appconfig.txt` next to `PlcsimAutoStart.exe` (`key = value`; `#`/`;` = comment; blank =
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
| `max_powered_on` | `1` | The most PLCs you can power on by hand at once (editable in the UI). Not limited by the auto-start cap, so you can raise it to test how many your machine handles. |
| `hard_max_powered_on` | `1` | The auto-start cap: the most PLCs auto-start turns on at boot. Set it to how many this machine can run at once. Editable in the UI (raising it asks you to confirm) or here. It only affects auto-start — the manual limit above can be higher. |

## Boot protections

| Key | Default | Meaning |
|-----|---------|---------|
| `boot_fail_limit` | `2` | How many boots in a row can fail to come up working before the service stops auto-starting (SAFE MODE). Set to `1` to stop after a single bad boot. |
| `stable_seconds` | `90` | How long after auto-start the machine must keep responding before the boot counts as good (the fail counter resets to 0). |
| `start_stagger_ms` | `4000` | Pause between power-ons during auto-start, so they don't all start at the same time. |
| `health_probe_interval_ms` | `15000` | How often, during the stability window, the tool checks that its own web page still responds. |
| `health_timeout_ms` | `3000` | If a check takes longer than this, it counts as failed — the sign of a machine that looks alive but is frozen. |
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
| `log` | `autostart.log` (next to the exe) | Log file path. |
| `ip.<InstanceName>` | (none) | Saved IP override for a PLC, format `ip,mask,gateway`, re-applied on every power-on. Created when you set an IP from the UI. |
