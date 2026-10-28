namespace Ble;

public sealed partial class Editor
{
    enum ViStatus { Done, Incomplete, Invalid, NotMotion }
    enum MotionType { Exclusive, Inclusive, Linewise }

    readonly record struct Motion(ViStatus Status, int Pos, MotionType Type)
    {
        public static readonly Motion Incomplete = new(ViStatus.Incomplete, 0, MotionType.Exclusive);
        public static readonly Motion Invalid = new(ViStatus.Invalid, 0, MotionType.Exclusive);
    }

    public sealed record Register(string Text, bool Linewise, bool Blockwise = false);

    public static readonly Dictionary<char, Register> Registers = new();

    readonly List<KeyInput> _viKeys = new();
    (char Cmd, char Ch)? _viLastFind;
    List<KeyInput>? _viDotKeys;
    List<KeyInput>? _viRecording;
    bool _viReplaying;
    (string Text, int Point)? _viInsertUndo;
    int _viInsertCount = 1;
    int _viInsertStart;
    string _viInsertBase = "";
    bool _viReplace;
    string _viReplaceOrig = "";
    int _viReplaceStart;
    bool _viOneCommand;
    string _viSearch = "";
    bool _viSearchBackward = true;

    void ResetViLineState()
    {
        _viKeys.Clear();
        _viRecording = null;
        _viInsertUndo = null;
        _viInsertCount = 1;
        _viReplace = false;
        _viOneCommand = false;
    }

    static void RegisterViWidgets()
    {
        W("vi-normal-mode", (e, k) => e.ViLeaveInsert());
        W("vi-insert-one-command", (e, k) =>
        {
            e._viOneCommand = true;
            e.SetKeymap("vi_nmap");
        });
    }

    string? ModeLine()
    {
        if (Keymap == "emacs")
        {
            // ble.sh: the emacs mode line shows prompt_emacs_mode_indicator, the numeric argument and REC
            var e = ExpandPrompt(Options.GetRaw("prompt_emacs_mode_indicator"));
            if (Arg.Length > 0) e = (e.Length > 0 ? e + " " : "") + "(arg: \x1b[1;34m" + Arg + "\x1b[m)";
            if (_macroKeys != null && _macroReg == '\x01') e = (e.Length > 0 ? e + " " : "") + "\x1b[1;31mREC\x1b[m";
            return e.Length == 0 ? null : e;
        }
        if (!Options.GetBool("keymap_vi_mode_show")) return null;
        if (!Keymap.StartsWith("vi", StringComparison.Ordinal) && _macroKeys == null) return null;
        var s = ExpandPrompt(Options.GetRaw("prompt_vi_mode_indicator"));
        return s.Length == 0 ? null : s;
    }

    void ApplyCursorShape()
    {
        var opt = Keymap switch
        {
            "vi_imap" => "keymap_vi_imap_cursor",
            "vi_nmap" => _viKeys.Count > 0 ? "keymap_vi_omap_cursor" : "keymap_vi_nmap_cursor",
            "vi_xmap" => "keymap_vi_xmap_cursor",
            "vi_smap" => "keymap_vi_smap_cursor",
            _ => null,
        };
        if (opt == null)
        {
            return;
        }
        var v = Options.Get(opt);
        if (v.Length > 0 && int.TryParse(v, out var n)) Term.Write($"\x1b[{n} q");
    }

    // ---- entering / leaving insert mode ----

    void ViEnterInsert(int pos, int count, (string Text, int Point) undoSnapshot)
    {
        Point = Math.Clamp(pos, 0, Text.Length);
        _viInsertUndo = undoSnapshot;
        _viInsertCount = Math.Max(1, count);
        _viInsertStart = Point;
        _viInsertBase = Text;
        SetKeymap("vi_imap");
    }

    void ViLeaveInsert()
    {
        FinishBlockInsert();
        // repeat inserted text for counts like 3ix<ESC>
        if (_viInsertCount > 1 && !_viReplace)
        {
            int grow = Text.Length - _viInsertBase.Length;
            if (grow > 0 && Point == _viInsertStart + grow
                && string.CompareOrdinal(Text, 0, _viInsertBase, 0, _viInsertStart) == 0)
            {
                var ins = Text.Substring(_viInsertStart, grow);
                var rep = string.Concat(Enumerable.Repeat(ins, _viInsertCount - 1));
                Text = Text.Insert(Point, rep);
                Point += rep.Length;
            }
        }
        _viInsertCount = 1;
        if (_viInsertUndo is var (ut, up) && ut != Text && !_viReplaying) PushUndo(ut, up);
        _viInsertUndo = null;
        _viReplace = false;
        Overwrite = false;
        if (_viRecording != null)
        {
            _viDotKeys = _viRecording;
            _viRecording = null;
        }
        int ls = LineStart(Point);
        if (Point > ls) Point--;
        SetKeymap("vi_nmap");
    }

    // ---- normal / visual mode key handling ----

    void ViKey(KeyInput k)
    {
        // user bindings take precedence when no vi command is pending
        if (_viKeys.Count == 0)
        {
            var km = Keymaps.Get(Keymap);
            var seq = string.Join(' ', _pending.Append(k.Name));
            if (km.TryGet(seq, out var b))
            {
                _pending.Clear();
                InvokeBinding(b, k);
                ViClampNormal();
                return;
            }
            if (km.IsPrefix(seq)) { _pending.Add(k.Name); return; }
            if (_pending.Count > 0) { _pending.Clear(); Bell(); return; }
        }

        _viKeys.Add(k);
        var beforeText = Text; var beforePoint = Point;
        var keymapBefore = Keymap;
        ViStatus st;
        try { st = ViParse(beforeText, beforePoint); }
        catch (Exception ex) { st = ViStatus.Invalid; ShowError(ex); }
        if (st == ViStatus.Incomplete) { if (_viKeys.Count == 1) ApplyCursorShape(); return; }
        var keys = _viKeys.ToList();
        _viKeys.Clear();
        if (st == ViStatus.Invalid) { Bell(); ViClampNormal(); ApplyCursorShape(); return; }

        bool enteredInsert = keymapBefore != "vi_imap" && Keymap == "vi_imap";
        bool changed = Text != beforeText;
        bool repeatable = keymapBefore is not ("vi_xmap" or "vi_smap") && !(keys.Count >= 1 && keys[0].Name is "u" or "C-r" or "." or "@" or "q" or ":" or "m");
        if (!_viReplaying && (changed || enteredInsert) && repeatable)
        {
            if (enteredInsert) _viRecording = keys;
            else _viDotKeys = keys;
        }
        if (!enteredInsert)
        {
            if (changed && !_viUndoHandled && !_viReplaying) PushUndo(beforeText, beforePoint);
            ViClampNormal();
            if (_viOneCommand && Keymap == "vi_nmap" && !(keys.Count == 1 && keys[0].Name == "C-o"))
            {
                _viOneCommand = false;
                SetKeymap("vi_imap");
            }
        }
        _viUndoHandled = false;
        Point = Math.Clamp(Point, 0, Text.Length);
        ApplyCursorShape();
    }

    bool _viUndoHandled;

    void ViClampNormal()
    {
        if (Keymap != "vi_nmap" || _viOneCommand) { Point = Math.Clamp(Point, 0, Text.Length); return; }
        Point = Math.Clamp(Point, 0, Text.Length);
        int ls = LineStart(Point), le = LineEnd(Point);
        if (Point >= le && le > ls) Point = le - 1;
    }

    static int ParseCount(List<KeyInput> ks, ref int i)
    {
        int n = 0; bool any = false;
        while (i < ks.Count && ks[i].Name.Length == 1 && char.IsDigit(ks[i].Name[0]) && (any || ks[i].Name != "0"))
        {
            n = n * 10 + (ks[i].Name[0] - '0');
            if (n > MaxCount) n = MaxCount;
            any = true;
            i++;
        }
        return any ? n : 0;
    }

    ViStatus ViParse(string beforeText, int beforePoint)
    {
        var ks = _viKeys;
        int i = 0;
        char reg = '\0';
        if (ks[0].Name == "\"")
        {
            if (ks.Count < 2) return ViStatus.Incomplete;
            reg = ks[1].Char;
            if (reg == '\0') return ViStatus.Invalid;
            i = 2;
        }
        int count1 = ParseCount(ks, ref i);
        if (i >= ks.Count) return ViStatus.Incomplete;
        string c = ks[i].Name; i++;
        if (c == "g" || c == "z" || c == "Z")
        {
            if (i >= ks.Count) return ViStatus.Incomplete;
            c += ks[i].Name; i++;
        }

        if (Keymap == "vi_xmap") return ViVisual(c, count1, reg, ks, ref i);

        if (c is "d" or "c" or "y" or ">" or "<" or "g~" or "gu" or "gU")
        {
            int count2 = ParseCount(ks, ref i);
            if (i >= ks.Count) return ViStatus.Incomplete;
            int count = Math.Max(1, count1) * Math.Max(1, count2);
            bool hasCount = count1 > 0 || count2 > 0;
            string m = ks[i].Name; i++;
            if (m == "g")
            {
                if (i >= ks.Count) return ViStatus.Incomplete;
                m += ks[i].Name; i++;
            }
            int a, b; bool linewise;
            string opLast = c.Length == 1 ? c : c.Substring(1);
            if (m == c || m == opLast || (c.StartsWith('g') && (m == "g" + opLast || m == c)))
            {
                // dd, cc, yy, >>, g~~, guu, gUU
                int ls = LineStart(Point);
                int lastLine = Point;
                for (int n = 1; n < count; n++)
                {
                    int le = LineEnd(lastLine);
                    if (le >= Text.Length) break;
                    lastLine = le + 1;
                }
                a = ls; b = LineEnd(lastLine); linewise = true;
            }
            else if (m is "i" or "a")
            {
                if (i >= ks.Count) return ViStatus.Incomplete;
                var obj = ks[i].Name; i++;
                var r = TextObject(m == "i", obj, count);
                if (r == null) return ViStatus.Invalid;
                (a, b) = r.Value; linewise = false;
            }
            else
            {
                // cw/cW behave like ce/cE on a non-blank
                if (c == "c" && (m == "w" || m == "W") && Point < Text.Length && !char.IsWhiteSpace(Text[Point]))
                {
                    int p = Point;
                    bool big = m == "W";
                    for (int n = 0; n < count; n++)
                    {
                        bool atEnd = p + 1 >= Text.Length || TextOps.ViClass(Text[p + 1], big) != TextOps.ViClass(Text[p], big);
                        if (!(n == 0 && atEnd)) p = TextOps.ViWordEnd(Text, p, big);
                    }
                    a = Point; b = Math.Min(Text.Length, p + 1); linewise = false;
                }
                else
                {
                    var mo = ViMotion(m, count, hasCount, ks, ref i, forOperator: true);
                    if (mo.Status != ViStatus.Done) return mo.Status == ViStatus.Incomplete ? ViStatus.Incomplete : ViStatus.Invalid;
                    if (mo.Type == MotionType.Linewise)
                    {
                        a = LineStart(Math.Min(Point, mo.Pos)); b = LineEnd(Math.Max(Point, mo.Pos)); linewise = true;
                    }
                    else
                    {
                        a = Math.Min(Point, mo.Pos);
                        b = Math.Max(Point, mo.Pos);
                        if (mo.Type == MotionType.Inclusive) b = Math.Min(Text.Length, b + 1);
                        linewise = false;
                        // 'dw' on the last word of a line stops at the end of the line
                        if ((m == "w" || m == "W") && mo.Pos > Point)
                        {
                            int le = LineEnd(Point);
                            if (b > le && le >= a) b = le;
                        }
                    }
                }
            }
            return ViOperator(c, a, b, linewise, reg, count, beforeText, beforePoint);
        }

        int ii = i;
        var mot = ViMotion(c, Math.Max(1, count1), count1 > 0, ks, ref ii, forOperator: false);
        if (mot.Status == ViStatus.Incomplete) return ViStatus.Incomplete;
        if (mot.Status == ViStatus.Done)
        {
            Point = mot.Pos;
            if (c is "G" or "gg" or "%" or "n" or "N" or "'" or "`") _viLastJump = beforePoint;
            return ViStatus.Done;
        }
        if (mot.Status == ViStatus.Invalid) return ViStatus.Invalid;
        return ViSimple(c, count1, reg, ks, ref i, beforeText, beforePoint);
    }

    // ---- motions ----

    Motion ViMotion(string m, int count, bool hasCount, List<KeyInput> ks, ref int i, bool forOperator)
    {
        var t = Text;
        int p = Point;
        int ls = LineStart(p), le = LineEnd(p);
        count = Math.Max(1, count);
        switch (m)
        {
            case "h": case "left": case "DEL": case "C-h":
                if (p == ls) return forOperator ? new Motion(ViStatus.Done, p, MotionType.Exclusive) : Motion.Invalid;
                return new Motion(ViStatus.Done, Math.Max(ls, p - count), MotionType.Exclusive);
            case "l": case "right": case "SP":
                {
                    int lim = forOperator ? le : Math.Max(ls, le - 1);
                    if (p >= lim && !forOperator) return Motion.Invalid;
                    return new Motion(ViStatus.Done, Math.Min(lim, p + count), MotionType.Exclusive);
                }
            case "0": case "home": case "g0":
                return new Motion(ViStatus.Done, ls, MotionType.Exclusive);
            case "^": case "_":
                return new Motion(ViStatus.Done, TextOps.FirstNonBlank(t, ls), MotionType.Exclusive);
            case "$": case "end": case "g$":
                {
                    int q = p;
                    for (int n = 1; n < count; n++) { int e2 = LineEnd(q); if (e2 >= t.Length) break; q = e2 + 1; }
                    return new Motion(ViStatus.Done, Math.Max(LineStart(q), LineEnd(q) - 1), MotionType.Inclusive);
                }
            case "|":
                return new Motion(ViStatus.Done, Math.Min(le, ls + count - 1), MotionType.Exclusive);
            case "w": case "W": case "M-right":
                {
                    int q = p;
                    for (int n = 0; n < count; n++) q = TextOps.ViNextWordStart(t, q, m == "W");
                    if (q == p && !forOperator) return Motion.Invalid;
                    return new Motion(ViStatus.Done, forOperator ? q : Math.Min(q, Math.Max(0, t.Length)), MotionType.Exclusive);
                }
            case "b": case "B": case "M-left":
                {
                    if (p == 0) return forOperator ? new Motion(ViStatus.Done, 0, MotionType.Exclusive) : Motion.Invalid;
                    int q = p;
                    for (int n = 0; n < count; n++) q = TextOps.ViPrevWordStart(t, q, m == "B");
                    return new Motion(ViStatus.Done, q, MotionType.Exclusive);
                }
            case "e": case "E":
                {
                    if (t.Length == 0) return Motion.Invalid;
                    int q = p;
                    for (int n = 0; n < count; n++) q = TextOps.ViWordEnd(t, q, m == "E");
                    return new Motion(ViStatus.Done, q, MotionType.Inclusive);
                }
            case "ge": case "gE":
                {
                    int q = p;
                    for (int n = 0; n < count; n++) q = TextOps.ViPrevWordEnd(t, q, m == "gE");
                    return new Motion(ViStatus.Done, q, MotionType.Inclusive);
                }
            case "f": case "F": case "t": case "T":
                {
                    if (i >= ks.Count) return Motion.Incomplete;
                    char ch = ks[i].Char; i++;
                    if (ch == '\0') return Motion.Invalid;
                    _viLastFind = (m[0], ch);
                    return FindChar(m[0], ch, count);
                }
            case ";": case ",":
                {
                    if (_viLastFind is not var (fc, fch)) return Motion.Invalid;
                    char cmd = fc;
                    if (m == ",") cmd = cmd switch { 'f' => 'F', 'F' => 'f', 't' => 'T', _ => 't' };
                    return FindChar(cmd, fch, count, repeat: true);
                }
            case "%":
                {
                    int q = TextOps.MatchBracket(t, p);
                    return q < 0 ? Motion.Invalid : new Motion(ViStatus.Done, q, MotionType.Inclusive);
                }
            case "j": case "down": case "C-n": case "+":
                {
                    if (LineEnd(p) >= t.Length)
                    {
                        if (forOperator) return Motion.Invalid;
                        if (m == "+" ) return Motion.Invalid;
                        HistoryMove(count);
                        return new Motion(ViStatus.Done, Point, MotionType.Linewise);
                    }
                    _goalCol = -1;
                    var save = Point;
                    MoveLine(count);
                    int q = Point; Point = save;
                    if (m == "+") q = TextOps.FirstNonBlank(t, LineStart(q));
                    return new Motion(ViStatus.Done, q, MotionType.Linewise);
                }
            case "k": case "up": case "C-p": case "-":
                {
                    if (ls == 0)
                    {
                        if (forOperator) return Motion.Invalid;
                        if (m == "-") return Motion.Invalid;
                        HistoryMove(-count);
                        return new Motion(ViStatus.Done, Point, MotionType.Linewise);
                    }
                    _goalCol = -1;
                    var save = Point;
                    MoveLine(-count);
                    int q = Point; Point = save;
                    if (m == "-") q = TextOps.FirstNonBlank(t, LineStart(q));
                    return new Motion(ViStatus.Done, q, MotionType.Linewise);
                }
            case "G":
                {
                    if (t.Contains('\n') || forOperator)
                    {
                        int target = t.Length;
                        if (hasCount) { target = 0; for (int n = 1; n < count; n++) { int e2 = LineEnd(target); if (e2 >= t.Length) break; target = e2 + 1; } }
                        return new Motion(ViStatus.Done, TextOps.FirstNonBlank(t, LineStart(target)), MotionType.Linewise);
                    }
                    if (hasCount) HistoryGoto(Math.Clamp(count - 1, 0, History.Count), pointAtEnd: false);
                    else HistoryGoto(History.Count, pointAtEnd: false);
                    return new Motion(ViStatus.Done, Point, MotionType.Linewise);
                }
            case "gg":
                {
                    if (t.Contains('\n') || forOperator)
                    {
                        int target = 0;
                        for (int n = 1; n < count && hasCount; n++) { int e2 = LineEnd(target); if (e2 >= t.Length) break; target = e2 + 1; }
                        return new Motion(ViStatus.Done, TextOps.FirstNonBlank(t, target), MotionType.Linewise);
                    }
                    if (History.Count == 0) return Motion.Invalid;
                    HistoryGoto(hasCount ? Math.Clamp(count - 1, 0, History.Count) : 0, pointAtEnd: false);
                    return new Motion(ViStatus.Done, Point, MotionType.Linewise);
                }
            case "'": case "`":
                return ViMarkMotion(m, ks, ref i);
            case "n": case "N":
                if (forOperator) return new Motion(ViStatus.Invalid, 0, MotionType.Exclusive);
                if (_viSearch.Length == 0) return Motion.Invalid;
                return ViSearchHistory(m == "n" ? _viSearchBackward : !_viSearchBackward, count)
                    ? new Motion(ViStatus.Done, Point, MotionType.Exclusive) : Motion.Invalid;
        }
        return new Motion(ViStatus.NotMotion, 0, MotionType.Exclusive);
    }

    void HistoryMove(int delta)
    {
        int target = Math.Clamp(HistIndex + delta, 0, History.Count);
        if (target == HistIndex) { Bell(); return; }
        HistoryGoto(target, pointAtEnd: false);
    }

    Motion FindChar(char cmd, char ch, int count, bool repeat = false)
    {
        var t = Text; int p = Point;
        int ls = LineStart(p), le = LineEnd(p);
        int q = p;
        for (int n = 0; n < count; n++)
        {
            if (cmd is 'f' or 't')
            {
                int from = q + 1;
                if (cmd == 't' && repeat && n == 0 && q + 1 < le && t[q + 1] == ch) from = q + 2;
                int f = from <= le ? t.IndexOf(ch, from, le - from) : -1;
                if (f < 0) return Motion.Invalid;
                q = f;
            }
            else
            {
                int from = q - 1;
                if (cmd == 'T' && repeat && n == 0 && q - 1 >= ls && t[q - 1] == ch) from = q - 2;
                int f = from >= ls ? t.LastIndexOf(ch, from, from - ls + 1) : -1;
                if (f < 0) return Motion.Invalid;
                q = f;
            }
        }
        return cmd switch
        {
            'f' => new Motion(ViStatus.Done, q, MotionType.Inclusive),
            't' => new Motion(ViStatus.Done, q - 1, MotionType.Inclusive),
            'F' => new Motion(ViStatus.Done, q, MotionType.Exclusive),
            _ => new Motion(ViStatus.Done, q + 1, MotionType.Exclusive),
        };
    }

    // ---- text objects ----

    (int, int)? TextObject(bool inner, string obj, int count)
    {
        var t = Text; int p = Math.Min(Point, Math.Max(0, t.Length - 1));
        if (t.Length == 0) return null;
        switch (obj)
        {
            case "w": case "W":
                {
                    bool big = obj == "W";
                    int cls = TextOps.ViClass(t[p], big);
                    int a = p, b = p;
                    while (a > 0 && t[a - 1] != '\n' && TextOps.ViClass(t[a - 1], big) == cls) a--;
                    while (b < t.Length && t[b] != '\n' && TextOps.ViClass(t[b], big) == cls) b++;
                    for (int n = 1; n < count; n++)
                    {
                        int c2 = b < t.Length ? TextOps.ViClass(t[b], big) : -1;
                        if (c2 < 0) break;
                        while (b < t.Length && t[b] != '\n' && TextOps.ViClass(t[b], big) == c2) b++;
                    }
                    if (!inner)
                    {
                        int b2 = b;
                        while (b2 < t.Length && (t[b2] == ' ' || t[b2] == '\t')) b2++;
                        if (b2 > b && cls != 0) b = b2;
                        else while (a > 0 && (t[a - 1] == ' ' || t[a - 1] == '\t')) a--;
                    }
                    return (a, b);
                }
            case "\"": case "'": case "`":
                {
                    char q = obj[0];
                    int ls = LineStart(p), le = LineEnd(p);
                    var qs = new List<int>();
                    for (int k = ls; k < le; k++) if (t[k] == q && (k == ls || t[k - 1] != '`' || q == '`')) qs.Add(k);
                    for (int k = 0; k + 1 < qs.Count; k += 2)
                    {
                        int s = qs[k], e = qs[k + 1];
                        if ((p >= s && p <= e) || p < s)
                        {
                            if (inner) return (s + 1, e);
                            int e2 = e + 1;
                            while (e2 < le && (t[e2] == ' ' || t[e2] == '\t')) e2++;
                            return (s, e2);
                        }
                    }
                    return null;
                }
            case "(": case ")": case "b": return Brackets('(', ')', inner, count);
            case "{": case "}": case "B": return Brackets('{', '}', inner, count);
            case "[": case "]": return Brackets('[', ']', inner, count);
            case "<": case ">": return Brackets('<', '>', inner, count);
        }
        return null;
    }

    (int, int)? Brackets(char open, char close, bool inner, int count)
    {
        var t = Text; int p = Math.Min(Point, t.Length - 1);
        int a = -1;
        int start = p;
        if (t[p] == close) start = p - 1;
        for (int n = 0; n < count; n++)
        {
            int depth = 0; a = -1;
            for (int k = start; k >= 0; k--)
            {
                if (t[k] == close && k != p) depth++;
                else if (t[k] == open)
                {
                    if (depth == 0) { a = k; break; }
                    depth--;
                }
            }
            if (a < 0) return null;
            start = a - 1;
            if (n + 1 < count && start < 0) return null;
        }
        int d2 = 0, b = -1;
        for (int k = a + 1; k < t.Length; k++)
        {
            if (t[k] == open) d2++;
            else if (t[k] == close) { if (d2 == 0) { b = k; break; } d2--; }
        }
        if (b < 0) return null;
        return inner ? (a + 1, b) : (a, b + 1);
    }

    // ---- registers ----

    void SetRegister(char reg, string text, bool linewise, bool yank, bool block = false)
    {
        if (reg == '_') return;
        var r = new Register(text, linewise, block);
        if (reg is '+' or '*') TrySetClipboard(text);
        if (reg >= 'A' && reg <= 'Z')
        {
            char lower = char.ToLowerInvariant(reg);
            var old = Registers.GetValueOrDefault(lower);
            r = old == null ? r : new Register(old.Text + (linewise || old.Linewise ? "\n" : "") + text, linewise || old.Linewise);
            Registers[lower] = r;
        }
        else if (reg >= 'a' && reg <= 'z') Registers[reg] = r;
        Registers['"'] = r;
        if (yank) Registers['0'] = r;
        else
        {
            // shift numbered delete registers
            for (char c = '9'; c > '1'; c--) if (Registers.TryGetValue((char)(c - 1), out var prev)) Registers[c] = prev;
            Registers['1'] = r;
        }
    }

    void TrySetClipboard(string text)
    {
        try
        {
            using var ps = System.Management.Automation.PowerShell.Create(System.Management.Automation.RunspaceMode.CurrentRunspace);
            ps.AddCommand("Set-Clipboard").AddParameter("Value", text);
            ps.Invoke();
        }
        catch { }
    }

    Register? GetRegister(char reg)
    {
        if (reg is '+' or '*')
        {
            var s = Shell.Evaluate(System.Management.Automation.ScriptBlock.Create("Get-Clipboard -Raw"));
            if (string.IsNullOrEmpty(s)) return null;
            s = s.Replace("\r\n", "\n");
            bool lw = s.EndsWith('\n');
            return new Register(lw ? s.TrimEnd('\n') : s, lw);
        }
        if (reg == '\0') reg = '"';
        return Registers.GetValueOrDefault(char.ToLowerInvariant(reg));
    }

    // ---- operators ----

    ViStatus ViOperator(string op, int a, int b, bool linewise, char reg, int count, string beforeText, int beforePoint)
    {
        a = Math.Clamp(a, 0, Text.Length); b = Math.Clamp(b, a, Text.Length);
        switch (op)
        {
            case "y":
                SetRegister(reg, Text.Substring(a, b - a), linewise, yank: true);
                if (!linewise) Point = a;
                else if (Point > b) Point = a;
                return ViStatus.Done;
            case "d":
                {
                    SetRegister(reg, Text.Substring(a, b - a), linewise, yank: false);
                    if (linewise)
                    {
                        if (b < Text.Length) b++;            // take the following newline
                        else if (a > 0) a--;                 // or the preceding one
                    }
                    Text = Text.Remove(a, b - a);
                    Point = linewise ? TextOps.FirstNonBlank(Text, LineStart(Math.Min(a, Text.Length))) : a;
                    return ViStatus.Done;
                }
            case "c":
                {
                    SetRegister(reg, Text.Substring(a, b - a), linewise, yank: false);
                    if (linewise)
                    {
                        // keep indentation of the first line
                        int fnb = TextOps.FirstNonBlank(Text, a);
                        if (fnb <= b) a = fnb;
                    }
                    Text = Text.Remove(a, b - a);
                    ViEnterInsert(a, 1, (beforeText, beforePoint));
                    return ViStatus.Done;
                }
            case ">":
            case "<":
                {
                    int s = LineStart(a);
                    var sb = new System.Text.StringBuilder(Text.Substring(0, s));
                    var block = Text.Substring(s, b - s);
                    var lines = block.Split('\n');
                    int width = Math.Max(1, Options.GetInt("indent_offset", 4));
                    for (int n = 0; n < lines.Length; n++)
                    {
                        var l = lines[n];
                        if (op == ">") { if (l.Length > 0) l = (Options.GetBool("indent_tabs") && width % TabWidth == 0 ? new string('\t', width / TabWidth) : new string(' ', width)) + l; }
                        else { int r = 0; while (r < width && r < l.Length && l[r] == ' ') r++; if (r == 0 && l.StartsWith('\t')) r = 1; l = l.Substring(r); }
                        lines[n] = l;
                    }
                    sb.Append(string.Join('\n', lines)).Append(Text.Substring(b));
                    Text = sb.ToString();
                    Point = TextOps.FirstNonBlank(Text, s);
                    return ViStatus.Done;
                }
            case "g~":
            case "gu":
            case "gU":
                {
                    var part = Text.Substring(a, b - a);
                    part = op switch
                    {
                        "gu" => part.ToLowerInvariant(),
                        "gU" => part.ToUpperInvariant(),
                        _ => new string(part.Select(ch => char.IsUpper(ch) ? char.ToLowerInvariant(ch) : char.ToUpperInvariant(ch)).ToArray()),
                    };
                    Text = Text.Substring(0, a) + part + Text.Substring(b);
                    Point = a;
                    return ViStatus.Done;
                }
        }
        return ViStatus.Invalid;
    }

    // ---- simple commands ----

    ViStatus ViSimple(string c, int count1, char reg, List<KeyInput> ks, ref int i, string beforeText, int beforePoint)
    {
        int count = Math.Max(1, count1);
        var undo = (beforeText, beforePoint);
        int ls = LineStart(Point), le = LineEnd(Point);
        switch (c)
        {
            case "x": case "delete":
                if (le == ls) return ViStatus.Invalid;
                return ViOperator("d", Point, Math.Min(le, Point + count), false, reg, count, beforeText, beforePoint);
            case "X":
                if (Point == ls) return ViStatus.Invalid;
                return ViOperator("d", Math.Max(ls, Point - count), Point, false, reg, count, beforeText, beforePoint);
            case "s":
                return ViOperator("c", Point, Math.Min(le, Point + count), false, reg, count, beforeText, beforePoint);
            case "S":
                return ViOperator("c", ls, LineEndAfter(Point, count), true, reg, count, beforeText, beforePoint);
            case "C":
                return ViOperator("c", Point, LineEndAfter(Point, count), false, reg, count, beforeText, beforePoint);
            case "D":
                {
                    var st = ViOperator("d", Point, LineEndAfter(Point, count), false, reg, count, beforeText, beforePoint);
                    return st;
                }
            case "Y":
                return ViOperator("y", ls, LineEndAfter(Point, count), true, reg, count, beforeText, beforePoint);
            case "p": case "P":
                {
                    var r = GetRegister(reg);
                    if (r == null) return ViStatus.Invalid;
                    PasteRegister(r, before: c == "P", count);
                    return ViStatus.Done;
                }
            case "r":
                {
                    if (i >= ks.Count) return ViStatus.Incomplete;
                    var key = ks[i]; i++;
                    char ch = key.Char;
                    if (key.Name == "RET") ch = '\n';
                    if (ch == '\0') return ViStatus.Invalid;
                    if (Point + count > le) return ViStatus.Invalid;
                    if (ch == '\n')
                    {
                        Text = Text.Substring(0, Point) + "\n" + Text.Substring(Point + count);
                        Point++;
                    }
                    else
                    {
                        Text = Text.Substring(0, Point) + new string(ch, count) + Text.Substring(Point + count);
                        Point += count - 1;
                    }
                    return ViStatus.Done;
                }
            case "R":
                _viReplace = true;
                _viReplaceOrig = Text;
                _viReplaceStart = Point;
                Overwrite = true;
                ViEnterInsert(Point, count, undo);
                _viInsertCount = 1;
                return ViStatus.Done;
            case "~":
                {
                    if (le == ls) return ViStatus.Invalid;
                    int e2 = Math.Min(le, Point + count);
                    var part = new string(Text.Substring(Point, e2 - Point).Select(ch => char.IsUpper(ch) ? char.ToLowerInvariant(ch) : char.ToUpperInvariant(ch)).ToArray());
                    Text = Text.Substring(0, Point) + part + Text.Substring(e2);
                    Point = e2;
                    return ViStatus.Done;
                }
            case "J": case "gJ":
                {
                    int joins = Math.Max(1, count - 1);
                    for (int n = 0; n < joins; n++)
                    {
                        int e2 = LineEnd(Point);
                        if (e2 >= Text.Length) return n > 0 ? ViStatus.Done : ViStatus.Invalid;
                        int s2 = e2 + 1;
                        if (c == "J") while (s2 < Text.Length && (Text[s2] == ' ' || Text[s2] == '\t')) s2++;
                        var sep = c == "J" && e2 > LineStart(Point) && s2 < Text.Length && Text[s2] != '\n' && Text[s2] != ')' ? " " : "";
                        Text = Text.Substring(0, e2) + sep + Text.Substring(s2);
                        Point = e2;
                    }
                    return ViStatus.Done;
                }
            case "i": ViEnterInsert(Point, count, undo); return ViStatus.Done;
            case "a": ViEnterInsert(le > ls ? Math.Min(le, Point + 1) : Point, count, undo); return ViStatus.Done;
            case "I": ViEnterInsert(TextOps.FirstNonBlank(Text, ls), count, undo); return ViStatus.Done;
            case "gI": ViEnterInsert(ls, count, undo); return ViStatus.Done;
            case "A": ViEnterInsert(le, count, undo); return ViStatus.Done;
            case "o":
                Text = Text.Insert(le, "\n");
                ViEnterInsert(le + 1, 1, undo);
                return ViStatus.Done;
            case "O":
                Text = Text.Insert(ls, "\n");
                ViEnterInsert(ls, 1, undo);
                return ViStatus.Done;
            case "u":
                InvokeWidgetNoUndo("undo", count);
                return ViStatus.Done;
            case "C-r":
                InvokeWidgetNoUndo("redo", count);
                return ViStatus.Done;
            case "U":
                InvokeWidgetNoUndo("revert-line", 1);
                return ViStatus.Done;
            case ".":
                {
                    if (_viDotKeys == null) return ViStatus.Invalid;
                    var keys = _viDotKeys.ToList();
                    if (count1 > 0)
                    {
                        int j = 0;
                        if (keys.Count > 1 && keys[0].Name == "\"") j = 2;
                        int k2 = j;
                        ParseCount(keys, ref k2);
                        keys.RemoveRange(j, k2 - j);
                        keys.InsertRange(j, count1.ToString().Select(ch => KeyInput.FromName(ch.ToString())));
                    }
                    _viKeys.Clear();
                    _viReplaying = true;
                    try
                    {
                        foreach (var key in keys) Dispatch(key);
                        if (Keymap == "vi_imap") ViLeaveInsert();
                    }
                    finally { _viReplaying = false; }
                    _viKeys.Add(KeyInput.FromName("."));
                    if (Text != beforeText) PushUndo(beforeText, beforePoint);
                    _viUndoHandled = true;
                    return ViStatus.Done;
                }
            case "v":
                Mark = Point; MarkActive = true; MarkKind = MarkKind.ViChar;
                SetKeymap("vi_xmap");
                return ViStatus.Done;
            case "V":
                Mark = Point; MarkActive = true; MarkKind = MarkKind.ViLine;
                SetKeymap("vi_xmap");
                return ViStatus.Done;
            case "C-v": case "C-q":
                Mark = Point; MarkActive = true; MarkKind = MarkKind.ViBlock; _viBlockEol = false;
                SetKeymap("vi_xmap");
                return ViStatus.Done;
            case "gh":
                EnterSelect(MarkKind.ViChar);
                return ViStatus.Done;
            case "gH":
                EnterSelect(MarkKind.ViLine);
                return ViStatus.Done;
            case "S-left": case "S-right": case "S-up": case "S-down": case "S-home": case "S-end":
                EnterSelect(MarkKind.ViChar);
                SelectKey(KeyInput.FromName(c));
                return ViStatus.Done;
            case "q":
                return ViMacroRecord(ks, ref i);
            case "@":
                return ViMacroPlay(count, ks, ref i);
            case "m":
                return ViSetMark(ks, ref i);
            case ":":
                ViReadEx(count1 > 1 ? $".,.+{count1 - 1}" : count1 == 1 ? "." : "");
                return ViStatus.Done;
            case "/": case "?":
                ViReadSearch(c == "/");
                return ViStatus.Done;
            case "C-j":
                AcceptLine();
                return ViStatus.Done;
            case "RET": case "C-m":
                // ble.sh: accept-single-line-or vi-command/forward-first-non-space
                if (Text.Contains('\n') || Highlighter.IsIncomplete(Text))
                {
                    if (LineEnd(Point) >= Text.Length) return ViStatus.Invalid;
                    Point = TextOps.FirstNonBlank(Text, LineEnd(Point) + 1);
                    return ViStatus.Done;
                }
                AcceptLine();
                return ViStatus.Done;
            case "C-c":
                // ble.sh vi-command/cancel: normal mode does not discard the line
                InvokeWidget("vi-command/cancel", default);
                return ViStatus.Done;
            case "K": case "f1":
                InvokeWidget("command-help", default);
                return ViStatus.Done;
            case "C-g":
                InvokeWidget("show-line-info", default);
                return ViStatus.Done;
            case "C-delete":
                return ViOperator("d", Point, TextOps.ForwardWord(Text, Point, TextOps.IsCWord), false, reg, count, beforeText, beforePoint);
            case "C-l":
                InvokeWidget("clear-screen", default);
                return ViStatus.Done;
            case "C-d":
                if (Text.Length == 0) { Result = "exit"; Done = true; return ViStatus.Done; }
                return ViStatus.Invalid;
            case "#":
                InvokeWidget("insert-comment", default);
                return ViStatus.Done;
            case "C-a": case "C-x":
                return ViIncrement(c == "C-a" ? count : -count) ? ViStatus.Done : ViStatus.Invalid;
            case "ZZ":
                AcceptLine();
                return ViStatus.Done;
            case "ESC":
                if (_viOneCommand) { _viOneCommand = false; SetKeymap("vi_imap"); return ViStatus.Done; }
                return ViStatus.Invalid;
            case "TAB":
                InvokeWidget("complete", default);
                return ViStatus.Done;
        }
        return ViStatus.Invalid;
    }

    int LineEndAfter(int pos, int count)
    {
        int q = pos;
        for (int n = 1; n < count; n++) { int e2 = LineEnd(q); if (e2 >= Text.Length) break; q = e2 + 1; }
        return LineEnd(q);
    }

    void InvokeWidgetNoUndo(string name, int count)
    {
        var save = Arg;
        Arg = count > 1 ? count.ToString() : "";
        Widgets[name](this, default);
        Arg = save;
        _viUndoHandled = true;
    }

    bool ViIncrement(int delta)
    {
        int ls = LineStart(Point), le = LineEnd(Point);
        int p = Point;
        // find a number at or after the cursor on this line
        int s = p;
        while (s > ls && char.IsDigit(Text[s - 1]) && char.IsDigit(Text[Math.Min(s, le - 1)])) s--;
        if (s < le && !char.IsDigit(Text[s]))
        {
            while (s < le && !char.IsDigit(Text[s])) s++;
            if (s >= le) return false;
        }
        int e = s;
        while (e < le && char.IsDigit(Text[e])) e++;
        if (s > ls && Text[s - 1] == '-') s--;
        if (!long.TryParse(Text.AsSpan(s, e - s), out var v)) return false;
        var nv = (v + delta).ToString();
        Text = Text.Substring(0, s) + nv + Text.Substring(e);
        Point = s + nv.Length - 1;
        return true;
    }

    // ---- visual mode ----

    ViStatus ViVisual(string c, int count1, char reg, List<KeyInput> ks, ref int i)
    {
        int count = Math.Max(1, count1);
        var beforeText = Text; var beforePoint = Point;
        void Exit() { MarkActive = false; MarkKind = MarkKind.None; SetKeymap("vi_nmap"); }
        if (MarkKind == MarkKind.ViBlock)
        {
            var br = ViBlockCommand(c, count, reg, ks, ref i, beforeText, beforePoint);
            if (br != null) return br.Value;
        }
        var (a, b) = RegionBounds();
        bool lw = MarkKind == MarkKind.ViLine;
        switch (c)
        {
            case "ESC": case "C-c": case "C-[":
                Exit(); return ViStatus.Done;
            case "C-v": case "C-q":
                if (MarkKind == MarkKind.ViBlock) Exit(); else { MarkKind = MarkKind.ViBlock; _viBlockEol = false; }
                return ViStatus.Done;
            case "C-g":
                if (MarkKind == MarkKind.ViBlock) MarkKind = MarkKind.ViChar;
                SetKeymap("vi_smap");
                return ViStatus.Done;
            case ":":
                _viVisualLines = (LineNumber(a), LineNumber(Math.Max(a, b - 1)));
                Exit();
                ViReadEx("'<,'>");
                return ViStatus.Done;
            case "v":
                if (MarkKind == MarkKind.ViChar) Exit(); else MarkKind = MarkKind.ViChar;
                return ViStatus.Done;
            case "V":
                if (MarkKind == MarkKind.ViLine) Exit(); else MarkKind = MarkKind.ViLine;
                return ViStatus.Done;
            case "o":
                (Mark, Point) = (Point, Mark);
                return ViStatus.Done;
            case "d": case "x": case "delete": case "D": case "X":
                {
                    bool l2 = lw || c is "D" or "X";
                    if (l2 && !lw) { a = LineStart(a); b = LineEnd(b); }
                    Exit();
                    return ViOperator("d", a, b, l2, reg, 1, beforeText, beforePoint);
                }
            case "c": case "s": case "C": case "S": case "R":
                {
                    bool l2 = lw || c is "C" or "S" or "R";
                    if (l2 && !lw) { a = LineStart(a); b = LineEnd(b); }
                    MarkActive = false; MarkKind = MarkKind.None;
                    return ViOperator("c", a, b, l2, reg, 1, beforeText, beforePoint);
                }
            case "y": case "Y":
                {
                    bool l2 = lw || c == "Y";
                    if (l2 && !lw) { a = LineStart(a); b = LineEnd(b); }
                    Exit();
                    return ViOperator("y", a, b, l2, reg, 1, beforeText, beforePoint);
                }
            case "~": case "u": case "U":
                Exit();
                return ViOperator(c == "~" ? "g~" : c == "u" ? "gu" : "gU", a, b, false, reg, 1, beforeText, beforePoint);
            case ">": case "<":
                Exit();
                return ViOperator(c, a, b, true, reg, 1, beforeText, beforePoint);
            case "J":
                {
                    Exit();
                    Point = a;
                    int lines = Text.Substring(a, b - a).Count(ch => ch == '\n') + 1;
                    int ii = 0;
                    return ViSimple("J", Math.Max(2, lines), reg, ks, ref ii, beforeText, beforePoint);
                }
            case "p": case "P":
                {
                    var r = GetRegister(reg);
                    if (r == null) return ViStatus.Invalid;
                    Exit();
                    var removed = Text.Substring(a, b - a);
                    Text = Text.Remove(a, b - a).Insert(a, r.Text);
                    Point = Math.Max(a, a + r.Text.Length - 1);
                    if (c == "p") SetRegister('\0', removed, lw, yank: false);
                    return ViStatus.Done;
                }
            case "r":
                {
                    if (i >= ks.Count) return ViStatus.Incomplete;
                    char ch = ks[i].Char; i++;
                    if (ch == '\0') return ViStatus.Invalid;
                    Exit();
                    var part = new string(Text.Substring(a, b - a).Select(x => x == '\n' ? x : ch).ToArray());
                    Text = Text.Substring(0, a) + part + Text.Substring(b);
                    Point = a;
                    return ViStatus.Done;
                }
            case "i": case "a":
                {
                    if (i >= ks.Count) return ViStatus.Incomplete;
                    var obj = ks[i].Name; i++;
                    var r = TextObject(c == "i", obj, count);
                    if (r == null) return ViStatus.Invalid;
                    Mark = r.Value.Item1;
                    Point = Math.Max(r.Value.Item1, r.Value.Item2 - 1);
                    MarkKind = MarkKind.ViChar;
                    return ViStatus.Done;
                }
            case "I":
                Exit(); ViEnterInsert(a, 1, (beforeText, beforePoint)); return ViStatus.Done;
            case "A":
                Exit(); ViEnterInsert(b, 1, (beforeText, beforePoint)); return ViStatus.Done;
            case "RET":
                Exit(); AcceptLine(); return ViStatus.Done;
        }
        int ii2 = i;
        var mo = ViMotion(c, count, count1 > 0, ks, ref ii2, forOperator: true);
        if (mo.Status == ViStatus.Incomplete) return ViStatus.Incomplete;
        if (mo.Status == ViStatus.Done)
        {
            Point = Math.Clamp(mo.Pos, 0, Math.Max(0, Text.Length - 1));
            if (MarkKind == MarkKind.ViBlock) _viBlockEol = c is "$" or "end";
            return ViStatus.Done;
        }
        return ViStatus.Invalid;
    }

    // ---- history search (/ ? n N) ----

    void ViReadSearch(bool backwardOlder)
    {
        string q = "";
        string prefix = backwardOlder ? "/" : "?";
        Info = prefix;
        InfoFace = "status_line";
        Modal = key =>
        {
            switch (key.Name)
            {
                case "RET": case "C-m": case "C-j":
                    Modal = null; Info = null;
                    if (q.Length > 0) _viSearch = q;
                    _viSearchBackward = backwardOlder;
                    if (_viSearch.Length == 0 || !ViSearchHistory(backwardOlder, 1)) Bell();
                    ViClampNormal();
                    return true;
                case "ESC": case "C-c": case "C-g":
                    Modal = null; Info = null;
                    return true;
                case "DEL": case "C-h":
                    if (q.Length == 0) { Modal = null; Info = null; return true; }
                    q = q.Substring(0, q.Length - 1);
                    Info = prefix + q;
                    return true;
            }
            if (key.Char != '\0') { q += key.Char; Info = prefix + q; }
            return true;
        };
    }

    bool ViSearchHistory(bool older, int count)
    {
        var cmp = History.SmartCase(_viSearch);
        int idx = HistIndex;
        for (int n = 0; n < count; n++)
        {
            int found = -1;
            if (older) { for (int j = Math.Min(idx - 1, History.Count - 1); j >= 0; j--) if (History[j].Contains(_viSearch, cmp)) { found = j; break; } }
            else { for (int j = idx + 1; j < History.Count; j++) if (History[j].Contains(_viSearch, cmp)) { found = j; break; } }
            if (found < 0) return n > 0 && Commit(idx);
            idx = found;
        }
        return Commit(idx);

        bool Commit(int target)
        {
            HistoryGoto(target, pointAtEnd: false);
            int at = Text.IndexOf(_viSearch, cmp);
            Point = Math.Max(0, at);
            return true;
        }
    }

    /// <summary>Called by the insert-mode BS in replace mode: restore the original character.</summary>
    internal bool ViReplaceBackspace()
    {
        if (!_viReplace || Keymap != "vi_imap") return false;
        if (Point <= _viReplaceStart) { Bell(); return true; }
        int p = Point - 1;
        if (p < _viReplaceOrig.Length && p >= _viReplaceStart)
        {
            // only valid when the text hasn't shifted: in replace mode text only grows at the end of the line
            Text = Text.Substring(0, p) + _viReplaceOrig[p] + Text.Substring(p + 1);
        }
        else Text = Text.Remove(p, 1);
        Point = p;
        return true;
    }
}
