using System.Management.Automation;
using System.Management.Automation.Runspaces;

namespace Ble;

public enum CommandKind { NotFound, Alias, Function, Cmdlet, Application, Script }

public enum PathKind { None, Directory, File, Executable, Link }

/// <summary>Variable state used for highlighting (ble.sh varname_* faces).</summary>
public enum VarKind { Unset, Normal, Export, Array, Hash, Readonly, Number, Empty }

public sealed record CompletionItem(string CompletionText, string ListItemText, string ToolTip, CompletionResultType Type);

public sealed record CompletionSet(int ReplacementIndex, int ReplacementLength, List<CompletionItem> Items);

/// <summary>Everything the editor needs from the PowerShell session.</summary>
public interface IShell
{
    /// <summary>TabExpansion2. With <paramref name="cancel"/>, the completion is stopped (and null returned)
    /// as soon as it returns true, e.g. when a key is pressed.</summary>
    CompletionSet? Complete(string text, int cursor, Func<bool>? cancel = null);
    /// <summary>Optional work done while the user is idle (warms caches). Returns true when there is nothing left to do.</summary>
    bool Warmup() => true;
    CommandKind ResolveCommand(string name);
    PathKind ResolvePath(string path);
    VarKind GetVariableKind(string name);
    /// <summary>The value of a variable holding one string or path ($ps_config, $env:APPDATA), else null.</summary>
    string? GetVariableText(string name) => null;
    /// <summary>Parameter names and aliases of a command, or null if they cannot be checked (simple functions, native programs).</summary>
    IReadOnlyCollection<string>? GetParameterNames(string command);
    string? InvokePrompt();
    string CurrentDirectory { get; }
    /// <summary>Runs a command with its output going to the host (ble-bind -c).</summary>
    void RunCommand(ScriptBlock sb);
    /// <summary>Runs an edit command (ble-bind -x, user widgets) with READLINE_LINE/POINT/MARK.</summary>
    (string Line, int Point, int Mark) RunEditCommand(ScriptBlock sb, string line, int point, int mark);
    /// <summary>Evaluates a scriptblock and returns its output as a string.</summary>
    string Evaluate(ScriptBlock sb);
}

/// <summary>IShell implementation backed by the current runspace.</summary>
public sealed class PsShell : IShell
{
    readonly EngineIntrinsics? _engine;

    public PsShell(EngineIntrinsics? engine) { _engine = engine; }

    static PowerShell NewPs() => PowerShell.Create(RunspaceMode.CurrentRunspace);

    public CompletionSet? Complete(string text, int cursor, Func<bool>? cancel = null)
    {
        try
        {
            using var ps = NewPs();
            // stop the pipeline from a timer thread when cancel() says so (PowerShell.BeginStop is thread-safe)
            int stopped = 0;
            using var timer = cancel == null ? null : new Timer(_ =>
            {
                try { if (cancel() && Interlocked.Exchange(ref stopped, 1) == 0) ps.BeginStop(null, null); } catch { }
            }, null, 5, 5);
            var r = CommandCompletion.CompleteInput(text, cursor, null, ps);
            if (r == null || Volatile.Read(ref stopped) != 0) return null;
            var items = new List<CompletionItem>(r.CompletionMatches.Count);
            foreach (var m in r.CompletionMatches)
                items.Add(new CompletionItem(m.CompletionText, m.ListItemText ?? m.CompletionText, m.ToolTip ?? "", m.ResultType));
            return new CompletionSet(r.ReplacementIndex, r.ReplacementLength, items);
        }
        catch { return null; }
    }

    // ---- command lookup ----
    // GetCommand on a name that does not exist takes ~20 ms (it searches every PATH directory and
    // module), and highlighting looks up every prefix while a command name is typed. So commands are
    // looked up directly in the session (aliases, functions, loaded cmdlets: ~0.03 ms) and in indexes
    // of PATH and of all modules' commands; GetCommand is only used for path-like names.

    static readonly CommandIndex Index = new();
    static bool _warmedUp;

    public CommandKind ResolveCommand(string name)
    {
        if (_engine == null || name.Length == 0) return CommandKind.NotFound;
        try
        {
            if (name.IndexOfAny(['\\', '/']) >= 0 || (name.Length > 1 && name[1] == ':'))
            {
                var ci = _engine.InvokeCommand.GetCommand(WildcardPattern.Escape(name), CommandTypes.All);
                return ci == null ? CommandKind.NotFound : Map(ci.CommandType);
            }
            var k = Lookup(name);
            // PowerShell's default verb: "childitem" runs Get-ChildItem
            if (k == CommandKind.NotFound && !name.Contains('-')) k = Lookup("Get-" + name) is var g && g is CommandKind.Cmdlet or CommandKind.Function ? g : CommandKind.NotFound;
            return k;
        }
        catch { return CommandKind.NotFound; }
    }

    CommandKind Lookup(string name)
    {
        var item = _engine!.InvokeProvider.Item;
        if (item.Exists("alias:" + name, false, true)) return CommandKind.Alias;
        if (item.Exists("function:" + name, false, true)) return CommandKind.Function;
        if (_engine.InvokeCommand.GetCmdlet(name) != null) return CommandKind.Cmdlet;
        var m = Index.Module(name, BuildModuleIndex);
        if (m != CommandKind.NotFound && Check.Exists(name, ModuleOf.GetValueOrDefault(name, ""))) return m;
        return Index.Path(name);
    }

    static readonly ModuleCommandCheck Check = new();
    static Dictionary<string, string> ModuleOf = new(StringComparer.OrdinalIgnoreCase);

    // commands of all available modules, also those not imported yet (they are imported on first use)
    Dictionary<string, CommandKind> BuildModuleIndex()
    {
        var d = new Dictionary<string, CommandKind>(StringComparer.OrdinalIgnoreCase);
        var modules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var ps = NewPs();
        ps.AddCommand("Get-Command").AddParameter("CommandType", CommandTypes.Alias | CommandTypes.Function | CommandTypes.Filter | CommandTypes.Cmdlet | CommandTypes.Configuration)
            .AddParameter("ErrorAction", ActionPreference.Ignore);
        foreach (var o in ps.Invoke())
            if (o?.BaseObject is CommandInfo ci && d.TryAdd(ci.Name, Map(ci.CommandType))) modules[ci.Name] = ci.ModuleName ?? "";
        ModuleOf = modules;
        Check.Reset();
        return d;
    }

    /// <summary>Idle work: builds the command indexes and runs the (slow) first completion once.</summary>
    public bool Warmup()
    {
        if (_engine == null || _warmedUp) return true;
        _warmedUp = true;
        try
        {
            Index.Module("", BuildModuleIndex);
            Index.Path("");
            using var ps = NewPs();
            CommandCompletion.CompleteInput("Get-", 4, null, ps);
        }
        catch { }
        return true;
    }

    static CommandKind Map(CommandTypes t) => t switch
    {
        CommandTypes.Alias => CommandKind.Alias,
        CommandTypes.Function or CommandTypes.Filter or CommandTypes.Configuration => CommandKind.Function,
        CommandTypes.Cmdlet => CommandKind.Cmdlet,
        CommandTypes.ExternalScript => CommandKind.Script,
        CommandTypes.Application => CommandKind.Application,
        _ => CommandKind.Function,
    };

    public string CurrentDirectory
    {
        get
        {
            try { return _engine?.SessionState.Path.CurrentFileSystemLocation.ProviderPath ?? Environment.CurrentDirectory; }
            catch { return Environment.CurrentDirectory; }
        }
    }

    public PathKind ResolvePath(string path) => FileSystemPaths.Resolve(path, CurrentDirectory);

    public VarKind GetVariableKind(string name)
    {
        try
        {
            if (name.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
                return Environment.GetEnvironmentVariable(name.Substring(4)) != null ? VarKind.Export : VarKind.Unset;
            if (_engine == null) return VarKind.Normal;
            var v = _engine.SessionState.PSVariable.Get(name);
            if (v == null) return VarKind.Unset;
            var val = v.Value is PSObject p ? p.BaseObject : v.Value;
            // same order of precedence as ble.sh: array, hash, readonly, number, empty
            if (val is System.Collections.IList) return VarKind.Array;
            if (val is System.Collections.IDictionary) return VarKind.Hash;
            if ((v.Options & (ScopedItemOptions.ReadOnly | ScopedItemOptions.Constant)) != 0) return VarKind.Readonly;
            if (val is int or long or short or byte or sbyte or uint or ulong or ushort or System.Numerics.BigInteger) return VarKind.Number;
            if (val == null || val is string { Length: 0 }) return VarKind.Empty;
            return VarKind.Normal;
        }
        catch { return VarKind.Normal; }
    }

    public string? GetVariableText(string name)
    {
        try
        {
            if (name.StartsWith("env:", StringComparison.OrdinalIgnoreCase)) return Environment.GetEnvironmentVariable(name.Substring(4));
            var v = _engine?.SessionState.PSVariable.GetValue(name);
            return (v is PSObject p ? p.BaseObject : v) switch
            {
                string s => s,
                PathInfo pi => pi.ProviderPath,
                FileSystemInfo fi => fi.FullName,
                _ => null,
            };
        }
        catch { return null; }
    }

    public IReadOnlyCollection<string>? GetParameterNames(string command)
    {
        if (_engine == null) return null;
        try
        {
            var ci = _engine.InvokeCommand.GetCommand(WildcardPattern.Escape(command), CommandTypes.All);
            for (int depth = 0; ci is AliasInfo ai && depth < 8; depth++) ci = ai.ResolvedCommand;
            if (ci is FunctionInfo fi && !fi.CmdletBinding) return null;   // simple functions take any arguments
            if (ci is not (CmdletInfo or FunctionInfo)) return null;
            var names = new List<string>();
            foreach (var kv in ci.Parameters)
            {
                names.Add(kv.Key);
                names.AddRange(kv.Value.Aliases);
            }
            return names;
        }
        catch { return null; }
    }

    public string? InvokePrompt()
    {
        try
        {
            using var ps = NewPs();
            ps.AddCommand("prompt");
            var r = ps.Invoke();
            return string.Concat(r.Select(o => o?.ToString()));
        }
        catch { return null; }
    }

    public void RunCommand(ScriptBlock sb)
    {
        using var ps = NewPs();
        ps.AddScript("param($__ble_sb) & $__ble_sb 2>&1 | Out-Default", false).AddArgument(sb);
        ps.Invoke();
    }

    public (string Line, int Point, int Mark) RunEditCommand(ScriptBlock sb, string line, int point, int mark)
    {
        using var ps = NewPs();
        // output of the command is shown (like bash bind -x); the editor redraws its line afterwards
        ps.AddScript(
            "param($__ble_sb, $__ble_l, $__ble_p, $__ble_m) " +
            "$global:READLINE_LINE = $__ble_l; $global:READLINE_POINT = $__ble_p; $global:READLINE_MARK = $__ble_m; " +
            "& $__ble_sb 2>&1 | Out-Default; [string]$global:READLINE_LINE; [int]$global:READLINE_POINT; [int]$global:READLINE_MARK", false)
            .AddArgument(sb).AddArgument(line).AddArgument(point).AddArgument(mark);
        var r = ps.Invoke();
        if (r.Count >= 3)
        {
            var l = r[r.Count - 3]?.ToString() ?? line;
            int p = r[r.Count - 2]?.BaseObject is int i ? i : point;
            int m = r[r.Count - 1]?.BaseObject is int j ? j : mark;
            return (l, Math.Clamp(p, 0, l.Length), Math.Clamp(m, -1, l.Length));
        }
        return (line, point, mark);
    }

    public string Evaluate(ScriptBlock sb)
    {
        try
        {
            using var ps = NewPs();
            ps.AddScript("param($__ble_sb) & $__ble_sb", false).AddArgument(sb);
            return string.Concat(ps.Invoke().Select(o => o?.ToString()));
        }
        catch { return ""; }
    }
}

public static class FileSystemPaths
{
    static readonly string[] ExeExtensions = [".exe", ".com", ".bat", ".cmd", ".ps1", ".msi", ".sh"];

    /// <summary>Classifies a (possibly relative) file system path without touching other providers.</summary>
    public static PathKind Resolve(string path, string cwd)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || path.Length > 400) return PathKind.None;
            if (path.IndexOfAny(['*', '?', '[', '\n', '"', '<', '>', '|']) >= 0) return PathKind.None;
            if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) return PathKind.None;
            int colon = path.IndexOf(':');
            if (colon >= 0 && !(colon == 1 && char.IsLetter(path[0]))) return PathKind.None; // provider path like env: or HKLM:
            string p = path;
            if (p == "~" || p.StartsWith("~/", StringComparison.Ordinal) || p.StartsWith(@"~\", StringComparison.Ordinal))
                p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p.Substring(1);
            p = Path.IsPathRooted(p) ? p : Path.Combine(cwd, p);
            bool isDir = Directory.Exists(p);
            if ((isDir || File.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) return PathKind.Link;
            if (isDir) return PathKind.Directory;
            if (File.Exists(p))
            {
                var ext = Path.GetExtension(p);
                return ExeExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase) ? PathKind.Executable : PathKind.File;
            }
        }
        catch { }
        return PathKind.None;
    }
}

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
        var map = new Dictionary<string, CommandKind>(StringComparer.OrdinalIgnoreCase);
        bool windows = OperatingSystem.IsWindows();
        var exts = new HashSet<string>((Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        var dirs = new List<(string, DateTime)>();
        foreach (var dir in pathVar.Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase))
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
