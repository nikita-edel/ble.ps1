using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace Ble;

/// <summary>
/// Whether PowerShell can run a command that Get-Command lists from a module that isn't imported yet,
/// found out without touching the user's session. Get-Command also lists commands of
/// Windows-PowerShell-only modules (ISE, PSScheduledJob ...) that the real lookup doesn't find, but
/// asking the real lookup imports the module: 0.3-1.5 s on the key that completes the name. So a
/// background thread with its own runspace reads which modules can be loaded (Get-Module
/// -ListAvailable, from the manifests, nothing is imported) and uses the real lookup only for commands
/// of the other modules (some of them load through Windows PowerShell compatibility). Until it
/// knows, a command counts as found.
/// </summary>
sealed class ModuleCommandCheck
{
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _results = new(StringComparer.OrdinalIgnoreCase);
    readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _queued = new(StringComparer.OrdinalIgnoreCase);
    readonly System.Collections.Concurrent.BlockingCollection<(string? Name, string Module)> _queue = new();
    volatile HashSet<string>? _loadable;
    Thread? _thread;

    public bool Exists(string name, string module)
    {
        if (_results.TryGetValue(name, out var r)) return r;
        if (_loadable is { } l && l.Contains(module)) return true;
        if (_queued.TryAdd(name, 0)) Enqueue((name, module));
        return true;
    }

    /// <summary>The module index was rebuilt (PSModulePath changed): read the loadable modules again.</summary>
    public void Reset()
    {
        _loadable = null;
        _results.Clear();
        _queued.Clear();
        Enqueue((null, ""));
    }

    void Enqueue((string?, string) item)
    {
        if (_thread == null)
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "ble module check" };
            _thread.Start();
        }
        _queue.Add(item);
    }

    void Run()
    {
        try
        {
            using var rs = RunspaceFactory.CreateRunspace(InitialSessionState.CreateDefault2());
            rs.Open();
            foreach (var (name, module) in _queue.GetConsumingEnumerable())
            {
                using var ps = PowerShell.Create();
                ps.Runspace = rs;
                try
                {
                    if (name == null)
                    {
                        ps.AddCommand("Get-Module").AddParameter("ListAvailable").AddParameter("ErrorAction", ActionPreference.Ignore);
                        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var o in ps.Invoke())
                            if (o?.BaseObject is PSModuleInfo mi) set.Add(mi.Name);
                        _loadable = set;
                        continue;
                    }
                    if (_loadable is { } l && l.Contains(module)) { _results[name] = true; continue; }
                    ps.AddScript("$null -ne $ExecutionContext.InvokeCommand.GetCommand($args[0], 'All')").AddArgument(WildcardPattern.Escape(name));
                    var res = ps.Invoke();
                    _results[name] = res.Count == 0 || res[0]?.BaseObject is not false;
                }
                catch { if (name != null) _results[name] = true; }
            }
        }
        catch { }
    }
}

/// <summary>
/// Name indexes for fast command lookup: executables and scripts in PATH (rebuilt when PATH or a PATH
/// directory changes; checked at most once a second) and the commands of all modules (rebuilt when
/// PSModulePath changes).
/// </summary>
sealed class CommandIndex
{
    Dictionary<string, CommandKind>? _path;
    string? _pathVar;
    (string Dir, DateTime Time)[] _dirs = [];
    long _checkedAt;

    Dictionary<string, CommandKind>? _modules;
    string? _modulePathVar;

    public CommandKind Path(string name)
    {
        var now = Environment.TickCount64;
        if (_path == null || now - _checkedAt > 1000)
        {
            _checkedAt = now;
            var pv = Environment.GetEnvironmentVariable("PATH") ?? "";
            if (_path == null || pv != _pathVar || _dirs.Any(d => DirTime(d.Dir) != d.Time)) BuildPath(pv);
        }
        return _path!.TryGetValue(name, out var k) ? k : CommandKind.NotFound;
    }

    public CommandKind Module(string name, Func<Dictionary<string, CommandKind>> build)
    {
        var mp = Environment.GetEnvironmentVariable("PSModulePath") ?? "";
        if (_modules == null || mp != _modulePathVar)
        {
            _modulePathVar = mp;
            try { _modules = build(); }
            catch { _modules = new(StringComparer.OrdinalIgnoreCase); }
        }
        return _modules.TryGetValue(name, out var k) ? k : CommandKind.NotFound;
    }

    static DateTime DirTime(string dir)
    {
        try { return Directory.GetLastWriteTimeUtc(dir); } catch { return default; }
    }

    void BuildPath(string pathVar)
    {
        _pathVar = pathVar;
        bool windows = OperatingSystem.IsWindows();
        // program names are case sensitive off Windows: GIT doesn't run git
        var names = windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var map = new Dictionary<string, CommandKind>(names);
        var exts = new HashSet<string>((Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        var dirs = new List<(string, DateTime)>();
        foreach (var dir in pathVar.Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Distinct(names))
        {
            dirs.Add((dir, DirTime(dir)));
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir); } catch { continue; }
            try
            {
                foreach (var f in files)
                {
                    var file = System.IO.Path.GetFileName(f);
                    var ext = System.IO.Path.GetExtension(file);
                    // the first PATH directory wins, as in PowerShell
                    if (ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
                    {
                        map.TryAdd(file, CommandKind.Script);
                        map.TryAdd(System.IO.Path.GetFileNameWithoutExtension(file), CommandKind.Script);
                    }
                    else if (!windows) map.TryAdd(file, CommandKind.Application);
                    else if (exts.Contains(ext))
                    {
                        map.TryAdd(file, CommandKind.Application);
                        map.TryAdd(System.IO.Path.GetFileNameWithoutExtension(file), CommandKind.Application);
                    }
                }
            }
            catch { }
        }
        _dirs = dirs.ToArray();
        _path = map;
    }
}
