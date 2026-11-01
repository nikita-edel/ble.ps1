using System.Text;

namespace Ble;

// vi: block visual mode, q/@ macros, marks and select mode.
public sealed partial class Editor
{
    // ---- line helpers ----

    public int LineNumber(int pos)
    {
        pos = Math.Clamp(pos, 0, Text.Length);
        int n = 0;
        for (int i = 0; i < pos; i++) if (Text[i] == '\n') n++;
        return n;
    }

    public int LineCount => Text.Count(c => c == '\n') + 1;

    public int LineStartOfNumber(int n)
    {
        if (n <= 0) return 0;
        int p = 0;
        for (int k = 0; k < n; k++)
        {
            int nl = Text.IndexOf('\n', p);
            if (nl < 0) return LineStart(Text.Length);
            p = nl + 1;
        }
        return p;
    }

    int Column(int pos) => pos - LineStart(pos);

    // ---- mode name (used by the mode line and \q{keymap:vi/mode-indicator}) ----

    // ble.sh lib/keymap.vi.sh: ble/prompt/backslash:keymap:vi/mode-indicator (names from bleopt keymap_vi_mode_name_*)
    string ModeName()
    {
        static string N(string mode) => Options.Get("keymap_vi_mode_name_" + mode);
        var insert = _viReplace || Overwrite ? N("replace") : N("insert");
        if (_viOneCommand) insert = "(" + insert.ToLowerInvariant() + ")";
        string s;
        if (Keymap is "vi_xmap" or "vi_smap")
        {
            var visual = Keymap == "vi_smap" ? N("select") : N("visual");
            if (MarkKind == MarkKind.ViLine) visual += " " + N("linewise");
            else if (MarkKind == MarkKind.ViBlock) visual += " " + N("blockwise");
            s = "-- " + (_viOneCommand ? insert + " " + visual : visual) + " --";
        }
        else if (Keymap == "vi_imap" || (Keymap == "vi_nmap" && _viOneCommand)) s = "-- " + insert + " --";
        else if (Keymap == "vi_nmap") s = Options.Get("keymap_vi_mode_string_nmap");
        else s = "";
        if (_macroKeys != null && _macroReg != '\x01') s = (s.Length > 0 ? s + " " : "") + "recording @" + _macroReg;
        return s;
    }

    // ble.sh lib/keymap.emacs.sh: ble/prompt/backslash:keymap:emacs/mode-indicator
    string EmacsModeName()
    {
        if (!Text.Contains('\n')) return "";
        var s = Options.Get("keymap_emacs_mode_string_multiline");
        bool recording = _macroKeys != null && _macroReg == '\x01';
        var km = Keymaps.Get("emacs");
        // the key hint is only shown with ble.sh's default RET / C-j bindings and nothing else to show
        if (Arg.Length == 0 && !recording && km.TryGet("RET", out var cm) && cm.Widget == "accept-single-line-or-newline"
            && km.TryGet("C-j", out var cj) && cj.Widget is "accept-line" or "accept-suggestion-and-execute")
            s = (s.Length > 0 ? s + " " : "") + "(\x1b[35mRET\x1b[m or \x1b[35mC-m\x1b[m: insert a newline, \x1b[35mC-j\x1b[m: run)";
        return s;
    }

    // ---- block visual ----

    bool _viBlockEol;

    sealed record BlockInsert(int FirstLine, int LastLine, int Col, bool Pad, bool Eol, string BaseLine);
    BlockInsert? _viBlockInsert;

    /// <summary>Per-line ranges [start, end) of the visual block.</summary>
    public List<(int Start, int End)> BlockRanges()
    {
        var list = new List<(int, int)>();
        int l1 = LineNumber(Math.Min(Mark, Point)), l2 = LineNumber(Math.Max(Mark, Point));
        int c1 = Column(Mark), c2 = Column(Point);
        if (c1 > c2) (c1, c2) = (c2, c1);
        for (int n = l1; n <= l2; n++)
        {
            int ls = LineStartOfNumber(n), le = LineEnd(ls), len = le - ls;
            int s = ls + Math.Min(c1, len);
            int e = _viBlockEol ? le : ls + Math.Min(c2 + 1, len);
            list.Add((s, Math.Max(s, e)));
        }
        return list;
    }

    (int FirstLine, int LastLine, int LeftCol, int RightCol) BlockShape()
    {
        int l1 = LineNumber(Math.Min(Mark, Point)), l2 = LineNumber(Math.Max(Mark, Point));
        int c1 = Column(Mark), c2 = Column(Point);
        if (c1 > c2) (c1, c2) = (c2, c1);
        return (l1, l2, c1, c2);
    }

    void BlockDelete(List<(int Start, int End)> rows)
    {
        for (int r = rows.Count - 1; r >= 0; r--)
        {
            var (s, e) = rows[r];
            if (e > s) Text = Text.Remove(s, e - s);
        }
    }

    static string BlockText(string text, List<(int Start, int End)> rows) =>
        string.Join('\n', rows.Select(r => text.Substring(r.Start, r.End - r.Start)));

    /// <summary>Visual-block commands. Returns null when the key is not block-specific.</summary>
    ViStatus? ViBlockCommand(string c, int count, char reg, List<KeyInput> ks, ref int i, string beforeText, int beforePoint)
    {
        var rows = BlockRanges();
        var (l1, l2, c1, c2) = BlockShape();
        void Exit() { MarkActive = false; MarkKind = MarkKind.None; SetKeymap("vi_nmap"); }
        switch (c)
        {
            case "d": case "x": case "delete": case "D": case "X":
                {
                    if (c is "D" or "X") { _viBlockEol = true; rows = BlockRanges(); }
                    SetRegister(reg, BlockText(Text, rows), false, yank: false, block: true);
                    int top = rows[0].Start;
                    BlockDelete(rows);
                    Exit();
                    Point = Math.Min(top, Text.Length);
                    return ViStatus.Done;
                }
            case "y": case "Y":
                {
                    if (c == "Y") { _viBlockEol = true; rows = BlockRanges(); }
                    SetRegister(reg, BlockText(Text, rows), false, yank: true, block: true);
                    Exit();
                    Point = rows[0].Start;
                    return ViStatus.Done;
                }
            case "c": case "s": case "C": case "S":
                {
                    bool eol = c is "C" or "S" || _viBlockEol;
                    if (eol) { _viBlockEol = true; rows = BlockRanges(); }
                    SetRegister(reg, BlockText(Text, rows), false, yank: false, block: true);
                    BlockDelete(rows);
                    MarkActive = false; MarkKind = MarkKind.None;
                    StartBlockInsert(l1, l2, c1, pad: false, eol: eol, (beforeText, beforePoint));
                    return ViStatus.Done;
                }
            case "I":
                MarkActive = false; MarkKind = MarkKind.None;
                StartBlockInsert(l1, l2, c1, pad: false, eol: false, (beforeText, beforePoint));
                return ViStatus.Done;
            case "A":
                {
                    bool eol = _viBlockEol;
                    MarkActive = false; MarkKind = MarkKind.None;
                    StartBlockInsert(l1, l2, c2 + 1, pad: true, eol: eol, (beforeText, beforePoint));
                    return ViStatus.Done;
                }
            case "r":
                {
                    if (i >= ks.Count) return ViStatus.Incomplete;
                    char ch = ks[i].Char; i++;
                    if (ch == '\0') return ViStatus.Invalid;
                    var sb = new StringBuilder(Text);
                    foreach (var (s, e) in rows) for (int k = s; k < e; k++) sb[k] = ch;
                    Text = sb.ToString();
                    Exit();
                    Point = rows[0].Start;
                    return ViStatus.Done;
                }
            case "~": case "u": case "U":
                {
                    var sb = new StringBuilder(Text);
                    foreach (var (s, e) in rows)
                        for (int k = s; k < e; k++)
                            sb[k] = c == "u" ? char.ToLowerInvariant(sb[k]) : c == "U" ? char.ToUpperInvariant(sb[k])
                                : char.IsUpper(sb[k]) ? char.ToLowerInvariant(sb[k]) : char.ToUpperInvariant(sb[k]);
                    Text = sb.ToString();
                    Exit();
                    Point = rows[0].Start;
                    return ViStatus.Done;
                }
            case ">": case "<":
                {
                    int a = LineStartOfNumber(l1), b = LineEnd(LineStartOfNumber(l2));
                    Exit();
                    return ViOperator(c, a, b, true, reg, 1, beforeText, beforePoint);
                }
            case "O":
                {
                    // move to the other corner on the same line
                    int pl = LineStart(Point), ml = LineStart(Mark);
                    int pc = Column(Point), mc = Column(Mark);
                    Point = Math.Min(pl + mc, LineEnd(pl));
                    Mark = Math.Min(ml + pc, LineEnd(ml));
                    return ViStatus.Done;
                }
            case "p": case "P":
                {
                    var r = GetRegister(reg);
                    if (r == null) return ViStatus.Invalid;
                    BlockDelete(rows);
                    Exit();
                    Point = rows[0].Start;
                    PasteRegister(r, before: true, count: 1);
                    return ViStatus.Done;
                }
        }
        return null;
    }

    void StartBlockInsert(int l1, int l2, int col, bool pad, bool eol, (string Text, int Point) undo)
    {
        int ls = LineStartOfNumber(l1), le = LineEnd(ls);
        int pos;
        if (eol) pos = le;
        else if (le - ls < col)
        {
            if (pad) { Text = Text.Insert(le, new string(' ', col - (le - ls))); pos = ls + col; }
            else pos = le;
        }
        else pos = ls + col;
        var lsNow = LineStartOfNumber(l1);
        var baseLine = Text.Substring(lsNow, LineEnd(lsNow) - lsNow);
        _viBlockInsert = new BlockInsert(l1, l2, eol ? baseLine.Length : pos - lsNow, pad, eol, baseLine);
        _viBlockEol = false;
        ViEnterInsert(pos, 1, undo);
    }

    /// <summary>On leaving insert mode after a block I/A/c: repeat the inserted text on the other lines.</summary>
    void FinishBlockInsert()
    {
        var bi = _viBlockInsert;
        _viBlockInsert = null;
        if (bi == null || bi.LastLine <= bi.FirstLine) return;
        int ls = LineStartOfNumber(bi.FirstLine);
        var line = Text.Substring(ls, LineEnd(ls) - ls);
        int col = Math.Min(bi.Col, bi.BaseLine.Length);
        int grow = line.Length - bi.BaseLine.Length;
        if (grow <= 0 || LineNumber(Point) != bi.FirstLine) return;
        if (!line.StartsWith(bi.BaseLine.Substring(0, col), StringComparison.Ordinal) ||
            !line.EndsWith(bi.BaseLine.Substring(col), StringComparison.Ordinal)) return;
        var ins = line.Substring(col, grow);
        if (ins.Contains('\n')) return;
        for (int n = bi.LastLine; n > bi.FirstLine; n--)
        {
            if (n >= LineCount) continue;
            int s = LineStartOfNumber(n), e = LineEnd(s), len = e - s;
            if (bi.Eol) Text = Text.Insert(e, ins);
            else if (len < bi.Col)
            {
                if (bi.Pad) Text = Text.Insert(e, new string(' ', bi.Col - len) + ins);
            }
            else Text = Text.Insert(s + bi.Col, ins);
        }
    }

    // ---- pasting from the terminal in normal / visual mode ----
    // ble.sh gets pastes as bracketed paste. Windows consoles drop those markers before ReadKey, so a
    // paste is recognized by its keys: they are in the input buffer all at once, while typed keys come
    // one at a time. Without this, "echo hi" pasted in normal mode runs e, c, h, ... as commands.

    const int PasteMinKeys = 3;

    /// <summary>If k starts a paste in a vi command mode, reads the rest and returns the pasted text.</summary>
    string? TakeViPaste(KeyInput k)
    {
        if (!_keyAfterIdle || Keymap is not ("vi_nmap" or "vi_xmap" or "vi_smap") || Modal != null || !Term.KeyAvailable) return null;
        var keys = new List<KeyInput> { k };
        while (true)
        {
            while (Term.KeyAvailable)
                if (KeyNames.FromConsoleKey(Term.ReadKey()) is { } next) keys.Add(next);
            // a long paste can arrive in pieces
            Term.Wait(5);
            if (!Term.KeyAvailable) break;
        }
        var sb = new StringBuilder();
        foreach (var key in keys)
        {
            if (key.Name is "RET" or "C-m" or "C-j") sb.Append('\n');
            else if (key.Name is "TAB" or "C-i") sb.Append('\t');
            else if (key.IsPrintable) sb.Append(key.Name == "SP" ? " " : key.Name);
            else { sb = null; break; }
        }
        if (sb != null && keys.Count >= PasteMinKeys) return sb.ToString();
        // not a paste (or too short to tell): the keys run as usual
        for (int i = 1; i < keys.Count; i++) _keyQueue.Enqueue(keys[i]);
        return null;
    }

    /// <summary>ble.sh vi-command/bracketed-paste: normal mode inserts the text after the cursor (before
    /// it at the start of a line) and goes back to normal mode; visual and select mode replace the
    /// selection; with an operator pending, the operator is cancelled.</summary>
    void ViPaste(string text)
    {
        _lastKeyTime = Term.Now;
        Info = null;
        _pending.Clear();
        bool countOnly = _viKeys.All(x => x.Name.Length == 1 && char.IsDigit(x.Name[0]));
        _viKeys.Clear();   // ble.sh discards the count
        if (!countOnly) { Bell(); ApplyCursorShape(); return; }
        if (Keymap is "vi_xmap" or "vi_smap")
        {
            if (Keymap == "vi_smap") SetKeymap("vi_xmap");
            ViKey(KeyInput.FromName("c"));
            if (Keymap != "vi_imap") return;
        }
        else
        {
            int pos = Point == LineStart(Point) ? Point : Math.Min(Point + 1, LineEnd(Point));
            ViEnterInsert(pos, 1, (Text, Point));
        }
        Insert(text);
        ViLeaveInsert();
        ApplyCursorShape();
    }

    /// <summary>Pastes a register at the cursor (handles charwise, linewise and blockwise registers).</summary>
    void PasteRegister(Register r, bool before, int count)
    {
        int ls = LineStart(Point), le = LineEnd(Point);
        if (r.Blockwise)
        {
            var lines = r.Text.Split('\n');
            int col = Column(Point) + (!before && le > ls ? 1 : 0);
            int first = LineNumber(Point);
            for (int k = 0; k < lines.Length; k++)
            {
                int n = first + k;
                if (n >= LineCount) Text += "\n";
                int s = LineStartOfNumber(n), e = LineEnd(s);
                if (e - s < col) { Text = Text.Insert(e, new string(' ', col - (e - s))); e = s + col; }
                var piece = string.Concat(Enumerable.Repeat(lines[k], count));
                Text = Text.Insert(s + col, piece);
            }
            Point = LineStartOfNumber(first) + col;
            return;
        }
        if (r.Linewise)
        {
            var block = string.Join('\n', Enumerable.Repeat(r.Text, count));
            if (!before)
            {
                Text = Text.Insert(le, "\n" + block);
                Point = TextOps.FirstNonBlank(Text, le + 1);
            }
            else
            {
                Text = Text.Insert(ls, block + "\n");
                Point = TextOps.FirstNonBlank(Text, ls);
            }
            return;
        }
        var rep = string.Concat(Enumerable.Repeat(r.Text, count));
        int at = !before && le > ls ? Math.Min(le, Point + 1) : Point;
        Text = Text.Insert(at, rep);
        Point = Math.Max(at, at + rep.Length - 1);
    }

    // ---- macros (q / @) ----

    public static readonly Dictionary<char, List<KeyInput>> Macros = new();
    char _macroReg;
    List<KeyInput>? _macroKeys;
    char _lastMacro;
    int _macroPlayDepth;
    int _macroBudget;

    void RecordMacroKey(KeyInput k) => _macroKeys?.Add(k);

    ViStatus ViMacroRecord(List<KeyInput> ks, ref int i)
    {
        if (_macroKeys != null)
        {
            // the terminating q was recorded before it was dispatched
            if (_macroKeys.Count > 0) _macroKeys.RemoveAt(_macroKeys.Count - 1);
            char lower = char.ToLowerInvariant(_macroReg);
            if (char.IsUpper(_macroReg) && Macros.TryGetValue(lower, out var old)) Macros[lower] = old.Concat(_macroKeys).ToList();
            else Macros[lower] = _macroKeys;
            _macroKeys = null;
            return ViStatus.Done;
        }
        if (i >= ks.Count) return ViStatus.Incomplete;
        char reg = ks[i].Char; i++;
        if (!(char.IsLetterOrDigit(reg) || reg == '"')) return ViStatus.Invalid;
        _macroReg = reg;
        _macroKeys = new List<KeyInput>();
        return ViStatus.Done;
    }

    ViStatus ViMacroPlay(int count, List<KeyInput> ks, ref int i)
    {
        if (i >= ks.Count) return ViStatus.Incomplete;
        char reg = ks[i].Char; i++;
        if (reg == ':') { if (_viLastEx == null) return ViStatus.Invalid; for (int n = 0; n < count; n++) ExecuteEx(_viLastEx); return ViStatus.Done; }
        if (reg == '@') reg = _lastMacro;
        reg = char.ToLowerInvariant(reg);
        List<KeyInput>? keys = null;
        if (Macros.TryGetValue(reg, out var m)) keys = m;
        else if (Registers.TryGetValue(reg, out var r)) keys = r.Text.Select(ch => ch == '\n' ? new KeyInput("RET", '\0') : KeyInput.FromName(ch == ' ' ? "SP" : ch.ToString())).ToList();
        if (keys == null || _macroPlayDepth >= 10) return ViStatus.Invalid;
        _lastMacro = reg;
        _viKeys.Clear();
        if (_macroPlayDepth == 0) _macroBudget = 100000;   // limit for recursive / huge-count replays
        _macroPlayDepth++;
        try
        {
            for (int n = 0; n < count && !Done && _macroBudget > 0; n++)
                foreach (var k in keys)
                {
                    if (Done || --_macroBudget < 0) break;
                    Dispatch(k);
                }
        }
        finally { _macroPlayDepth--; }
        _viKeys.Add(KeyInput.FromName("@"));
        _viUndoHandled = true;
        return ViStatus.Done;
    }

    // ---- marks (m ' `) ----

    readonly Dictionary<char, int> _viMarks = new();
    int _viLastJump = -1;

    ViStatus ViSetMark(List<KeyInput> ks, ref int i)
    {
        if (i >= ks.Count) return ViStatus.Incomplete;
        char ch = ks[i].Char; i++;
        if (!char.IsLetter(ch) && ch != '\'' && ch != '`') return ViStatus.Invalid;
        if (ch is '\'' or '`') _viLastJump = Point;
        else _viMarks[ch] = Point;
        return ViStatus.Done;
    }

    Motion ViMarkMotion(string m, List<KeyInput> ks, ref int i)
    {
        if (i >= ks.Count) return Motion.Incomplete;
        char ch = ks[i].Char; i++;
        int target;
        if (ch is '\'' or '`') { if (_viLastJump < 0) return Motion.Invalid; target = _viLastJump; }
        else if (!_viMarks.TryGetValue(ch, out target)) return Motion.Invalid;
        target = Math.Clamp(target, 0, Text.Length);
        return m == "'"
            ? new Motion(ViStatus.Done, TextOps.FirstNonBlank(Text, LineStart(target)), MotionType.Linewise)
            : new Motion(ViStatus.Done, target, MotionType.Exclusive);
    }

    // ---- select mode (vi_smap) ----

    void EnterSelect(MarkKind kind)
    {
        if (!MarkActive) Mark = Point;
        MarkActive = true;
        MarkKind = kind == MarkKind.ViLine ? MarkKind.ViLine : MarkKind.ViChar;
        SetKeymap("vi_smap");
    }

    void SelectKey(KeyInput k)
    {
        var km = Keymaps.Get("vi_smap");
        var seq = string.Join(' ', _pending.Append(k.Name));
        if (km.TryGet(seq, out var bnd)) { _pending.Clear(); InvokeBinding(bnd, k); return; }
        if (km.IsPrefix(seq)) { _pending.Add(k.Name); return; }
        _pending.Clear();

        void Exit() { MarkActive = false; MarkKind = MarkKind.None; SetKeymap("vi_nmap"); ViClampNormal(); }
        int max = Math.Max(0, Text.Length - 1);
        switch (k.Name)
        {
            case "ESC": case "C-c": case "C-[":
                Exit(); return;
            case "C-g":
                SetKeymap("vi_xmap"); return;
            case "left": case "S-left": Point = Math.Max(LineStart(Point), Point - 1); return;
            case "right": case "S-right": Point = Math.Min(Math.Max(LineStart(Point), LineEnd(Point) - 1), Point + 1); return;
            case "up": case "S-up": MoveLine(-1); return;
            case "down": case "S-down": MoveLine(1); return;
            case "home": case "S-home": Point = LineStart(Point); return;
            case "end": case "S-end": Point = Math.Max(LineStart(Point), LineEnd(Point) - 1); return;
            case "C-left": case "C-S-left": Point = TextOps.ViPrevWordStart(Text, Point, false); return;
            case "C-right": case "C-S-right": Point = Math.Min(max, TextOps.ViNextWordStart(Text, Point, false)); return;
            case "RET":
                Exit(); AcceptLine(); return;
            case "DEL": case "C-h": case "delete":
                SelectReplace(null); return;
        }
        if (k.Char != '\0') { SelectReplace(k); return; }
        Bell();
    }

    void SelectReplace(KeyInput? k)
    {
        var undo = (Text, Point);
        var (a, b) = RegionBounds();
        Text = Text.Remove(a, b - a);
        MarkActive = false; MarkKind = MarkKind.None;
        ViEnterInsert(a, 1, undo);
        if (k is { } key) Dispatch(key);
    }
}
