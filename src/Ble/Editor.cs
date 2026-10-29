using System.Management.Automation;

namespace Ble;

public enum MarkKind { None, Emacs, Shift, ViChar, ViLine, ViBlock }

/// <summary>The line editor. One instance lives for the whole session; per-line state is reset in StartLine.</summary>
public sealed partial class Editor
{
    public ITerminal Term { get; set; }
    public IShell Shell { get; set; }
    public History History { get; }
    public Highlighter Highlighter { get; private set; }
    public Renderer Renderer { get; }

    // ---- line state -------------------------------------------------------
    public string Text = "";
    public int Point;
    public int Mark = -1;
    public bool MarkActive;
    public MarkKind MarkKind;
    public string Keymap = "emacs";
    public string Arg = "";
    public string LastWidget = "";
    public string ThisWidget = "";
    public bool Done;
    public string? Result;
    public string? Info;
    public string InfoFace = "status_info";
    public bool Overwrite;
    /// <summary>A modal handler (isearch, menu, quoted-insert...). Returns true if it consumed the key.</summary>
    public Func<KeyInput, bool>? Modal;
    public (int Start, int End)? InsertRegion;
    public (int Start, int End)? MatchRegion;
    public string? FinalSuffix;

    readonly Stack<(string Text, int Point)> _undo = new();
    readonly Stack<(string Text, int Point)> _redo = new();
    readonly List<string> _pending = new();
    readonly Queue<KeyInput> _keyQueue = new();
    int _goalCol = -1;
    bool _killFlag, _lastWasKill, _yankFlag, _lastWasYank;
    (int Start, int End) _yankRange;
    int _yankIndex;
    long _lastKeyTime;
    int _renderFailures;
    string? _rps1;

    public static readonly List<string> KillRing = new();

    public Editor(ITerminal term, IShell shell, History history)
    {
        Term = term;
        Shell = shell;
        History = history;
        Highlighter = new Highlighter(shell);
        Renderer = new Renderer(term);
    }

    public void SetShell(IShell shell)
    {
        if (ReferenceEquals(shell, Shell)) return;
        Shell = shell;
        Highlighter = new Highlighter(shell);
    }

    public int UndoCount => _undo.Count;

    // ---- main loop --------------------------------------------------------

    public void StartLine(string initial = "")
    {
        Text = initial;
        Point = initial.Length;
        Mark = -1; MarkActive = false; MarkKind = MarkKind.None;
        Keymap = Options.Get("default_keymap") == "vi" ? "vi_imap" : "emacs";
        Arg = ""; LastWidget = ""; ThisWidget = "";
        Done = false; Result = null; Info = null; InfoFace = "status_info";
        Overwrite = false; Modal = null; InsertRegion = null; MatchRegion = null; FinalSuffix = null;
        _undo.Clear(); _redo.Clear(); _pending.Clear(); _keyQueue.Clear();
        _goalCol = -1; _lastWasKill = false; _lastWasYank = false;
        Menu = null;
        ClearSuggestion(); _suggestFor = null; _autoMenuPending = false;
        ResetListView();
        _completeCache.Clear();
        ResetViLineState();
        History.MaxSize = Math.Max(1, Options.GetInt("history_size", 10000));
        if (Options.GetBool("history_share")) History.Sync();
        HistIndex = History.Count;
        _histEdits.Clear();
        Highlighter.ResetCaches();
        _viMarks.Clear(); _viLastJump = -1; _viBlockInsert = null; _viBlockEol = false;
        _rps1 = ExpandPrompt(Options.GetRaw("prompt_rps1"));
        if (_rps1.Length == 0) _rps1 = null;
        var title = ExpandPrompt(Options.GetRaw("prompt_xterm_title"));
        if (title.Length > 0) Term.Write("\x1b]0;" + TextWidth.StripAnsi(title).Replace("\a", "") + "\a");
        _lastKeyTime = Term.Now;
    }

    /// <summary>Reads one command line. The prompt has already been written.</summary>
    public string ReadLine()
    {
        StartLine(TakeNextLineInitial() ?? "");
        bool prevCtrlC = Term.TreatControlCAsInput;
        Term.TreatControlCAsInput = true;
        try
        {
            Renderer.Begin(Term.CursorLeft);
            ApplyCursorShape();
            SafeRender();
            while (!Done)
            {
                var k = NextKey();
                if (TakeViPaste(k) is { } pasted) ViPaste(pasted);
                else ProcessKey(k);
                if (!Done && !InputPending())
                {
                    UpdateSuggestion();
                    SafeRender();
                }
            }
            FinishLine();
        }
        finally
        {
            Term.TreatControlCAsInput = prevCtrlC;
        }
        return Result ?? "";
    }

    public bool InputPending() => _keyQueue.Count > 0 || Term.KeyAvailable;

    /// <summary>Pushes a key to be processed before reading from the terminal.</summary>
    public void PushKey(KeyInput k) => _keyQueue.Enqueue(k);

    bool _keyAfterIdle;   // the last key from NextKey came while the editor was waiting for input

    KeyInput NextKey()
    {
        _keyAfterIdle = false;
        if (_keyQueue.Count > 0) return _keyQueue.Dequeue();
        while (true)
        {
            if (Term.KeyAvailable)
            {
                var k = KeyNames.FromConsoleKey(Term.ReadKey());
                if (k != null) return k.Value;
                continue;
            }
            _keyAfterIdle = true;
            int w = Term.Width;
            if (w != Renderer.Width)
            {
                Renderer.AdjustForResize(w);
                SafeRender();
            }
            RunIdle();
            Term.Wait(10);
        }
    }

    public void ProcessKey(KeyInput k)
    {
        _lastKeyTime = Term.Now;
        if (Modal == null) { Info = null; InfoFace = "status_info"; }
        var before = Text;
        RecordMacroKey(k);
        Dispatch(k);
        // modal handlers (menu, isearch) may change the text outside of widgets
        if (Text != before && MarkActive && MarkKind is MarkKind.Emacs or MarkKind.Shift) MarkActive = false;
        Mark = Math.Clamp(Mark, -1, Text.Length);
        Point = Math.Clamp(Point, 0, Text.Length);
    }

    void FinishLine()
    {
        Menu = null; InsertRegion = null; MatchRegion = null; Modal = null; MarkActive = false;
        _suggest = null;
        Info = null;
        if (Result != null && Result.Length > 0 && FinalSuffix == null) TryTransientPrompt();
        try
        {
            var m = BuildModel();
            m.Final = true;
            m.FinalSuffix = FinalSuffix;
            Renderer.Render(m);
        }
        catch
        {
            Term.Write("\r\n");
        }
        if (Options.Get("default_keymap") == "vi" || Keymap.StartsWith("vi", StringComparison.Ordinal))
        {
            if (Options.Get("keymap_vi_imap_cursor").Length > 0 || Options.Get("keymap_vi_nmap_cursor").Length > 0)
                Term.Write("\x1b[0 q");
        }
        if (!string.IsNullOrWhiteSpace(Result) && FinalSuffix == null)
        {
            History.Add(Result!, Shell.CurrentDirectory);
            Session.MarkExecStart(Result!, this);
        }
    }

    void TryTransientPrompt()
    {
        var mode = Options.Get("prompt_ps1_transient");
        if (mode.Length == 0) return;
        if (mode == "same-dir" && Session.LastAcceptDirectory != Shell.CurrentDirectory) return;
        var prompt = Session.PromptText;
        if (prompt == null) return;
        int w = Renderer.Width;
        var (rows, col) = TextWidth.Measure(prompt, w);
        if (col != Renderer.StartCol) return;   // prompt was not what we think it was
        string final;
        if (mode == "trim")
        {
            int nl = prompt.LastIndexOf('\n');
            if (nl < 0) return;
            final = prompt.Substring(nl + 1);
        }
        else
        {
            final = ExpandPrompt(Options.GetRaw("prompt_ps1_final"));
        }
        Renderer.EraseFromPromptRow(rows);
        Term.Write(final + "\x1b[0m");
        var (frows, fcol) = TextWidth.Measure(final, w);
        Renderer.StartCol = fcol;
        Renderer.CurRow = 0;
        Renderer.Active = true;
        _ = frows;
    }

    // ---- rendering --------------------------------------------------------

    public RenderModel BuildModel()
    {
        var m = new RenderModel { Text = Text, Point = Point, Ps2 = ExpandPrompt(Options.GetRaw("prompt_ps2")), Rps1 = _rps1 };
        var faces = (string?[])Highlighter.Highlight(Text).Clone();
        if (faces.Length != Text.Length) Array.Resize(ref faces, Text.Length);
        if (MarkActive && Mark >= 0 && MarkKind == MarkKind.ViBlock)
        {
            foreach (var (a, b) in BlockRanges())
                for (int i = Math.Max(0, a); i < Math.Min(b, faces.Length); i++) faces[i] = "region";
        }
        else if (MarkActive && Mark >= 0)
        {
            var (a, b) = RegionBounds();
            for (int i = Math.Max(0, a); i < Math.Min(b, faces.Length); i++) faces[i] = "region";
        }
        if (InsertRegion is var (ia, ib))
            for (int i = Math.Max(0, ia); i < Math.Min(ib, faces.Length); i++) faces[i] = "region_insert";
        if (MatchRegion is var (ma, mb))
            for (int i = Math.Max(0, ma); i < Math.Min(mb, faces.Length); i++) faces[i] = "region_match";
        if (Overwrite && Point < Text.Length && Text[Point] != '\n') faces[Point] = "overwrite_mode";
        m.Faces = faces;
        if (_suggest != null && Point == Text.Length && Modal == null) m.Suggestion = _suggest;

        if (Info != null) m.Panels.Add(PanelLine.Of(Info, InfoFace));
        if (Menu != null || ListViewActive)
        {
            int w = Renderer.Width > 0 ? Renderer.Width : Term.Width;
            var (rows, _) = TextWidth.Measure(Text + (m.Suggestion ?? ""), w, Renderer.StartCol);
            int avail = Term.Height - (rows + 1) - m.Panels.Count - 2;
            int maxOpt = Options.GetInt("complete_menu_maxlines", 10);
            int max = maxOpt > 0 ? Math.Min(maxOpt, avail) : avail;    // ble.sh: -1 = no limit
            if (Menu != null) m.Panels.AddRange(Menu.Layout(w, Math.Max(1, max)));
            else AddListViewPanels(m, w, Math.Min(avail - 1, ListMax));
        }
        var mode = ModeLine();
        if (mode != null) m.Panels.Add(PanelLine.Of(mode, "status_line"));
        var status = StatusLine(Renderer.Width > 0 ? Renderer.Width : Term.Width);
        if (status != null) m.Panels.Add(status);
        return m;
    }

    public void SafeRender()
    {
        try
        {
            Renderer.Render(BuildModel());
            _renderFailures = 0;
        }
        catch (Exception ex)
        {
            if (++_renderFailures > 3) throw;
            Info = "ble: render error: " + ex.Message;
            InfoFace = "status_error";
        }
    }

    // ---- dispatch ---------------------------------------------------------

    public void Dispatch(KeyInput k)
    {
        if (_viRecording != null && !_viReplaying && Keymap == "vi_imap") _viRecording.Add(k);
        if (ListViewKey(k)) return;
        if (Modal != null)
        {
            bool consumed;
            try { consumed = Modal(k); }
            catch (Exception ex) { Modal = null; ShowError(ex); return; }
            if (consumed) return;
        }
        if (Keymap is "vi_nmap" or "vi_xmap")
        {
            ViKey(k);
            return;
        }
        if (Keymap == "vi_smap")
        {
            SelectKey(k);
            return;
        }
        DispatchKeymap(Keymaps.Get(Keymap), k, defaultInsert: true);
    }

    /// <summary>Looks up the key sequence in a keymap. Returns false if the key was not bound (and not consumed).</summary>
    bool DispatchKeymap(Keymap km, KeyInput k, bool defaultInsert)
    {
        _pending.Add(k.Name);
        var seq = string.Join(' ', _pending);
        if (km.TryGet(seq, out var b))
        {
            _pending.Clear();
            InvokeBinding(b, k);
            return true;
        }
        if (km.IsPrefix(seq))
        {
            return true;
        }
        if (_pending.Count > 1)
        {
            if (_pending.Count == 2 && _pending[0] == "ESC" && km.EscapeIsMeta)
            {
                _pending.Clear();
                var meta = MetaName(k.Name);
                if (km.TryGet(meta, out var mb)) { InvokeBinding(mb, new KeyInput(meta, '\0')); return true; }
            }
            _pending.Clear();
            Bell();
            return true;
        }
        _pending.Clear();
        if (defaultInsert && k.IsPrintable)
        {
            InvokeWidget("self-insert", k);
            return true;
        }
        if (defaultInsert) Bell();
        return false;
    }

    static string MetaName(string name)
    {
        if (name.StartsWith("C-", StringComparison.Ordinal)) return name.StartsWith("C-M-", StringComparison.Ordinal) ? name : "C-M-" + name.Substring(2);
        if (name.StartsWith("M-", StringComparison.Ordinal)) return name;
        return "M-" + name;
    }

    public void InvokeBinding(Binding b, KeyInput k)
    {
        switch (b.Kind)
        {
            case BindingKind.Widget:
                InvokeWidget(b.Widget, k);
                break;
            case BindingKind.Command:
                RunCommandBinding(b.Script!);
                break;
            case BindingKind.Edit:
                RunEditBinding(b.Script!);
                break;
            case BindingKind.Macro:
                RunMacro(b.Widget);
                break;
            case BindingKind.UserWidget:
                RunUserWidget(b.Widget);
                break;
        }
    }

    int _macroDepth;

    /// <summary>ble-bind -s: replays a key sequence as if typed.</summary>
    void RunMacro(string spec)
    {
        if (_macroDepth >= 8) { Bell(); return; }   // a macro that (indirectly) invokes itself
        _macroDepth++;
        try
        {
            foreach (var name in KeyNames.ParseSpec(spec)) Dispatch(KeyInput.FromName(name));
        }
        finally { _macroDepth--; }
    }

    // widgets that manage the undo stack themselves
    static readonly HashSet<string> NoUndoWidgets = new() { "undo", "redo", "revert-line", "vi-normal-mode" };
    static readonly HashSet<string> GoalWidgets = new()
    {
        "backward-line-or-history-prev", "forward-line-or-history-next", "backward-line", "forward-line",
    };
    static readonly HashSet<string> MenuKeepWidgets = new()
    {
        "complete", "menu-complete", "menu-complete-backward", "possible-completions",
    };

    public void InvokeWidget(string name, KeyInput k)
    {
        if (!Widgets.TryGetValue(name, out var fn) && UserWidgets.ContainsKey(name))
        {
            RunUserWidget(name);
            LastWidget = name;
            return;
        }
        if (fn == null)
        {
            Info = $"ble: unknown widget '{name}'";
            InfoFace = "status_error";
            return;
        }
        var bt = Text; var bp = Point;
        ThisWidget = name;
        _killFlag = false; _yankFlag = false;
        try { fn(this, k); }
        catch (Exception ex) { ShowError(ex, name); }
        AfterWidget(name, k, bt, bp);
    }

    void AfterWidget(string name, KeyInput k, string beforeText, int beforePoint)
    {
        Point = Math.Clamp(Point, 0, Text.Length);
        if (Text != beforeText && !NoUndoWidgets.Contains(name) && _viInsertUndo == null)
        {
            bool merge = name is "self-insert" or "append-arg" && LastWidget is "self-insert" or "append-arg" && k.Char != ' ' && _undo.Count > 0;
            if (!merge) PushUndo(beforeText, beforePoint);
            _redo.Clear();
        }
        if (name is not ("digit-argument" or "universal-arg" or "append-arg") || (name == "append-arg" && Text != beforeText)) { Arg = ""; _universal = false; }
        _lastWasKill = _killFlag;
        _lastWasYank = _yankFlag;
        if (MarkActive && MarkKind == MarkKind.Shift && !name.StartsWith("shift-", StringComparison.Ordinal)) MarkActive = false;
        if (MarkActive && MarkKind == MarkKind.Emacs && Text != beforeText) MarkActive = false;
        if (!GoalWidgets.Contains(name)) _goalCol = -1;
        if (Menu != null && !Menu.Selecting && !MenuKeepWidgets.Contains(name))
        {
            if (name is "self-insert" or "delete-backward-char" && Options.GetBool("complete_menu_filter")) RefilterMenu();
            else CloseMenu();
        }
        if (Menu == null) InsertRegion = null;
        LastWidget = name;
    }

    public void PushUndo(string text, int point)
    {
        _undo.Push((text, point));
        _redo.Clear();
    }

    public void ShowError(Exception ex, string? widget = null)
    {
        var e = ex is System.Reflection.TargetInvocationException { InnerException: not null } t ? t.InnerException! : ex;
        Info = widget != null ? $"ble: {widget}: {e.Message}" : $"ble: {e.Message}";
        InfoFace = "status_error";
    }

    public void Bell()
    {
        // ble.sh: a colon-separated list of abell (audible) and vbell (visible); ble.ps1 also takes audible/visible
        var bell = ":" + Options.Get("edit_bell") + ":";
        if (bell.Contains(":abell:") || bell.Contains(":audible:")) Term.Write("\a");
        if ((bell.Contains(":vbell:") || bell.Contains(":visible:")) && Info == null) { Info = "[ble: bell]"; InfoFace = "vbell"; }
    }

    // ---- shell commands bound to keys --------------------------------------

    /// <summary>Leaves the current line on screen, runs an action that may print, and redraws the prompt.</summary>
    public void RunAboveLine(Action action)
    {
        var saveText = Text; var savePoint = Point;
        var m = BuildModel();
        m.Final = true;
        Renderer.Render(m);
        bool prev = Term.TreatControlCAsInput;
        Term.TreatControlCAsInput = false;
        try { action(); }
        catch (Exception ex) { Term.Write($"\x1b[91mble: {ex.Message}\x1b[m\r\n"); }
        finally { Term.TreatControlCAsInput = prev; }
        RedrawPrompt(clear: false);
        Text = saveText; Point = Math.Min(savePoint, Text.Length);
    }

    void RunCommandBinding(ScriptBlock sb) => RunAboveLine(() => Shell.RunCommand(sb));

    /// <summary>User-defined widgets: Register-BleWidget scriptblocks and functions named ble/widget/NAME.</summary>
    public static readonly Dictionary<string, ScriptBlock> UserWidgets = new(StringComparer.Ordinal);

    /// <summary>True while a ble-bind -x command or user widget runs (Invoke-BleWidget is allowed then).</summary>
    public bool InEditCommand { get; private set; }

    void RunUserWidget(string name)
    {
        var sb = UserWidgets.TryGetValue(name, out var s) ? s : ScriptBlock.Create("ble/widget/" + name);
        RunEditBinding(sb);
        LastWidget = name;
    }

    void RunEditBinding(ScriptBlock sb)
    {
        var before = Text; var bp = Point;
        int startCol = Renderer.StartCol;
        bool wasActive = Renderer.Active;
        Renderer.EraseDrawing();
        bool prev = Term.TreatControlCAsInput;
        Term.TreatControlCAsInput = false;
        InEditCommand = true;
        try
        {
            var (line, point, mark) = Shell.RunEditCommand(sb, Text, Point, MarkActive ? Mark : -1);
            Text = line.Replace("\r\n", "\n");
            Point = Math.Clamp(point, 0, Text.Length);
            if (mark >= 0) { Mark = mark; if (!MarkActive) { MarkActive = true; MarkKind = MarkKind.Emacs; } }
            else if (MarkKind is MarkKind.Emacs or MarkKind.Shift) MarkActive = false;
        }
        catch (Exception ex) { ShowError(ex); }
        finally
        {
            InEditCommand = false;
            Term.TreatControlCAsInput = prev;
        }
        if (Text != before) PushUndo(before, bp);
        LastWidget = "edit-command";
        // if the command printed something, start over on a fresh prompt below its output
        int col = Term.CursorLeft;
        if (wasActive && col != startCol)
        {
            if (col != 0) Term.Write("\r\n");
            RedrawPrompt(clear: false);
        }
        else Renderer.Begin(startCol);
    }

    /// <summary>Runs a widget on the live line from a user script (Invoke-BleWidget).</summary>
    public void InvokeWidgetFromScript(string name, string line, int point, int mark, out string newLine, out int newPoint, out int newMark)
    {
        Text = line.Replace("\r\n", "\n");
        Point = Math.Clamp(point, 0, Text.Length);
        if (mark >= 0) { Mark = Math.Min(mark, Text.Length); if (!MarkActive) { MarkActive = true; MarkKind = MarkKind.Emacs; } }
        if (Widgets.TryGetValue(name, out var fn)) fn(this, default);
        else throw new ArgumentException($"unknown widget '{name}'");
        newLine = Text; newPoint = Point; newMark = MarkActive ? Mark : -1;
    }

    /// <summary>Writes a fresh prompt (optionally clearing the screen) and restarts drawing after it.</summary>
    public void RedrawPrompt(bool clear)
    {
        if (clear) Term.Write("\x1b[H\x1b[2J\x1b[3J");
        var p = Shell.InvokePrompt() ?? "PS> ";
        Term.Write(p);
        int col;
        try { col = Term.CursorLeft; }
        catch { col = TextWidth.Measure(p, Term.Width).Col; }
        Renderer.Begin(col);
    }

    // ---- basic text operations -------------------------------------------

    public (int Start, int End) RegionBounds()
    {
        int a = Math.Min(Mark, Point), b = Math.Max(Mark, Point);
        a = Math.Clamp(a, 0, Text.Length); b = Math.Clamp(b, 0, Text.Length);
        if (MarkKind == MarkKind.ViChar) b = Math.Min(Text.Length, b + 1);
        else if (MarkKind == MarkKind.ViLine) { a = LineStart(a); b = LineEnd(b); }
        return (a, b);
    }

    public void SetText(string text, int point)
    {
        Text = text;
        Point = Math.Clamp(point, 0, text.Length);
    }

    public void Insert(string s)
    {
        if (MarkActive && MarkKind == MarkKind.Shift)
        {
            var (a, b) = RegionBounds();
            Text = Text.Remove(a, b - a);
            Point = a;
            MarkActive = false;
        }
        if (Overwrite && s.IndexOf('\n') < 0)
        {
            int le = LineEnd(Point);
            int n = Math.Min(s.Length, le - Point);
            if (n > 0) Text = Text.Remove(Point, n);
        }
        Text = Text.Insert(Point, s);
        Point += s.Length;
    }

    public string Delete(int a, int b)
    {
        if (a > b) (a, b) = (b, a);
        a = Math.Clamp(a, 0, Text.Length); b = Math.Clamp(b, 0, Text.Length);
        var s = Text.Substring(a, b - a);
        Text = Text.Remove(a, b - a);
        if (Point > b) Point -= b - a;
        else if (Point > a) Point = a;
        return s;
    }

    public void Kill(int a, int b, bool prepend = false)
    {
        if (a > b) (a, b) = (b, a);
        if (a == b) { _killFlag = _lastWasKill; return; }
        var s = Delete(a, b);
        Point = a;
        AddKill(s, prepend);
    }

    public void AddKill(string s, bool prepend = false)
    {
        if (_lastWasKill && KillRing.Count > 0)
            KillRing[0] = prepend ? s + KillRing[0] : KillRing[0] + s;
        else
        {
            KillRing.Insert(0, s);
            if (KillRing.Count > 30) KillRing.RemoveAt(KillRing.Count - 1);
        }
        _killFlag = true;
    }

    public int LineStart(int pos)
    {
        pos = Math.Clamp(pos, 0, Text.Length);
        return pos == 0 ? 0 : Text.LastIndexOf('\n', pos - 1) + 1;
    }

    public int LineEnd(int pos)
    {
        pos = Math.Clamp(pos, 0, Text.Length);
        int i = Text.IndexOf('\n', pos);
        return i < 0 ? Text.Length : i;
    }

    public int CountArg(int def = 1)
    {
        if (Arg.Length == 0) return def;
        if (Arg == "-") return -1;
        // counts are capped: a runaway M-3 M-3 M-3 ... must not insert millions of characters
        return int.TryParse(Arg, out var n) ? Math.Clamp(n, -MaxCount, MaxCount) : def;
    }

    public bool HasArg => Arg.Length > 0;

    public const int MaxCount = 9999;

    /// <summary>Display width of a tab in the command line (bleopt tab_width; '' = 8).</summary>
    public static int TabWidth => Options.GetInt("tab_width", 8) is var n && n > 0 ? Math.Min(n, 32) : 8;
}
