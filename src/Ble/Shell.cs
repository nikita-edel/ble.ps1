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
