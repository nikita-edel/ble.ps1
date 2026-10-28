using System.Management.Automation;

namespace Ble;

/// <summary>ble-bind: bind keys to widgets or commands.</summary>
/// <example>ble-bind -f 'C-x C-e' edit-and-execute-command</example>
/// <example>ble-bind -m vi_nmap -f 'K' history-prev</example>
/// <example>ble-bind -c 'M-g' { git status }</example>
/// <example>ble-bind -x 'C-t' { $global:READLINE_LINE += (Get-Date -f s) }</example>
/// <example>ble-bind -m vi_xmap -s 'Y' '" + y'   # keyboard macro: yank selection to the clipboard</example>
[Cmdlet(VerbsLifecycle.Invoke, "BleBind", DefaultParameterSetName = "Print")]
[Alias("ble-bind")]
public sealed class InvokeBleBindCommand : PSCmdlet
{
    [Parameter][Alias("m")] public string? Keymap { get; set; }
    [Parameter(ParameterSetName = "Function")][Alias("f")] public string? Function { get; set; }
    [Parameter(ParameterSetName = "Command")][Alias("c")] public string? Command { get; set; }
    [Parameter(ParameterSetName = "Execute")][Alias("x")] public string? Execute { get; set; }
    [Parameter(ParameterSetName = "Macro")][Alias("s")] public string? Macro { get; set; }
    [Parameter(Position = 0, ParameterSetName = "Function")]
    [Parameter(Position = 0, ParameterSetName = "Macro")]
    [Parameter(Position = 0, ParameterSetName = "Command")]
    [Parameter(Position = 0, ParameterSetName = "Execute")]
    [AllowEmptyString, AllowNull]
    public object? Action { get; set; }
    [Parameter(ParameterSetName = "Print")][Alias("P")] public SwitchParameter Print { get; set; }
    [Parameter(ParameterSetName = "List")][Alias("L")] public SwitchParameter ListWidgets { get; set; }
    [Parameter(ParameterSetName = "ReadKey")][Alias("k")] public SwitchParameter ReadKey { get; set; }
    /// <summary>ble.sh --cursor: DECSCUSR cursor shape for the keymap (sets bleopt keymap_KEYMAP_cursor).</summary>
    [Parameter(ParameterSetName = "Cursor", Mandatory = true)] public string? Cursor { get; set; }

    protected override void ProcessRecord()
    {
        if (Cursor != null)
        {
            var km0 = Keymap ?? (Options.Get("default_keymap") == "vi" ? "vi_imap" : "emacs");
            var opt = $"keymap_{km0}_cursor";
            if (!Options.Exists(opt)) { WriteError(new ErrorRecord(new ArgumentException($"ble-bind: -cursor is not supported for keymap '{km0}'"), "BadKeymap", ErrorCategory.InvalidArgument, km0)); return; }
            Options.Set(opt, Cursor);
            return;
        }
        if (ListWidgets)
        {
            var names = new SortedSet<string>(Editor.Widgets.Keys, StringComparer.Ordinal);
            foreach (var u in Editor.UserWidgets.Keys) names.Add(u);
            foreach (var f in SessionState.InvokeCommand.GetCommands("ble/widget/*", CommandTypes.Function, true))
                names.Add(f.Name.Substring("ble/widget/".Length));
            foreach (var w in names) WriteObject(w);
            return;
        }
        if (ReadKey)
        {
            Host.UI.Write("Press a key: ");
            var k = KeyNames.FromConsoleKey(Console.ReadKey(true));
            Host.UI.WriteLine(k?.Name ?? "(unknown)");
            return;
        }
        var kmName = Keymap ?? (Options.Get("default_keymap") == "vi" ? "vi_imap" : "emacs");
        var km = Ble.Keymaps.Get(kmName);
        if (Function == null && Command == null && Execute == null && Macro == null)
        {
            foreach (var name in Keymap != null ? [Keymap] : Ble.Keymaps.Names)
            {
                foreach (var kv in Ble.Keymaps.Get(name).Bindings)
                {
                    var spec = "'" + kv.Key.Replace("'", "''") + "'";
                    var b = kv.Value;
                    WriteObject(b.Kind switch
                    {
                        BindingKind.Widget => $"ble-bind -m {name} -f {spec} {b.Widget}",
                        BindingKind.Command => $"ble-bind -m {name} -c {spec} {{{b.Script}}}",
                        BindingKind.Macro => $"ble-bind -m {name} -s {spec} '{b.Widget.Replace("'", "''")}'",
                        BindingKind.UserWidget => $"ble-bind -m {name} -f {spec} {b.Widget}",
                        _ => $"ble-bind -m {name} -x {spec} {{{b.Script}}}",
                    });
                }
            }
            return;
        }
        var keys = Function ?? Command ?? Execute ?? Macro!;
        string[] seq;
        try { seq = KeyNames.ParseSpec(keys); }
        catch (FormatException ex) { ThrowTerminatingError(new ErrorRecord(ex, "BadKey", ErrorCategory.InvalidArgument, keys)); return; }
        var act = Action is PSObject p ? p.BaseObject : Action;
        if (act == null || (act is string s0 && (s0.Length == 0 || s0 == "-")))
        {
            km.Bind(seq, null);
            return;
        }
        Binding binding;
        if (Macro != null)
        {
            var target = act.ToString()!;
            try { KeyNames.ParseSpec(target); }
            catch (FormatException ex) { ThrowTerminatingError(new ErrorRecord(ex, "BadKey", ErrorCategory.InvalidArgument, target)); return; }
            binding = new Binding(BindingKind.Macro, target);
        }
        else if (Function != null && act is string widget)
        {
            if (Editor.Widgets.ContainsKey(widget)) binding = Binding.W(widget);
            else if (Editor.UserWidgets.ContainsKey(widget)
                     || SessionState.InvokeCommand.GetCommand("ble/widget/" + widget, CommandTypes.Function) != null)
                binding = new Binding(BindingKind.UserWidget, widget);
            else
            {
                ThrowTerminatingError(new ErrorRecord(new ArgumentException($"ble-bind: unknown widget '{widget}' (see ble-bind -L)"), "UnknownWidget", ErrorCategory.InvalidArgument, widget));
                return;
            }
        }
        else
        {
            var sb = act as ScriptBlock ?? ScriptBlock.Create(act.ToString()!);
            binding = new Binding(Command != null ? BindingKind.Command : BindingKind.Edit, "", sb);
        }
        km.Bind(seq, binding);
    }
}

/// <summary>bleopt: show or set options. Usage: bleopt, bleopt name, bleopt name=value, bleopt name value.</summary>
[Cmdlet(VerbsLifecycle.Invoke, "BleOpt")]
[Alias("bleopt")]
public sealed class InvokeBleOptCommand : PSCmdlet
{
    [Parameter(Position = 0, ValueFromRemainingArguments = true)]
    public object[]? Arguments { get; set; }

    protected override void ProcessRecord()
    {
        var args = CmdletArgs.Rejoin((Arguments ?? []).Select(CmdletArgs.Unsplit), n => Options.Exists(n)).ToArray();
        if (args.Length == 0)
        {
            foreach (var n in Options.Names) WriteObject($"bleopt {n}={Options.Quote(Options.GetRaw(n))}");
            return;
        }
        // bleopt name { scriptblock } -- strings use name=value, so `bleopt a b` shows both options
        if (args.Length == 2 && args[0] is string n0 && !n0.Contains('=') && Options.Exists(n0) && args[1] is ScriptBlock)
        {
            Set(n0, args[1]);
            return;
        }
        foreach (var a in args)
        {
            var s = a?.ToString() ?? "";
            if (s.IndexOf('=') > 0)
            {
                // ble.sh: NAME=VALUE, NAME:=VALUE (define), NAME+=VALUE / NAME-=VALUE (colon-separated lists)
                try
                {
                    if (!Options.Apply(s)) WriteError(new ErrorRecord(new ArgumentException($"bleopt: unrecognized argument '{s}'"), "BadOption", ErrorCategory.InvalidArgument, s));
                }
                catch (ArgumentException ex) { WriteError(new ErrorRecord(ex, "BadOption", ErrorCategory.InvalidArgument, s)); }
            }
            else
            {
                try
                {
                    foreach (var n in Options.Match(s)) WriteObject($"bleopt {n}={Options.Quote(Options.GetRaw(n))}");
                }
                catch (ArgumentException ex) { WriteError(new ErrorRecord(ex, "UnknownOption", ErrorCategory.InvalidArgument, s)); }
            }
        }
    }

    void Set(string name, object? value)
    {
        try { Options.Set(name, value); }
        catch (ArgumentException ex) { WriteError(new ErrorRecord(ex, "BadOption", ErrorCategory.InvalidArgument, name)); }
    }
}

[Cmdlet(VerbsCommon.Set, "BleOption")]
public sealed class SetBleOptionCommand : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0)] public string Name { get; set; } = "";
    [Parameter(Mandatory = true, Position = 1)][AllowEmptyString] public object Value { get; set; } = "";

    protected override void ProcessRecord()
    {
        try { Options.Set(Name, Value is PSObject p ? p.BaseObject : Value); }
        catch (ArgumentException ex) { ThrowTerminatingError(new ErrorRecord(ex, "BadOption", ErrorCategory.InvalidArgument, Name)); }
    }
}

[Cmdlet(VerbsCommon.Get, "BleOption")]
public sealed class GetBleOptionCommand : PSCmdlet
{
    [Parameter(Position = 0)] public string Name { get; set; } = "*";

    protected override void ProcessRecord()
    {
        var pat = new WildcardPattern(Name, WildcardOptions.IgnoreCase);
        foreach (var n in Options.Names)
            if (pat.IsMatch(n)) WriteObject(new PSObject(new { Name = n, Value = Options.GetRaw(n) }));
    }
}

/// <summary>ble-face: show or set faces (ble.sh syntax).
/// Usage: ble-face; ble-face PATTERN; ble-face NAME=SPEC; ble-face NAME:=SPEC (define);
/// ble-face -s NAME SPEC; ble-face -d NAME SPEC; ble-face -r PATTERN (reset to the default).</summary>
[Cmdlet(VerbsLifecycle.Invoke, "BleFace")]
[Alias("ble-face")]
public sealed class InvokeBleFaceCommand : PSCmdlet
{
    [Parameter(Position = 0, ValueFromRemainingArguments = true)]
    public object[]? Arguments { get; set; }
    /// <summary>ble-face -s NAME SPEC</summary>
    [Parameter][Alias("s")] public string? SetFace { get; set; }
    /// <summary>ble-face -d NAME SPEC (define a new face)</summary>
    [Parameter][Alias("d")] public string? DefineFace { get; set; }
    /// <summary>ble-face -r PATTERN... (restore defaults)</summary>
    [Parameter][Alias("r")] public SwitchParameter Reset { get; set; }

    protected override void ProcessRecord()
    {
        var args = CmdletArgs.Rejoin((Arguments ?? []).Select(CmdletArgs.Unsplit), n => Faces.GetSpec(n) != null || n.IndexOfAny(['*', '?', '@']) >= 0).Select(a => a?.ToString() ?? "").ToList();
        if (SetFace != null || DefineFace != null)
        {
            if (args.Count == 0) { WriteError(new ErrorRecord(new ArgumentException("ble-face: missing SPEC for -s/-d"), "BadFace", ErrorCategory.InvalidArgument, null)); return; }
            var name = SetFace ?? DefineFace!;
            Run(name + (DefineFace != null ? ":=" : "=") + args[0]);
            args.RemoveAt(0);
            if (args.Count == 0) return;
        }
        if (args.Count == 0)
        {
            if (!Reset) foreach (var n in Faces.Names) WriteObject($"ble-face {n}='{Faces.GetSpec(n)}'");
            else foreach (var n in Faces.Names.ToList()) Faces.ResetFace(n);
            return;
        }
        foreach (var a in args)
        {
            if (a.IndexOf('=') > 0) { Run(a); continue; }
            try
            {
                foreach (var n in Faces.Match(a))
                {
                    if (Reset) Faces.ResetFace(n);
                    else WriteObject($"ble-face {n}='{Faces.GetSpec(n)}'");
                }
            }
            catch (ArgumentException ex) { WriteError(new ErrorRecord(ex, "BadFace", ErrorCategory.InvalidArgument, a)); }
        }
    }

    void Run(string arg)
    {
        try
        {
            if (!Faces.Apply(arg)) WriteError(new ErrorRecord(new ArgumentException($"ble-face: unrecognized setting '{arg}'"), "BadFace", ErrorCategory.InvalidArgument, arg));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException) { WriteError(new ErrorRecord(ex, "BadFace", ErrorCategory.InvalidArgument, arg)); }
    }
}

/// <summary>ble-sabbrev: static abbreviations expanded when SPACE or ENTER follows the word.</summary>
/// <example>ble-sabbrev L='| less' gst='git status'</example>
/// <example>ble-sabbrev today { Get-Date -Format yyyy-MM-dd }</example>
[Cmdlet(VerbsLifecycle.Invoke, "BleSabbrev")]
[Alias("ble-sabbrev")]
public sealed class InvokeBleSabbrevCommand : PSCmdlet
{
    [Parameter(Position = 0, ValueFromRemainingArguments = true)]
    public object[]? Arguments { get; set; }
    [Parameter][Alias("r")] public string[]? Remove { get; set; }

    protected override void ProcessRecord()
    {
        if (Remove != null) foreach (var r in Remove) Editor.Abbrevs.Remove(r);
        var args = (Arguments ?? []).Select(a => a is PSObject p ? p.BaseObject : a).ToArray();
        if (args.Length == 0)
        {
            if (Remove == null)
                foreach (var kv in Editor.Abbrevs.OrderBy(k => k.Key, StringComparer.Ordinal))
                    WriteObject($"ble-sabbrev {kv.Key}={Options.Quote(kv.Value)}");
            return;
        }
        if (args.Length == 2 && args[0] is string key && !key.Contains('=') && args[1] is ScriptBlock or string)
        {
            Editor.Abbrevs[key] = args[1]!;
            return;
        }
        foreach (var a in args)
        {
            var s = a?.ToString() ?? "";
            int eq = s.IndexOf('=');
            if (eq <= 0)
            {
                if (Editor.Abbrevs.TryGetValue(s, out var v)) WriteObject($"ble-sabbrev {s}={Options.Quote(v)}");
                else WriteError(new ErrorRecord(new ArgumentException($"ble-sabbrev: '{s}' is not defined"), "NoAbbrev", ErrorCategory.ObjectNotFound, s));
                continue;
            }
            Editor.Abbrevs[s.Substring(0, eq)] = s.Substring(eq + 1);
        }
    }
}

/// <summary>Defines a widget implemented in PowerShell. The script sees $READLINE_LINE, $READLINE_POINT and
/// $READLINE_MARK and may change them; it can call built-in widgets with Invoke-BleWidget.
/// A function named ble/widget/NAME is picked up as widget NAME as well (ble.sh style).</summary>
/// <example>Register-BleWidget upcase-line { $global:READLINE_LINE = $READLINE_LINE.ToUpper() }</example>
[Cmdlet(VerbsLifecycle.Register, "BleWidget")]
public sealed class RegisterBleWidgetCommand : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0)] public string Name { get; set; } = "";
    [Parameter(Mandatory = true, Position = 1)] public ScriptBlock ScriptBlock { get; set; } = null!;

    protected override void ProcessRecord()
    {
        if (Editor.Widgets.ContainsKey(Name))
        {
            ThrowTerminatingError(new ErrorRecord(new ArgumentException($"'{Name}' is a built-in widget"), "BuiltinWidget", ErrorCategory.InvalidArgument, Name));
            return;
        }
        Editor.UserWidgets[Name] = ScriptBlock;
    }
}

/// <summary>Calls a built-in widget on the current line from inside a ble-bind -x command or user widget.</summary>
/// <example>Register-BleWidget kill-and-upcase { Invoke-BleWidget kill-forward-line; $global:READLINE_LINE = $READLINE_LINE.ToUpper() }</example>
[Cmdlet(VerbsLifecycle.Invoke, "BleWidget")]
public sealed class InvokeBleWidgetCommand : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0)] public string Name { get; set; } = "";

    protected override void ProcessRecord()
    {
        var e = Session.Editor;
        if (e == null || !e.InEditCommand)
        {
            ThrowTerminatingError(new ErrorRecord(new InvalidOperationException("Invoke-BleWidget can only be used inside a ble-bind -x command or a user widget"), "NotInWidget", ErrorCategory.InvalidOperation, Name));
            return;
        }
        var v = SessionState.PSVariable;
        string line = v.GetValue("global:READLINE_LINE")?.ToString() ?? e.Text;
        int point = ToInt(v.GetValue("global:READLINE_POINT"), e.Point);
        int mark = ToInt(v.GetValue("global:READLINE_MARK"), -1);
        try
        {
            e.InvokeWidgetFromScript(Name, line, point, mark, out var nl, out var np, out var nm);
            v.Set("global:READLINE_LINE", nl);
            v.Set("global:READLINE_POINT", np);
            v.Set("global:READLINE_MARK", nm);
        }
        catch (ArgumentException ex) { ThrowTerminatingError(new ErrorRecord(ex, "UnknownWidget", ErrorCategory.InvalidArgument, Name)); }
    }

    static int ToInt(object? o, int def)
    {
        if (o is PSObject p) o = p.BaseObject;
        return o switch { int i => i, long l => (int)l, string s when int.TryParse(s, out var n) => n, _ => def };
    }
}

/// <summary>ble-import: loads ble.ps1 modules or user scripts.
/// Built-in: keymap.vi, keymap.emacs, integration/fzf-key-bindings, integration/fzf-completion
/// (the fzf modules need fzf on PATH, or $env:BLE_FZF). PowerShell scripts (.ps1) are dot-sourced;
/// other files are read as ble.sh configuration (see Import-BleRc).</summary>
/// <example>ble-import integration/fzf-key-bindings</example>
/// <example>ble-import ~/.blerc</example>
[Cmdlet(VerbsData.Import, "BleModule")]
[Alias("ble-import")]
public sealed class ImportBleModuleCommand : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0, ValueFromRemainingArguments = true)] public string[] Name { get; set; } = [];
    /// <summary>ble.sh compatibility: delayed import. Imports happen immediately here.</summary>
    [Parameter][Alias("d")] public SwitchParameter Delay { get; set; }

    protected override void ProcessRecord()
    {
        foreach (var raw in Name)
        {
            var n = raw.Trim();
            switch (BleModules.TryImport(n))
            {
                case BleModules.Result.Imported: continue;
                case BleModules.Result.ImportedNeedsFzf: WarnIfNoFzf(); continue;
            }
            var path = SessionState.Path.GetUnresolvedProviderPathFromPSPath(n);
            if (!File.Exists(path) && File.Exists(path + ".ps1")) path += ".ps1";
            if (!File.Exists(path))
            {
                WriteError(new ErrorRecord(new FileNotFoundException($"ble-import: '{n}' is neither a built-in module nor a file"), "NotFound", ErrorCategory.ObjectNotFound, n));
                continue;
            }
            if (!path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".psm1", StringComparison.OrdinalIgnoreCase))
            {
                ImportBleRcCommand.Load(this, path, quiet: false);
                continue;
            }
            // dot-source into the caller's scope, so functions like ble/widget/NAME stay defined
            SessionState.InvokeCommand.InvokeScript(false, ScriptBlock.Create(". '" + path.Replace("'", "''") + "'"), null);
        }
    }

    void WarnIfNoFzf()
    {
        var exe = Environment.GetEnvironmentVariable("BLE_FZF");
        if (!string.IsNullOrEmpty(exe) && File.Exists(exe)) return;
        if (SessionState.InvokeCommand.GetCommand(string.IsNullOrEmpty(exe) ? "fzf" : exe, CommandTypes.Application) == null)
            WriteWarning("ble-import: fzf was not found on PATH; the fzf keys will show a message until it is installed (e.g. winget install junegunn.fzf)");
    }
}

/// <summary>ble-history: shows or edits the command history, like bash's history builtin (which ble.sh wraps).
/// The history file is shared with PSReadLine.</summary>
/// <example>ble-history 20          # the last 20 entries</example>
/// <example>ble-history -d 5-7      # delete entries 5 to 7 (negative numbers count from the end)</example>
/// <example>ble-history -c -w       # clear the history and write the (empty) history file</example>
[Cmdlet(VerbsLifecycle.Invoke, "BleHistory", SupportsShouldProcess = true)]
[Alias("ble-history")]
public sealed class InvokeBleHistoryCommand : PSCmdlet
{
    [Parameter(Position = 0)] public int? Count { get; set; }
    /// <summary>history -c: clear the history in memory.</summary>
    [Parameter][Alias("c")] public SwitchParameter Clear { get; set; }
    /// <summary>history -d OFFSET or START-END: delete entries (1 = oldest, negative = from the end).</summary>
    [Parameter][Alias("d")] public string? Delete { get; set; }
    /// <summary>history -w: replace the history file with the history in memory.</summary>
    [Parameter][Alias("w")] public SwitchParameter Write { get; set; }

    protected override void ProcessRecord()
    {
        Session.EnsureHistory();
        var h = Session.History;
        if (Clear && ShouldProcess("history in memory", "Clear")) h.Clear();
        if (Delete != null)
        {
            if (!TryParseRange(Delete, h.Count, out int a, out int b))
            {
                WriteError(new ErrorRecord(new ArgumentException($"ble-history: {Delete}: history position out of range"), "BadRange", ErrorCategory.InvalidArgument, Delete));
                return;
            }
            if (ShouldProcess($"history entries {a + 1}-{b + 1}", "Delete")) h.RemoveRange(a, b - a + 1);
        }
        if (Write && ShouldProcess(h.FilePath ?? "(no history file)", "Rewrite history file")) h.Rewrite();
        if (Clear || Delete != null || Write) return;
        int start = Count is int n && n >= 0 ? Math.Max(0, h.Count - n) : 0;
        for (int i = start; i < h.Count; i++) WriteObject($"{i + 1,5}  {h[i]}");
    }

    static bool TryParseRange(string s, int count, out int a, out int b)
    {
        a = b = -1;
        int Pos(int v) => v < 0 ? count + v : v - 1;
        // "-3" is an offset from the end; "2-5" and "-5--2" are ranges
        var m = System.Text.RegularExpressions.Regex.Match(s.Trim(), @"^(-?\d+)(?:-(-?\d+))?$");
        if (!m.Success) return false;
        a = Pos(int.Parse(m.Groups[1].Value));
        b = m.Groups[2].Success ? Pos(int.Parse(m.Groups[2].Value)) : a;
        return a >= 0 && b >= a && b < count;
    }
}

/// <summary>Clears the command history: in memory and in the history file (shared with PSReadLine).
/// PowerShell's own session history (Get-History) is separate; use Clear-History for it.</summary>
/// <example>Clear-BleHistory</example>
/// <example>Clear-BleHistory -InMemoryOnly</example>
[Cmdlet(VerbsCommon.Clear, "BleHistory", SupportsShouldProcess = true, ConfirmImpact = ConfirmImpact.Medium)]
public sealed class ClearBleHistoryCommand : PSCmdlet
{
    [Parameter] public SwitchParameter InMemoryOnly { get; set; }

    protected override void ProcessRecord()
    {
        Session.EnsureHistory();
        var h = Session.History;
        if (InMemoryOnly)
        {
            if (ShouldProcess("history in memory", "Clear")) h.Clear();
            return;
        }
        if (!ShouldProcess(h.FilePath ?? "history in memory", "Clear history (memory and file)")) return;
        h.Clear();
        h.Rewrite();
    }
}

/// <summary>Applies a ble.sh configuration file (~/.blerc): bleopt, ble-face, ble-bind -f/-s/--cursor,
/// ble-sabbrev, ble-import and the blehook/eval-after-load keymap hooks. Bash code that cannot run
/// here is skipped with a warning. Settings made afterwards in PowerShell (bleopt, ble-bind ...) win.
/// Without -Path, ~/.blerc and then ~/.config/blesh/init.sh are tried (as ble.sh does).</summary>
/// <example>Import-BleRc</example>
/// <example>Import-BleRc ~/dotfiles/blerc -Quiet</example>
[Cmdlet(VerbsData.Import, "BleRc")]
[Alias("ble-rc")]
public sealed class ImportBleRcCommand : PSCmdlet
{
    [Parameter(Position = 0)] public string? Path { get; set; }
    /// <summary>Don't print warnings about skipped bash code.</summary>
    [Parameter] public SwitchParameter Quiet { get; set; }

    protected override void ProcessRecord()
    {
        string? path = Path != null ? SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path) : BleRc.FindDefault();
        if (path == null || !File.Exists(path))
        {
            var what = Path ?? "~/.blerc or ~/.config/blesh/init.sh";
            WriteError(new ErrorRecord(new FileNotFoundException($"Import-BleRc: '{what}' not found"), "NotFound", ErrorCategory.ObjectNotFound, what));
            return;
        }
        Load(this, path, Quiet);
    }

    internal static void Load(PSCmdlet cmdlet, string path, bool quiet)
    {
        var rc = new BleRc();
        rc.LoadFile(path);
        if (!quiet) foreach (var w in rc.Warnings) cmdlet.WriteWarning(w);
        cmdlet.WriteVerbose($"{path}: {rc.Applied} settings applied, {rc.Warnings.Count} skipped");
    }
}

/// <summary>New-BleRc: writes the default .blerc that ships with the module (default.blerc next to
/// Ble.dll), to start a configuration from. Without -Path it writes ~/.blerc, which Import-BleRc
/// reads by default; a directory gets a .blerc inside it. An existing file is only replaced with -Force.</summary>
/// <example>New-BleRc</example>
/// <example>New-BleRc $ps_config; Import-BleRc $ps_config/.blerc</example>
[Cmdlet(VerbsCommon.New, "BleRc", SupportsShouldProcess = true)]
[OutputType(typeof(FileInfo))]
public sealed class NewBleRcCommand : PSCmdlet
{
    [Parameter(Position = 0)] public string? Path { get; set; }
    /// <summary>Replace an existing file.</summary>
    [Parameter] public SwitchParameter Force { get; set; }

    internal static string DefaultRcFile =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(NewBleRcCommand).Assembly.Location) ?? "", "default.blerc");

    protected override void ProcessRecord()
    {
        string path = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path ?? "~/.blerc");
        if (Directory.Exists(path)) path = System.IO.Path.Combine(path, ".blerc");
        if (!File.Exists(DefaultRcFile))
        {
            WriteError(new ErrorRecord(new FileNotFoundException($"New-BleRc: '{DefaultRcFile}' is missing from the module"), "NoDefault", ErrorCategory.ObjectNotFound, DefaultRcFile));
            return;
        }
        if (File.Exists(path) && !Force)
        {
            WriteError(new ErrorRecord(new IOException($"New-BleRc: '{path}' already exists (use -Force to replace it)"), "Exists", ErrorCategory.ResourceExists, path));
            return;
        }
        if (!ShouldProcess(path, "Write the default .blerc")) return;
        var dir = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.Copy(DefaultRcFile, path, overwrite: true);
        WriteObject(new FileInfo(path));
    }
}

static class CmdletArgs
{
    /// <summary>
    /// PowerShell splits an unquoted a=fg=red,bold at the comma into an array; ble.sh specs use commas,
    /// so arrays are joined back ("fg=red,bold"). Wrapped values are unwrapped.
    /// </summary>
    public static object? Unsplit(object? a)
    {
        if (a is PSObject p) a = p.BaseObject;
        if (a is object[] arr) return string.Join(',', arr.Select(x => (x is PSObject q ? q.BaseObject : x)?.ToString()));
        return a;
    }

    /// <summary>
    /// A single comma list (ble-face region=fg=33,bold) reaches the cmdlet already flattened into separate
    /// arguments. Pieces after an assignment that cannot be names on their own are joined back to it.
    /// </summary>
    public static List<object?> Rejoin(IEnumerable<object?> args, Func<string, bool> isName)
    {
        var list = new List<object?>();
        foreach (var a in args)
        {
            // pieces may arrive as numbers: bleopt x=1,2 -> "x=1", 2
            var s = a is string or int or long or double ? a.ToString()! : null;
            if (s != null && list.Count > 0 && list[^1] is string prev && prev.IndexOf('=') > 0 && !IsAssignment(s, isName) && !isName(s))
            {
                list[^1] = prev + "," + s;
                continue;
            }
            list.Add(a);
        }
        return list;
    }

    // "fg=33" is a spec part, not an assignment of a face named fg
    static bool IsAssignment(string s, Func<string, bool> isName)
    {
        int eq = s.IndexOf('=');
        if (eq <= 0) return false;
        var name = s.Substring(0, eq).TrimEnd(':', '+', '-');
        return name is not ("fg" or "bg") && (isName(name) || name.IndexOfAny(['*', '?', '@']) >= 0 || s[eq - 1] == ':');
    }
}
