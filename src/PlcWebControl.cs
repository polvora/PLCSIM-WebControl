// PlcWebControl.cs
// Always-on local web service to control Siemens S7-PLCSIM Advanced virtual PLCs from a browser.
//
// Features:
//   * Web UI listing the PLCs of a PLCSIM workspace (Documents\PLCSIM\<workspace>).
//   * Power on / Run / Stop / Power off from the browser.
//   * Configurable LIMIT of how many instances may be powered on at once (default 1), with a
//     separate disk-only HARD safety cap that the web UI can never exceed.
//   * Per-instance auto-start at boot. Mode "last" restores whatever was running before the
//     last shutdown; mode "fixed" always starts one chosen instance.
//   * Boot protections against a freeze/restart loop: a boot-attempt counter that only resets
//     after the service stays healthy for a while (verified by repeated self HTTP /health probes),
//     a staggered start, and a manual SAFEMODE flag file.
//   * Optional networking: PLCSIM Softbus (default, zero-config) or TCP/IP mapped to a host
//     network adapter for real Ethernet communication.
//
// This program is a thin client of the proprietary Siemens PLCSIM Advanced API; that DLL is NOT
// redistributed. It is located on the local machine at runtime (auto-detected, or via the
// 'api_dll_path' config key / PLCSIM_API_DLL environment variable).
//
// Build with scripts\build.ps1 (.NET Framework csc.exe, /platform:x64).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Siemens.Simatic.Simulation.Runtime;

internal static class Program
{
    // Resolved at startup to the local Siemens PLCSIM Advanced API DLL (never shipped with this tool).
    private static string _apiDllPath;

    private static Config _cfg;
    private static PlcManager _plc;

    private static int Main(string[] args)
    {
        string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        _apiDllPath = ApiDllLocator.Find(exeDir);

        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            e.Name.StartsWith("Siemens.Simatic.Simulation.Runtime.Api", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(_apiDllPath) && File.Exists(_apiDllPath)
                ? Assembly.LoadFrom(_apiDllPath) : null;

        try { return RealMain(args, exeDir); }
        catch (Exception ex) { Log.Write("FATAL " + ex); return 1; }
    }

    private static int RealMain(string[] args, string exeDir)
    {
        _cfg = Config.Load(Path.Combine(exeDir, "appconfig.txt"), exeDir);
        Log.File = _cfg.LogFile;
        Log.Write("==================================================================");
        Log.Write("PLC-WebControl starting. workspace='" + _cfg.Workspace + "' prefix='" + _cfg.HttpPrefix + "'");
        Log.Write("PLCSIM Advanced API DLL: " + (string.IsNullOrEmpty(_apiDllPath) ? "NOT FOUND (set api_dll_path in appconfig.txt)" : _apiDllPath));

        _plc = new PlcManager(_cfg);

        // Wait for the PLCSIM runtime manager (boot timing), then apply global network config.
        if (_plc.WaitForRuntime(_cfg.ConnectWaitSeconds))
        {
            Log.Write("Runtime manager OK (API 0x" + SimulationRuntimeManager.Version.ToString("X") + ").");
            try { _plc.ApplyGlobalNetwork(); }
            catch (Exception ex) { Log.Write("WARN ApplyGlobalNetwork: " + ex.Message); }

            // If there is no record of "what was last running", seed it with whatever is powered on
            // now (robustness if the config was lost or this is a first run with instances already up).
            try
            {
                if (_cfg.LastRunning.Count == 0)
                {
                    var nowOn = _plc.PoweredOnList();
                    if (nowOn.Count > 0) { _cfg.LastRunning = nowOn; _cfg.Save(); Log.Write("Seeded last_running with currently powered-on: " + string.Join(", ", nowOn.ToArray())); }
                }
            }
            catch (Exception ex) { Log.Write("WARN seed last_running: " + ex.Message); }

            // Instance auto-start WITH PROTECTIONS (loop-breaker + manual flag + staggered start).
            if (_cfg.AutostartEnabled)
            {
                bool flag = BootGuard.FlagPresent(exeDir);
                int attempts = BootGuard.ReadAttempts(exeDir);
                if (flag)
                {
                    _plc.SafeMode = true;
                    _plc.SafeModeReason = "Manual SAFE MODE is active (SAFEMODE flag file present). Auto-start was skipped this boot.";
                    Log.Write("SAFE MODE (manual): SAFEMODE flag file present. Auto-start SKIPPED.");
                }
                else if (attempts >= _cfg.BootFailLimit)
                {
                    _plc.SafeMode = true;
                    _plc.SafeModeReason = "Detected " + attempts + " boots that never stabilized (possible freeze loop). Auto-start was skipped. Check the load / safety cap, then re-enable.";
                    Log.Write("SAFE MODE (loop-breaker): " + attempts + " consecutive non-stabilizing boots (limit " + _cfg.BootFailLimit + "). Auto-start SKIPPED to break the loop.");
                }
                else
                {
                    var targets = _plc.AutostartTargets();
                    if (targets.Count > 0)
                    {
                        int next = attempts + 1;
                        BootGuard.WriteAttempts(exeDir, next); // record the attempt BEFORE powering on (survives a freeze)
                        Log.Write("Auto-start (mode " + _cfg.AutostartMode + ", effective cap " + _cfg.EffectiveMax() + ", attempt " + next + "/" + _cfg.BootFailLimit + "). Staggered: " + string.Join(", ", targets.ToArray()));
                        ThreadPool.QueueUserWorkItem(_ => _plc.RunStaggeredAutostart(targets, exeDir));
                    }
                    else
                    {
                        Log.Write("Auto-start enabled but no target (mode " + _cfg.AutostartMode + ": nothing was running / no fixed instance).");
                    }
                }
            }
        }
        else
        {
            Log.Write("WARN: runtime manager not reachable yet; UI will keep retrying.");
        }

        var server = new WebServer(_cfg, _plc, exeDir);
        server.Start();
        Log.Write("Web UI listening on " + _cfg.HttpPrefix);
        // Block forever.
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }
}

// ----------------------------------------------------------------------------
// Locates the proprietary Siemens PLCSIM Advanced API DLL on the local machine.
// Order: 'api_dll_path' in appconfig.txt -> PLCSIM_API_DLL env var -> scan Siemens install folders.
internal static class ApiDllLocator
{
    private const string DllName = "Siemens.Simatic.Simulation.Runtime.Api.x64.dll";

    public static string Find(string exeDir)
    {
        // 1) explicit override in appconfig.txt (read directly; full Config not parsed yet)
        try
        {
            string cfg = Path.Combine(exeDir, "appconfig.txt");
            if (File.Exists(cfg))
                foreach (var line in File.ReadAllLines(cfg))
                {
                    string s = line.Trim();
                    if (s.StartsWith("api_dll_path", StringComparison.OrdinalIgnoreCase))
                    {
                        int eq = s.IndexOf('=');
                        if (eq > 0)
                        {
                            string p = s.Substring(eq + 1).Trim();
                            if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
                        }
                    }
                }
        }
        catch { }

        // 2) environment variable
        try
        {
            string env = Environment.GetEnvironmentVariable("PLCSIM_API_DLL");
            if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
        }
        catch { }

        // 3) scan standard Siemens Automation install roots for the newest matching DLL
        var roots = new List<string>();
        foreach (var pf in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                   Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            if (!string.IsNullOrEmpty(pf)) roots.Add(Path.Combine(pf, "Siemens", "Automation"));
        }
        string best = null; DateTime bestTime = DateTime.MinValue;
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var hit in Directory.GetFiles(root, DllName, SearchOption.AllDirectories))
                {
                    try { var t = File.GetLastWriteTimeUtc(hit); if (t > bestTime) { bestTime = t; best = hit; } }
                    catch { }
                }
            }
            catch { }
        }
        return best;
    }
}

// ----------------------------------------------------------------------------
internal sealed class Config
{
    public string Path;
    public string ExeDir;
    public string HttpPrefix = "http://+:8090/";   // bind to all interfaces (LAN) by default; remote access is the main feature
    public string Workspace;            // Documents\PLCSIM\<workspace>
    public string WorkspaceRoot;        // Documents\PLCSIM
    public bool AutostartEnabled = false;
    public string AutostartMode = "last";   // last = restore the set last powered on | fixed = use AutostartInstance
    public string AutostartInstance = "";
    public int MaxPoweredOn = 1;             // OPERATIONAL limit, editable from the web UI
    public int HardMaxPoweredOn = 4;         // HARD safety cap (disk-only); the web UI can never exceed it
    public int BootFailLimit = 2;            // consecutive non-stabilizing boots before entering SAFE MODE
    public int StableSeconds = 90;           // stability window after auto-start to declare a "clean boot"
    public int StartStaggerMs = 4000;        // pause between power-ons during staggered auto-start
    public int HealthProbeIntervalMs = 15000;// how often /health is probed during the stability window
    public int HealthTimeoutMs = 3000;       // strict probe timeout (detects a "soft freeze" of the web layer)
    public List<string> LastRunning = new List<string>(); // last set of powered-on instances
    public string NetworkMode = "Softbus";   // Softbus (zero-config) | TCPIPSingleAdapter | TCPIPMultipleAdapter
    public uint AdapterIndex = 0;            // host adapter ifIndex (only used for TCP/IP modes)
    public string AdapterName = "";
    public string StorageLayout = "default"; // default = native persistent storage (recommended)
    public uint PowerOnTimeout = 60000;
    public uint RunTimeout = 60000;
    public int ConnectWaitSeconds = 120;
    public string ApiDllPath = "";          // optional explicit path to the Siemens API DLL
    public string LogFile;
    public Dictionary<string, string> DesiredIp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // name -> "ip,mask,gw"

    // Effective cap: never exceeds the hard safety cap, even if the web asks for more.
    public int EffectiveMax() { return MaxPoweredOn < HardMaxPoweredOn ? MaxPoweredOn : HardMaxPoweredOn; }

    public bool IsTcpIp() { return NetworkMode.StartsWith("TCPIP", StringComparison.OrdinalIgnoreCase); }

    // Loopback URL to our own web server, for the self health probe (port derived from http_prefix).
    public string SelfHealthUrl()
    {
        string p = HttpPrefix;
        int i = p.IndexOf("://", StringComparison.Ordinal);
        string rest = (i >= 0 ? p.Substring(i + 3) : p).TrimEnd('/');
        int c = rest.LastIndexOf(':');
        string port = c >= 0 ? rest.Substring(c + 1) : "80";
        return "http://127.0.0.1:" + port + "/health";
    }

    public static Config Load(string path, string exeDir)
    {
        var c = new Config { Path = path, ExeDir = exeDir };
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        c.WorkspaceRoot = System.IO.Path.Combine(docs, "PLCSIM");
        c.LogFile = System.IO.Path.Combine(exeDir, "webcontrol.log");

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(path))
            foreach (var line in File.ReadAllLines(path))
            {
                var s = line.Trim();
                if (s.Length == 0 || s.StartsWith("#") || s.StartsWith(";")) continue;
                int eq = s.IndexOf('=');
                if (eq <= 0) continue;
                map[s.Substring(0, eq).Trim()] = s.Substring(eq + 1).Trim();
            }

        Get(map, "http_prefix", ref c.HttpPrefix);
        Get(map, "workspace_root", ref c.WorkspaceRoot);
        Get(map, "workspace", ref c.Workspace);
        GetBool(map, "autostart_enabled", ref c.AutostartEnabled);
        Get(map, "autostart_mode", ref c.AutostartMode);
        Get(map, "autostart_instance", ref c.AutostartInstance);
        int mx = c.MaxPoweredOn; if (map.ContainsKey("max_powered_on") && int.TryParse(map["max_powered_on"], out mx)) c.MaxPoweredOn = mx;
        if (c.MaxPoweredOn < 1) c.MaxPoweredOn = 1;
        int hmx = c.HardMaxPoweredOn; if (map.ContainsKey("hard_max_powered_on") && int.TryParse(map["hard_max_powered_on"], out hmx)) c.HardMaxPoweredOn = hmx;
        if (c.HardMaxPoweredOn < 1) c.HardMaxPoweredOn = 1;
        if (c.MaxPoweredOn > c.HardMaxPoweredOn) c.MaxPoweredOn = c.HardMaxPoweredOn;
        int bfl = c.BootFailLimit; if (map.ContainsKey("boot_fail_limit") && int.TryParse(map["boot_fail_limit"], out bfl)) c.BootFailLimit = bfl;
        if (c.BootFailLimit < 1) c.BootFailLimit = 1;
        int ss = c.StableSeconds; if (map.ContainsKey("stable_seconds") && int.TryParse(map["stable_seconds"], out ss)) c.StableSeconds = ss;
        int sg = c.StartStaggerMs; if (map.ContainsKey("start_stagger_ms") && int.TryParse(map["start_stagger_ms"], out sg)) c.StartStaggerMs = sg;
        int hpi = c.HealthProbeIntervalMs; if (map.ContainsKey("health_probe_interval_ms") && int.TryParse(map["health_probe_interval_ms"], out hpi)) c.HealthProbeIntervalMs = hpi;
        if (c.HealthProbeIntervalMs < 1000) c.HealthProbeIntervalMs = 1000;
        int hto = c.HealthTimeoutMs; if (map.ContainsKey("health_timeout_ms") && int.TryParse(map["health_timeout_ms"], out hto)) c.HealthTimeoutMs = hto;
        if (c.HealthTimeoutMs < 500) c.HealthTimeoutMs = 500;
        if (map.ContainsKey("last_running"))
            foreach (var n in map["last_running"].Split(','))
                if (n.Trim().Length > 0) c.LastRunning.Add(n.Trim());
        Get(map, "network_mode", ref c.NetworkMode);
        GetUInt(map, "adapter_index", ref c.AdapterIndex);
        Get(map, "adapter_name", ref c.AdapterName);
        Get(map, "storage_layout", ref c.StorageLayout);
        GetUInt(map, "poweron_timeout", ref c.PowerOnTimeout);
        GetUInt(map, "run_timeout", ref c.RunTimeout);
        int cw = c.ConnectWaitSeconds; if (map.ContainsKey("connect_wait_seconds") && int.TryParse(map["connect_wait_seconds"], out cw)) c.ConnectWaitSeconds = cw;
        Get(map, "api_dll_path", ref c.ApiDllPath);
        Get(map, "log", ref c.LogFile);
        foreach (var kv in map)
            if (kv.Key.StartsWith("ip.", StringComparison.OrdinalIgnoreCase))
                c.DesiredIp[kv.Key.Substring(3)] = kv.Value;

        // Default workspace = most recently modified folder that has Instance.JSON.
        if (string.IsNullOrEmpty(c.Workspace) && Directory.Exists(c.WorkspaceRoot))
        {
            var ws = Directory.GetDirectories(c.WorkspaceRoot)
                .Where(d => File.Exists(System.IO.Path.Combine(d, "Instance.JSON")))
                .OrderByDescending(d => Directory.GetLastWriteTimeUtc(d)).FirstOrDefault();
            if (ws != null) c.Workspace = ws;
        }
        return c;
    }

    public void Save()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# PLC-WebControl config (auto-written; edit while the service is stopped).");
        sb.AppendLine("# See docs/CONFIGURATION.md for the meaning of every key.");
        sb.AppendLine("http_prefix = " + HttpPrefix);
        sb.AppendLine("workspace_root = " + WorkspaceRoot);
        sb.AppendLine("workspace = " + Workspace);
        sb.AppendLine("autostart_enabled = " + (AutostartEnabled ? "true" : "false"));
        sb.AppendLine("autostart_mode = " + AutostartMode);
        sb.AppendLine("autostart_instance = " + AutostartInstance);
        sb.AppendLine("max_powered_on = " + MaxPoweredOn);
        sb.AppendLine("# hard_max_powered_on: HARD safety cap = the real capacity of this machine. The web UI never exceeds it.");
        sb.AppendLine("hard_max_powered_on = " + HardMaxPoweredOn);
        sb.AppendLine("boot_fail_limit = " + BootFailLimit);
        sb.AppendLine("stable_seconds = " + StableSeconds);
        sb.AppendLine("start_stagger_ms = " + StartStaggerMs);
        sb.AppendLine("health_probe_interval_ms = " + HealthProbeIntervalMs);
        sb.AppendLine("health_timeout_ms = " + HealthTimeoutMs);
        sb.AppendLine("last_running = " + string.Join(",", LastRunning.ToArray()));
        sb.AppendLine("network_mode = " + NetworkMode);
        sb.AppendLine("adapter_index = " + AdapterIndex);
        sb.AppendLine("adapter_name = " + AdapterName);
        sb.AppendLine("storage_layout = " + StorageLayout);
        sb.AppendLine("poweron_timeout = " + PowerOnTimeout);
        sb.AppendLine("run_timeout = " + RunTimeout);
        sb.AppendLine("connect_wait_seconds = " + ConnectWaitSeconds);
        sb.AppendLine("# api_dll_path: optional explicit path to Siemens.Simatic.Simulation.Runtime.Api.x64.dll (auto-detected if blank).");
        sb.AppendLine("api_dll_path = " + ApiDllPath);
        sb.AppendLine("log = " + LogFile);
        foreach (var kv in DesiredIp)
            if (!string.IsNullOrEmpty(kv.Value)) sb.AppendLine("ip." + kv.Key + " = " + kv.Value);
        File.WriteAllText(Path, sb.ToString(), new UTF8Encoding(false));
    }

    // Note: a blank value in the config file means "use the built-in default", so it is ignored here.
    private static void Get(Dictionary<string, string> m, string k, ref string v) { if (m.ContainsKey(k) && m[k].Length > 0) v = m[k]; }
    private static void GetBool(Dictionary<string, string> m, string k, ref bool v) { if (m.ContainsKey(k)) v = m[k].Equals("true", StringComparison.OrdinalIgnoreCase) || m[k] == "1"; }
    private static void GetUInt(Dictionary<string, string> m, string k, ref uint v) { uint t; if (m.ContainsKey(k) && uint.TryParse(m[k], out t)) v = t; }
}

// ----------------------------------------------------------------------------
internal sealed class ActionResult
{
    public bool Ok;
    public string Message;
    public static ActionResult Good(string m) { return new ActionResult { Ok = true, Message = m }; }
    public static ActionResult Bad(string m) { return new ActionResult { Ok = false, Message = m }; }
}

// Boot protections: on-disk attempt counter (loop-breaker) + manual SAFEMODE flag file.
internal static class BootGuard
{
    private static string StateFile(string dir) { return System.IO.Path.Combine(dir, "boot-state.txt"); }
    private static string FlagFile(string dir) { return System.IO.Path.Combine(dir, "SAFEMODE"); }

    public static int ReadAttempts(string dir)
    {
        try
        {
            string f = StateFile(dir);
            if (!File.Exists(f)) return 0;
            foreach (var line in File.ReadAllLines(f))
            {
                string s = line.Trim();
                if (s.StartsWith("attempts=", StringComparison.OrdinalIgnoreCase))
                {
                    int n; if (int.TryParse(s.Substring(9).Trim(), out n)) return n;
                }
            }
        }
        catch { }
        return 0;
    }

    public static void WriteAttempts(string dir, int n)
    {
        try { File.WriteAllText(StateFile(dir), "attempts=" + n + "\r\n"); }
        catch (Exception ex) { Log.Write("WARN BootGuard.WriteAttempts: " + ex.Message); }
    }

    public static bool FlagPresent(string dir)
    {
        try { return File.Exists(FlagFile(dir)); } catch { return false; }
    }

    public static void SetFlag(string dir, bool on)
    {
        try
        {
            string f = FlagFile(dir);
            if (on) { if (!File.Exists(f)) File.WriteAllText(f, "Presence of this file = DO NOT auto-start any instance on the next boot (manual safe mode).\r\n"); }
            else if (File.Exists(f)) File.Delete(f);
        }
        catch (Exception ex) { Log.Write("WARN BootGuard.SetFlag: " + ex.Message); }
    }
}

internal sealed class PlcManager
{
    private readonly Config _cfg;
    private readonly object _gate = new object();   // serializes all power operations -> enforces the limit atomically

    // Safe mode: auto-start was skipped this boot (by the loop-breaker or the manual flag).
    public volatile bool SafeMode = false;
    public string SafeModeReason = "";

    public PlcManager(Config cfg) { _cfg = cfg; }

    public bool WaitForRuntime(int seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            try { if (SimulationRuntimeManager.Version != 0) return true; } catch { }
            Thread.Sleep(2000);
        }
        try { return SimulationRuntimeManager.Version != 0; } catch { return false; }
    }

    public bool RuntimeAvailable()
    {
        try { return SimulationRuntimeManager.Version != 0; } catch { return false; }
    }

    // ----- discovery -----
    public List<Dictionary<string, object>> ListWorkspaces()
    {
        var list = new List<Dictionary<string, object>>();
        if (!Directory.Exists(_cfg.WorkspaceRoot)) return list;
        foreach (var d in Directory.GetDirectories(_cfg.WorkspaceRoot))
        {
            if (!File.Exists(Path.Combine(d, "Instance.JSON"))) continue;
            list.Add(new Dictionary<string, object> {
                { "name", Path.GetFileName(d) },
                { "path", d },
                { "current", string.Equals(d, _cfg.Workspace, StringComparison.OrdinalIgnoreCase) },
            });
        }
        return list;
    }

    // PLCs defined in the current workspace's Instance.JSON, with live runtime state.
    public List<Dictionary<string, object>> ListInstances()
    {
        var result = new List<Dictionary<string, object>>();
        var live = RegisteredState(); // name -> state

        var defs = ReadWorkspacePlcs(_cfg.Workspace);
        foreach (var def in defs)
        {
            string name = def.Item1, plcName = def.Item2; bool configured = def.Item3;
            string state = live.ContainsKey(name) ? live[name][0] : "Off";
            string ip = live.ContainsKey(name) ? live[name][1] : "";
            result.Add(new Dictionary<string, object> {
                { "name", name },
                { "plcName", plcName },
                { "configured", configured },
                { "registered", live.ContainsKey(name) },
                { "state", state },
                { "ip", ip },
                { "poweredOn", state != "Off" && state != "InvalidOperatingState" },
                { "running", state == "Run" },
            });
        }
        // Also surface any registered instance not in the workspace file (e.g. opened by the GUI).
        foreach (var kv in live)
            if (!defs.Any(d => string.Equals(d.Item1, kv.Key, StringComparison.OrdinalIgnoreCase)))
                result.Add(new Dictionary<string, object> {
                    { "name", kv.Key }, { "plcName", "(registered)" }, { "configured", false },
                    { "registered", true }, { "state", kv.Value[0] }, { "ip", kv.Value[1] },
                    { "poweredOn", kv.Value[0] != "Off" }, { "running", kv.Value[0] == "Run" },
                });
        return result;
    }

    public string PoweredOnInstance()
    {
        foreach (var kv in RegisteredState())
            if (kv.Value[0] != "Off" && kv.Value[0] != "InvalidOperatingState") return kv.Key;
        return null;
    }

    // name -> [state, ip]
    private Dictionary<string, string[]> RegisteredState()
    {
        var d = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (!RuntimeAvailable()) return d;
        var mgr = SimulationRuntimeManager.LocalRuntimeManagerInstance;
        foreach (var info in SimulationRuntimeManager.RegisteredInstanceInfo)
        {
            string state = "InvalidOperatingState", ip = "";
            try
            {
                var inst = mgr.CreateInterface(info.Name);
                state = inst.OperatingState.ToString();
                if (inst.OperatingState != EOperatingState.Off)
                {
                    try { var ips = inst.ControllerIP; if (ips != null) ip = string.Join(", ", ips.Where(x => !string.IsNullOrEmpty(x) && x != "0.0.0.0")); }
                    catch { }
                }
            }
            catch { }
            d[info.Name] = new[] { state, ip };
        }
        return d;
    }

    private static List<Tuple<string, string, bool>> ReadWorkspacePlcs(string workspace)
    {
        var list = new List<Tuple<string, string, bool>>();
        if (string.IsNullOrEmpty(workspace)) return list;
        string jf = Path.Combine(workspace, "Instance.JSON");
        if (!File.Exists(jf)) return list;
        try
        {
            var ser = new JavaScriptSerializer();
            var arr = ser.Deserialize<object[]>(File.ReadAllText(jf));
            foreach (var o in arr)
            {
                var m = o as Dictionary<string, object>;
                if (m == null) continue;
                string name = m.ContainsKey("InstanceName") ? Convert.ToString(m["InstanceName"]) : null;
                string plc = m.ContainsKey("PlcName") ? Convert.ToString(m["PlcName"]) : "";
                bool conf = m.ContainsKey("IsConfigured") && Convert.ToBoolean(m["IsConfigured"]);
                if (!string.IsNullOrEmpty(name)) list.Add(Tuple.Create(name, plc, conf));
            }
        }
        catch { }
        return list;
    }

    // ----- network -----
    public void ApplyGlobalNetwork()
    {
        var mgr = SimulationRuntimeManager.LocalRuntimeManagerInstance;
        var desired = (ENetworkMode)Enum.Parse(typeof(ENetworkMode), _cfg.NetworkMode, true);
        if (mgr.NetworkMode != desired)
        {
            Log.Write("Setting NetworkMode " + mgr.NetworkMode + " -> " + desired);
            mgr.NetworkMode = desired;
        }
        // Adapter binding only applies to TCP/IP modes; Softbus needs no host adapter.
        if (!_cfg.IsTcpIp()) return;
        bool alreadyBound = mgr.NetInterfaces.Any(n => n.interfaceIndex == _cfg.AdapterIndex && n.vSwitchBindingEnabled);
        if (!alreadyBound)
        {
            try { mgr.SetNetInterfaceBindings(_cfg.AdapterIndex); Log.Write("Bound adapter idx " + _cfg.AdapterIndex + " (" + _cfg.AdapterName + ")."); }
            catch (Exception ex) { Log.Write("WARN bind adapter: " + ex.Message); }
        }
    }

    public List<Dictionary<string, object>> ListAdapters()
    {
        var list = new List<Dictionary<string, object>>();
        if (!RuntimeAvailable()) return list;
        var mgr = SimulationRuntimeManager.LocalRuntimeManagerInstance;
        foreach (var n in mgr.NetInterfaces)
            list.Add(new Dictionary<string, object> {
                { "index", n.interfaceIndex }, { "name", n.interfaceName },
                { "description", n.interfaceDescription },
                { "bound", n.vSwitchBindingEnabled }, { "connected", n.isConnected },
            });
        return list;
    }

    private void MapInstanceToAdapter(IInstance inst)
    {
        if (!_cfg.IsTcpIp()) return;   // Softbus does not use a host adapter mapping
        try { inst.SetNetInterfaceMapping(EPLCInterface.IE1, _cfg.AdapterIndex); Log.Write("  mapped IE1 -> adapter idx " + _cfg.AdapterIndex); }
        catch (Exception ex) { Log.Write("  WARN map IE1: " + ex.Message); }
    }

    // Re-applies the saved IP for this instance (the API does not persist it across power cycles).
    private void ApplyDesiredIp(IInstance inst, string name)
    {
        string spec;
        if (!_cfg.DesiredIp.TryGetValue(name, out spec) || string.IsNullOrEmpty(spec)) return;
        var p = spec.Split(',');
        string ip = p.Length > 0 ? p[0].Trim() : "";
        if (string.IsNullOrEmpty(ip)) return;
        string mask = p.Length > 1 && p[1].Trim().Length > 0 ? p[1].Trim() : "255.255.255.0";
        string gw = p.Length > 2 && p[2].Trim().Length > 0 ? p[2].Trim() : "0.0.0.0";
        try { inst.SetIPSuite(0, new SIPSuite4(ip, mask, gw), false); Log.Write("  re-applied IP for '" + name + "': " + ip); }
        catch (Exception ex) { Log.Write("  WARN re-apply IP '" + name + "': " + ex.Message); }
    }

    // ----- power control (all serialized through _gate) -----
    private IInstance GetOrRegister(string name)
    {
        var mgr = SimulationRuntimeManager.LocalRuntimeManagerInstance;
        foreach (var info in SimulationRuntimeManager.RegisteredInstanceInfo)
            if (string.Equals(info.Name, name, StringComparison.OrdinalIgnoreCase))
                return mgr.CreateInterface(name);

        // Not registered -> register so the program loads from persistent storage.
        if (_cfg.StorageLayout == "default")
            return SimulationRuntimeManager.RegisterInstance(name);

        string storage = _cfg.StorageLayout == "workspace_instances"
            ? Path.Combine(_cfg.Workspace, "Instances", name)
            : _cfg.Workspace;
        try { Log.Write("  RegisterCustomInstance('" + name + "', '" + storage + "')"); return SimulationRuntimeManager.RegisterCustomInstance(name, storage); }
        catch (Exception ex)
        {
            Log.Write("  WARN RegisterCustomInstance failed (" + ex.Message + "); falling back to RegisterInstance.");
            return SimulationRuntimeManager.RegisterInstance(name);
        }
    }

    // Names of currently powered-on instances (state != Off). Basis of the limiter.
    private List<string> PoweredOnNames()
    {
        var list = new List<string>();
        if (!RuntimeAvailable()) return list;
        var mgr = SimulationRuntimeManager.LocalRuntimeManagerInstance;
        foreach (var info in SimulationRuntimeManager.RegisteredInstanceInfo)
        {
            try { if (mgr.CreateInterface(info.Name).OperatingState != EOperatingState.Off) list.Add(info.Name); }
            catch { }
        }
        return list;
    }

    // Same as PoweredOnNames, exposed for /status.
    public List<string> PoweredOnList() { return PoweredOnNames(); }

    // Persists the set of powered-on instances, so auto-start can restore it.
    private void UpdateLastRunning()
    {
        try
        {
            var on = PoweredOnNames();
            _cfg.LastRunning = on;
            _cfg.Save();
            Log.Write("  last_running = " + (on.Count > 0 ? string.Join(", ", on.ToArray()) : "(none)"));
        }
        catch (Exception ex) { Log.Write("  WARN update last_running: " + ex.Message); }
    }

    // Which instances to start at auto-start, according to the configured mode.
    public List<string> AutostartTargets()
    {
        var list = new List<string>();
        if (string.Equals(_cfg.AutostartMode, "fixed", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrEmpty(_cfg.AutostartInstance)) list.Add(_cfg.AutostartInstance);
        }
        else // "last": restore the last powered-on set
        {
            foreach (var n in _cfg.LastRunning)
                if (!string.IsNullOrEmpty(n) && !list.Contains(n)) list.Add(n);
        }
        int eff = _cfg.EffectiveMax();
        if (list.Count > eff) list = list.GetRange(0, eff);
        return list;
    }

    // STAGGERED auto-start: power on one by one, check health between each, abort the rest on failure.
    // If all come up, schedule the "clean boot" marker that resets the loop-breaker counter.
    public void RunStaggeredAutostart(List<string> targets, string exeDir)
    {
        bool allOk = true;
        for (int i = 0; i < targets.Count; i++)
        {
            string t = targets[i];
            if (!RuntimeAvailable()) { Log.Write("Auto-start ABORTED: runtime not responding before '" + t + "'."); allOk = false; break; }
            ActionResult r;
            try { r = StartInstance(t); }
            catch (Exception ex) { Log.Write("Auto-start ERROR '" + t + "': " + ex.Message); allOk = false; break; }
            Log.Write("Auto-start '" + t + "' (" + (i + 1) + "/" + targets.Count + "): " + r.Message);
            if (!r.Ok) { Log.Write("Auto-start ABORTED after failure on '" + t + "' (remaining instances not started)."); allOk = false; break; }
            if (i < targets.Count - 1) Thread.Sleep(_cfg.StartStaggerMs); // pause so load does not spike all at once
        }
        if (allOk) ScheduleMarkClean(exeDir);
        else Log.Write("Auto-start incomplete; NOT marking a clean boot (counter stays armed for the loop-breaker).");
    }

    // Declares a "clean boot" (counter=0) ONLY if the system passes repeated health probes across the
    // whole stability window. Each probe requires: runtime OK AND our OWN web server answers /health
    // quickly. So a "soft freeze" (processes alive but web inaccessible by timeout) does NOT mark clean,
    // and the loop-breaker stays armed. If the prober cannot even run (frozen), it also won't mark: fail-safe.
    private void ScheduleMarkClean(string exeDir)
    {
        int interval = _cfg.HealthProbeIntervalMs;
        int needed = (_cfg.StableSeconds * 1000 + interval - 1) / interval;
        if (needed < 1) needed = 1;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                for (int i = 0; i < needed; i++)
                {
                    Thread.Sleep(interval);
                    if (!SelfHealthy())
                    {
                        Log.Write("MarkClean: probe " + (i + 1) + "/" + needed + " FAILED (possible soft freeze: web/runtime not responding in time). NOT marking clean; counter stays armed.");
                        return;
                    }
                }
                BootGuard.WriteAttempts(exeDir, 0);
                Log.Write("Stable boot: " + needed + " health probes OK over ~" + _cfg.StableSeconds + "s. Boot-attempt counter reset to 0.");
            }
            catch (Exception ex) { Log.Write("WARN MarkClean: " + ex.Message); }
        });
    }

    // Active health probe: runtime responds AND our own web server answers /health within the timeout.
    private bool SelfHealthy()
    {
        if (!RuntimeAvailable()) return false;
        try
        {
            var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(_cfg.SelfHealthUrl());
            req.Method = "GET";
            req.Timeout = _cfg.HealthTimeoutMs;
            req.ReadWriteTimeout = _cfg.HealthTimeoutMs;
            using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
            {
                using (var sr = new System.IO.StreamReader(resp.GetResponseStream())) { sr.ReadToEnd(); }
                return resp.StatusCode == System.Net.HttpStatusCode.OK;
            }
        }
        catch { return false; }
    }

    public ActionResult StartInstance(string name) { return PowerOn(name, true); }

    public ActionResult PowerOn(string name, bool thenRun)
    {
        lock (_gate)
        {
            if (!RuntimeAvailable()) return ActionResult.Bad("Runtime manager not available.");
            try
            {
                // Instance limiter, enforced in the BACKEND (not just the UI): a power-on is allowed only
                // if the total number of powered-on instances would not exceed the effective cap.
                // It does NOT power off others automatically; it refuses with a clear message.
                var on = PoweredOnNames();
                bool nameAlreadyOn = on.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                var others = on.Where(n => !string.Equals(n, name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (!nameAlreadyOn && others.Count >= _cfg.EffectiveMax())
                {
                    int eff = _cfg.EffectiveMax();
                    string lim = eff == 1 ? "only one PLC powered on at a time" : ("at most " + eff + " PLCs powered on at a time");
                    return ActionResult.Bad("Cannot power on '" + name + "': " + others.Count +
                        " already powered on (" + string.Join(", ", others.ToArray()) + "). Power one off first (" + lim + ").");
                }

                var inst = GetOrRegister(name);
                MapInstanceToAdapter(inst);
                if (inst.OperatingState == EOperatingState.Off)
                {
                    Log.Write("PowerOn '" + name + "'...");
                    var rc = inst.PowerOn(_cfg.PowerOnTimeout);
                    if (rc != ERuntimeErrorCode.OK) return ActionResult.Bad("PowerOn returned " + rc);
                }
                var st = WaitStable(inst, _cfg.PowerOnTimeout);
                ApplyDesiredIp(inst, name);   // re-apply the saved IP (it does not persist by itself between power cycles)
                if (thenRun && st == EOperatingState.Stop)
                {
                    try { inst.Run(_cfg.RunTimeout); }
                    catch (SimulationRuntimeException ex)
                    {
                        if (ex.Message.IndexOf("IsEmpty", StringComparison.OrdinalIgnoreCase) >= 0 || ex.Message.Contains("-52"))
                            return ActionResult.Bad("'" + name + "' powered on but has NO program (download it from TIA Portal). Left in STOP.");
                        return ActionResult.Bad("Run failed: " + ex.Message);
                    }
                }
                UpdateLastRunning();
                return ActionResult.Good("'" + name + "' -> " + inst.OperatingState);
            }
            catch (Exception ex) { Log.Write("PowerOn ERROR: " + ex); return ActionResult.Bad(ex.Message); }
        }
    }

    // Sets the PLC IPv4 suite. Requires the instance to be powered on (Stop/Run).
    public ActionResult SetIp(string name, string ip, string mask, string gw)
    {
        if (string.IsNullOrEmpty(ip)) return ActionResult.Bad("Empty IP.");
        if (string.IsNullOrEmpty(mask)) mask = "255.255.255.0";
        if (string.IsNullOrEmpty(gw)) gw = "0.0.0.0";
        lock (_gate)
        {
            if (!RuntimeAvailable()) return ActionResult.Bad("Runtime manager not available.");
            try
            {
                var mgr = SimulationRuntimeManager.LocalRuntimeManagerInstance;
                bool reg = SimulationRuntimeManager.RegisteredInstanceInfo.Any(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
                if (!reg) return ActionResult.Bad("'" + name + "' is not powered on. Power it on first to set its IP.");
                var inst = mgr.CreateInterface(name);
                if (inst.OperatingState == EOperatingState.Off)
                    return ActionResult.Bad("'" + name + "' is powered off. Power it on first to set its IP.");
                inst.SetIPSuite(0, new SIPSuite4(ip, mask, gw), false);
                // Save it so it is re-applied automatically on every power-on (it does not persist by itself).
                _cfg.DesiredIp[name] = ip + "," + mask + "," + gw;
                _cfg.Save();
                string now = "";
                try { var ips = inst.ControllerIP; if (ips != null) now = string.Join(", ", ips.Where(x => !string.IsNullOrEmpty(x))); } catch { }
                Log.Write("SetIPSuite '" + name + "' -> " + ip + " / " + mask + " gw " + gw + "  (current: " + now + ", saved for re-apply)");
                return ActionResult.Good("IP of '" + name + "' set to " + ip + " (will be re-applied on every power-on)");
            }
            catch (Exception ex) { Log.Write("SetIp ERROR: " + ex.Message); return ActionResult.Bad(ex.Message); }
        }
    }

    public ActionResult Run(string name) { return Op(name, i => i.Run(_cfg.RunTimeout), "RUN"); }
    public ActionResult Stop(string name) { return Op(name, i => i.Stop(_cfg.RunTimeout), "STOP"); }

    public ActionResult PowerOff(string name)
    {
        var r = Op(name, i => { if (i.OperatingState == EOperatingState.Run) { try { i.Stop(_cfg.RunTimeout); } catch { } } i.PowerOff(_cfg.PowerOnTimeout); }, "POWEROFF");
        if (r.Ok) { lock (_gate) UpdateLastRunning(); }
        return r;
    }

    private ActionResult Op(string name, Action<IInstance> act, string label)
    {
        lock (_gate)
        {
            if (!RuntimeAvailable()) return ActionResult.Bad("Runtime manager not available.");
            try
            {
                var mgr = SimulationRuntimeManager.LocalRuntimeManagerInstance;
                bool exists = SimulationRuntimeManager.RegisteredInstanceInfo.Any(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
                if (!exists) return ActionResult.Bad("'" + name + "' is not registered (power it on first).");
                var inst = mgr.CreateInterface(name);
                Log.Write(label + " '" + name + "' (state " + inst.OperatingState + ")");
                act(inst);
                return ActionResult.Good("'" + name + "' -> " + inst.OperatingState);
            }
            catch (SimulationRuntimeException ex)
            {
                if (ex.Message.IndexOf("IsEmpty", StringComparison.OrdinalIgnoreCase) >= 0 || ex.Message.Contains("-52"))
                    return ActionResult.Bad("'" + name + "' has no program loaded.");
                return ActionResult.Bad(ex.Message);
            }
            catch (Exception ex) { Log.Write(label + " ERROR: " + ex); return ActionResult.Bad(ex.Message); }
        }
    }

    private static EOperatingState WaitStable(IInstance inst, uint timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var s = inst.OperatingState;
            if (s == EOperatingState.Stop || s == EOperatingState.Run) return s;
            Thread.Sleep(400);
        }
        return inst.OperatingState;
    }
}

// ----------------------------------------------------------------------------
internal sealed class WebServer
{
    private readonly Config _cfg;
    private readonly PlcManager _plc;
    private readonly string _wwwroot;
    private readonly string _exeDir;
    private readonly HttpListener _listener = new HttpListener();
    private readonly JavaScriptSerializer _json = new JavaScriptSerializer();

    public WebServer(Config cfg, PlcManager plc, string exeDir)
    {
        _cfg = cfg; _plc = plc; _exeDir = exeDir; _wwwroot = Path.Combine(exeDir, "wwwroot");
        _listener.Prefixes.Add(cfg.HttpPrefix);
    }

    public void Start()
    {
        _listener.Start();
        ThreadPool.QueueUserWorkItem(_ => Loop());
    }

    private void Loop()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = _listener.GetContext(); } catch { break; }
            ThreadPool.QueueUserWorkItem(__ => { try { Handle(ctx); } catch (Exception ex) { Log.Write("HTTP ERROR: " + ex.Message); } });
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        string path = ctx.Request.Url.AbsolutePath.TrimEnd('/');
        if (path == "") path = "/";

        // Lightweight health endpoint: does NOT touch the runtime. Used by the stability check
        // (loop-breaker) to detect a "soft freeze" where the web layer becomes inaccessible by timeout.
        if (path == "/health")
        {
            var hb = Encoding.UTF8.GetBytes("ok");
            ctx.Response.StatusCode = 200; ctx.Response.ContentType = "text/plain";
            ctx.Response.ContentLength64 = hb.Length;
            ctx.Response.OutputStream.Write(hb, 0, hb.Length);
            ctx.Response.OutputStream.Close();
            return;
        }
        if (path == "/" || path == "/index.html") { ServeFile("index.html", ctx); return; }
        if (path.StartsWith("/api/")) { Api(path.Substring(5), ctx); return; }
        // static
        ServeFile(path.TrimStart('/'), ctx);
    }

    private void Api(string ep, HttpListenerContext ctx)
    {
        var q = ctx.Request.QueryString;
        Dictionary<string, object> body = ReadJsonBody(ctx);
        Func<string> Name = () => (body != null && body.ContainsKey("name")) ? Convert.ToString(body["name"]) : q["name"];

        switch (ep)
        {
            case "status":
                WriteJson(ctx, new Dictionary<string, object> {
                    { "ok", true },
                    { "runtime", _plc.RuntimeAvailable() },
                    { "workspace", _cfg.Workspace },
                    { "workspaces", _plc.ListWorkspaces() },
                    { "instances", _plc.ListInstances() },
                    { "poweredOn", _plc.PoweredOnInstance() },
                    { "poweredOnList", _plc.PoweredOnList() },
                    { "maxPoweredOn", _cfg.MaxPoweredOn },
                    { "hardMaxPoweredOn", _cfg.HardMaxPoweredOn },
                    { "effectiveMax", _cfg.EffectiveMax() },
                    { "safeMode", _plc.SafeMode },
                    { "safeModeReason", _plc.SafeModeReason },
                    { "suppressNextBoot", BootGuard.FlagPresent(_exeDir) },
                    { "network", new Dictionary<string,object> { { "mode", _cfg.NetworkMode }, { "adapterIndex", _cfg.AdapterIndex }, { "adapterName", _cfg.AdapterName } } },
                    { "autostart", new Dictionary<string,object> { { "enabled", _cfg.AutostartEnabled }, { "mode", _cfg.AutostartMode }, { "instance", _cfg.AutostartInstance }, { "last", _cfg.LastRunning } } },
                });
                return;
            case "adapters": WriteJson(ctx, new Dictionary<string, object> { { "ok", true }, { "adapters", _plc.ListAdapters() } }); return;
            case "start": WriteResult(ctx, _plc.PowerOn(Name(), true)); return;
            case "poweron": WriteResult(ctx, _plc.PowerOn(Name(), false)); return;
            case "run": WriteResult(ctx, _plc.Run(Name())); return;
            case "setip":
                {
                    string ip = body != null && body.ContainsKey("ip") ? Convert.ToString(body["ip"]) : q["ip"];
                    string mask = body != null && body.ContainsKey("mask") ? Convert.ToString(body["mask"]) : q["mask"];
                    string gw = body != null && body.ContainsKey("gateway") ? Convert.ToString(body["gateway"]) : q["gateway"];
                    WriteResult(ctx, _plc.SetIp(Name(), ip, mask, gw));
                    return;
                }
            case "stop": WriteResult(ctx, _plc.Stop(Name())); return;
            case "poweroff": WriteResult(ctx, _plc.PowerOff(Name())); return;
            case "select-workspace":
                {
                    string ws = Name();
                    var match = _plc.ListWorkspaces().FirstOrDefault(w => string.Equals((string)w["name"], ws, StringComparison.OrdinalIgnoreCase));
                    if (match == null) { WriteResult(ctx, ActionResult.Bad("workspace not found")); return; }
                    _cfg.Workspace = (string)match["path"]; _cfg.Save();
                    WriteResult(ctx, ActionResult.Good("workspace -> " + ws));
                    return;
                }
            case "autostart":
                {
                    if (body != null)
                    {
                        if (body.ContainsKey("enabled")) _cfg.AutostartEnabled = Convert.ToBoolean(body["enabled"]);
                        if (body.ContainsKey("mode")) _cfg.AutostartMode = Convert.ToString(body["mode"]);
                        if (body.ContainsKey("instance")) _cfg.AutostartInstance = Convert.ToString(body["instance"]);
                    }
                    _cfg.Save();
                    bool fixedMode = string.Equals(_cfg.AutostartMode, "fixed", StringComparison.OrdinalIgnoreCase);
                    string desc = fixedMode ? ("fixed: " + _cfg.AutostartInstance) : "last running set";
                    WriteResult(ctx, ActionResult.Good("auto-start " + (_cfg.AutostartEnabled ? "ON" : "OFF") + " (" + desc + ")"));
                    return;
                }
            case "limit":
                {
                    int newMax = _cfg.MaxPoweredOn;
                    if (body != null && body.ContainsKey("max")) { try { newMax = Convert.ToInt32(body["max"]); } catch { } }
                    else if (!string.IsNullOrEmpty(q["max"])) int.TryParse(q["max"], out newMax);
                    if (newMax < 1) newMax = 1;
                    bool clamped = newMax > _cfg.HardMaxPoweredOn;
                    if (clamped) newMax = _cfg.HardMaxPoweredOn;   // never exceeds the hard safety cap
                    _cfg.MaxPoweredOn = newMax; _cfg.Save();
                    string msg = "operational power-on limit = " + newMax;
                    if (clamped) msg += " (clamped to the safety cap " + _cfg.HardMaxPoweredOn + ")";
                    WriteResult(ctx, clamped ? ActionResult.Bad(msg) : ActionResult.Good(msg));
                    return;
                }
            case "safemode":
                {
                    bool on = true;
                    if (body != null && body.ContainsKey("enabled")) { try { on = Convert.ToBoolean(body["enabled"]); } catch { } }
                    else if (!string.IsNullOrEmpty(q["enabled"])) on = (q["enabled"] == "1" || string.Equals(q["enabled"], "true", StringComparison.OrdinalIgnoreCase));
                    BootGuard.SetFlag(_exeDir, on);
                    WriteResult(ctx, ActionResult.Good(on
                        ? "Safe mode ARMED: the next boot will NOT auto-start instances."
                        : "Safe mode disarmed: the next boot will auto-start normally."));
                    return;
                }
            case "clear-safemode":
                {
                    BootGuard.SetFlag(_exeDir, false);
                    BootGuard.WriteAttempts(_exeDir, 0);
                    _plc.SafeMode = false; _plc.SafeModeReason = "";
                    WriteResult(ctx, ActionResult.Good("Auto-start re-enabled: boot-attempt counter reset and safe-mode flag cleared."));
                    return;
                }
            case "network":
                {
                    if (body != null)
                    {
                        if (body.ContainsKey("mode")) _cfg.NetworkMode = Convert.ToString(body["mode"]);
                        if (body.ContainsKey("adapterIndex")) _cfg.AdapterIndex = Convert.ToUInt32(body["adapterIndex"]);
                        if (body.ContainsKey("adapterName")) _cfg.AdapterName = Convert.ToString(body["adapterName"]);
                    }
                    _cfg.Save();
                    try { _plc.ApplyGlobalNetwork(); WriteResult(ctx, ActionResult.Good("network -> " + _cfg.NetworkMode + (_cfg.IsTcpIp() ? " / " + _cfg.AdapterName : ""))); }
                    catch (Exception ex) { WriteResult(ctx, ActionResult.Bad("saved, but applying failed: " + ex.Message)); }
                    return;
                }
            default: ctx.Response.StatusCode = 404; WriteJson(ctx, new Dictionary<string, object> { { "ok", false }, { "error", "unknown endpoint" } }); return;
        }
    }

    private Dictionary<string, object> ReadJsonBody(HttpListenerContext ctx)
    {
        if (!ctx.Request.HasEntityBody) return null;
        using (var sr = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
        {
            string raw = sr.ReadToEnd();
            if (string.IsNullOrWhiteSpace(raw)) return null;
            try { return _json.Deserialize<Dictionary<string, object>>(raw); } catch { return null; }
        }
    }

    private void ServeFile(string rel, HttpListenerContext ctx)
    {
        string full = Path.GetFullPath(Path.Combine(_wwwroot, rel));
        if (!full.StartsWith(Path.GetFullPath(_wwwroot), StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        { ctx.Response.StatusCode = 404; ctx.Response.Close(); return; }
        string ext = Path.GetExtension(full).ToLowerInvariant();
        ctx.Response.ContentType = ext == ".html" ? "text/html; charset=utf-8"
            : ext == ".js" ? "application/javascript" : ext == ".css" ? "text/css"
            : ext == ".svg" ? "image/svg+xml" : "application/octet-stream";
        byte[] data = File.ReadAllBytes(full);
        ctx.Response.ContentLength64 = data.Length;
        ctx.Response.OutputStream.Write(data, 0, data.Length);
        ctx.Response.Close();
    }

    private void WriteResult(HttpListenerContext ctx, ActionResult r)
    { WriteJson(ctx, new Dictionary<string, object> { { "ok", r.Ok }, { "message", r.Message } }); }

    private void WriteJson(HttpListenerContext ctx, object o)
    {
        byte[] data = Encoding.UTF8.GetBytes(_json.Serialize(o));
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.Headers["Cache-Control"] = "no-store";
        ctx.Response.ContentLength64 = data.Length;
        ctx.Response.OutputStream.Write(data, 0, data.Length);
        ctx.Response.Close();
    }
}

// ----------------------------------------------------------------------------
internal static class Log
{
    public static string File;
    private static readonly object _l = new object();
    public static void Write(string msg)
    {
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg;
        Console.WriteLine(line);
        try { lock (_l) { if (!string.IsNullOrEmpty(File)) System.IO.File.AppendAllText(File, line + Environment.NewLine); } } catch { }
    }
}
