using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace Ble;

public sealed partial class Editor
{
    public delegate void WidgetFn(Editor e, KeyInput k);

    /// <summary>All widgets by name (ble.sh naming).</summary>
    public static readonly Dictionary<string, WidgetFn> Widgets = new(StringComparer.Ordinal);

    static void W(string name, WidgetFn fn) => Widgets[name] = fn;

    public int HistIndex;
    readonly Dictionary<int, string> _histEdits = new();
    string? _histSearchPrefix;
    int _ylaHistory = -1;
    (int Start, int End) _ylaRange;

    static Editor()
    {
        RegisterBasicWidgets();
        RegisterHistoryWidgets();
        RegisterCompletionWidgets();
        RegisterViWidgets();
        RegisterFzfWidgets();
        RegisterBleShWidgets();
    }

    static void RegisterBasicWidgets()
    {
        // ---- insertion ----
        W("self-insert", (e, k) =>
        {
            if (k.Char == '\0') { e.Bell(); return; }
            int n = Math.Max(1, e.CountArg());
            e.Insert(new string(k.Char, n));
        });
        W("quoted-insert", (e, k) =>
        {
            e.Info = "(quoted-insert)";
            e.Modal = key =>
            {
                e.Modal = null;
                e.Info = null;
                char c = key.Char != '\0' ? key.Char : ControlCharOf(key.Name);
                if (c != '\0') { var bt = e.Text; var bp = e.Point; e.Insert(c.ToString()); e.PushUndo(bt, bp); }
                else e.Bell();
                return true;
            };
        });
        W("newline", (e, k) => e.Insert("\n"));
        W("tab-insert", (e, k) => e.Insert("\t"));
        W("magic-space", (e, k) =>
        {
            if ((!e.MarkActive || e.MarkKind != MarkKind.Shift) && Options.Get("edit_magic_expand").Contains("sabbrev")) e.ExpandAbbrev();
            e.Insert(new string(' ', Math.Max(1, e.CountArg())));
        });
        W("overwrite-mode", (e, k) => e.Overwrite = !e.Overwrite);
        W("paste-from-clipboard", (e, k) =>
        {
            var s = e.Shell.Evaluate(ScriptBlock.Create("Get-Clipboard -Raw"));
            if (string.IsNullOrEmpty(s)) { e.Bell(); return; }
            e.Insert(s.Replace("\r\n", "\n").TrimEnd('\n'));
        });

        // ---- movement ----
        W("forward-char", (e, k) =>
        {
            if (e.Point == e.Text.Length && e.AcceptSuggestion(false)) return;
            int n = e.CountArg();
            e.Point = Math.Clamp(e.Point + n, 0, e.Text.Length);
        });
        W("backward-char", (e, k) => e.Point = Math.Clamp(e.Point - e.CountArg(), 0, e.Text.Length));
        W("forward-cword", (e, k) =>
        {
            if (e.Point == e.Text.Length && e.AcceptSuggestion(true)) return;
            for (int i = 0; i < Math.Max(1, e.CountArg()); i++) e.Point = TextOps.ForwardWord(e.Text, e.Point, TextOps.IsCWord);
        });
        W("backward-cword", (e, k) => { for (int i = 0; i < Math.Max(1, e.CountArg()); i++) e.Point = TextOps.BackwardWord(e.Text, e.Point, TextOps.IsCWord); });
        W("forward-uword", (e, k) =>
        {
            if (e.Point == e.Text.Length && e.AcceptSuggestion(true, unixWord: true)) return;
            for (int i = 0; i < Math.Max(1, e.CountArg()); i++) e.Point = TextOps.ForwardWord(e.Text, e.Point, TextOps.IsUWord);
        });
        W("backward-uword", (e, k) => { for (int i = 0; i < Math.Max(1, e.CountArg()); i++) e.Point = TextOps.BackwardWord(e.Text, e.Point, TextOps.IsUWord); });
        Widgets["forward-word"] = Widgets["forward-cword"];
        Widgets["backward-word"] = Widgets["backward-cword"];
        W("beginning-of-line", (e, k) =>
        {
            int ls = e.LineStart(e.Point);
            // second press on a continuation line goes to the beginning of the text
            e.Point = e.Point == ls && e.LastWidget == "beginning-of-line" ? 0 : ls;
        });
        W("end-of-line", (e, k) =>
        {
            if (e.Point == e.Text.Length && e.AcceptSuggestion(false)) return;
            int le = e.LineEnd(e.Point);
            e.Point = e.Point == le && e.LastWidget == "end-of-line" ? e.Text.Length : le;
        });
        W("beginning-of-text", (e, k) => e.Point = 0);
        W("end-of-text", (e, k) =>
        {
            if (e.Point == e.Text.Length && e.AcceptSuggestion(false)) return;
            e.Point = e.Text.Length;
        });
        W("backward-line", (e, k) => { if (!e.MoveLine(-Math.Max(1, e.CountArg()))) e.Bell(); });
        W("forward-line", (e, k) => { if (!e.MoveLine(Math.Max(1, e.CountArg()))) e.Bell(); });
        W("character-search-forward", (e, k) => e.CharSearch(true));
        W("character-search-backward", (e, k) => e.CharSearch(false));

        // shift-selection variants
        foreach (var m in new[] { "forward-char", "backward-char", "forward-cword", "backward-cword", "beginning-of-line", "end-of-line", "beginning-of-text", "end-of-text", "forward-line", "backward-line" })
        {
            var inner = m;
            W("shift-" + m, (e, k) =>
            {
                if (!e.MarkActive || e.MarkKind != MarkKind.Shift) { e.Mark = e.Point; e.MarkActive = true; e.MarkKind = MarkKind.Shift; }
                var s = e._suggest; e._suggest = null;    // moving with shift never accepts suggestions
                Widgets[inner](e, k);
                e._suggest = s;
            });
        }

        // ---- mark & region ----
        W("set-mark", (e, k) => { e.Mark = e.Point; e.MarkActive = true; e.MarkKind = MarkKind.Emacs; });
        W("exchange-point-and-mark", (e, k) =>
        {
            if (e.Mark < 0) { e.Bell(); return; }
            (e.Mark, e.Point) = (Math.Min(e.Point, e.Text.Length), Math.Min(e.Mark, e.Text.Length));
            e.MarkActive = true;
            if (e.MarkKind == MarkKind.None) e.MarkKind = MarkKind.Emacs;
        });
        W("kill-region", (e, k) => { if (e.Mark < 0) { e.Bell(); return; } var (a, b) = e.RegionBounds(); e.Kill(a, b); e.MarkActive = false; });
        W("copy-region", (e, k) => { if (e.Mark < 0) { e.Bell(); return; } var (a, b) = e.RegionBounds(); e.AddKill(e.Text.Substring(a, b - a)); e.MarkActive = false; });
        W("kill-region-or-backward-uword", (e, k) =>
        {
            if (e.MarkActive) Widgets["kill-region"](e, k);
            else e.Kill(TextOps.BackwardWord(e.Text, e.Point, TextOps.IsUWord), e.Point, prepend: true);
        });
        W("copy-region-or-backward-uword", (e, k) =>
        {
            if (e.MarkActive) Widgets["copy-region"](e, k);
            else { int a = TextOps.BackwardWord(e.Text, e.Point, TextOps.IsUWord); e.AddKill(e.Text.Substring(a, e.Point - a)); }
        });
        W("cancel", (e, k) =>
        {
            if (e.MarkActive) { e.MarkActive = false; return; }
            if (e.Menu != null) { e.CloseMenu(); return; }
            if (e.DismissSuggestion()) return;
            e.Arg = "";
            e.Bell();
        });

        // ---- deletion & killing ----
        W("delete-backward-char", (e, k) =>
        {
            if (e.ViReplaceBackspace()) return;
            // ble.sh delete-region-or: an active region is deleted first
            if (e.MarkActive && e.MarkKind is MarkKind.Shift or MarkKind.Emacs) { var (a, b) = e.RegionBounds(); e.Delete(a, b); e.MarkActive = false; return; }
            if (e.Point == 0) { e.Bell(); return; }
            int n = Math.Max(1, e.CountArg());
            int start = Math.Max(0, e.Point - n);
            if (start > 0 && char.IsLowSurrogate(e.Text[start]) && char.IsHighSurrogate(e.Text[start - 1])) start--;
            e.Delete(start, e.Point);
        });
        W("delete-forward-char", (e, k) =>
        {
            if (e.MarkActive && e.MarkKind is MarkKind.Shift or MarkKind.Emacs) { var (a, b) = e.RegionBounds(); e.Delete(a, b); e.MarkActive = false; return; }
            if (e.Point >= e.Text.Length) { e.Bell(); return; }
            int n = Math.Max(1, e.CountArg());
            int end = Math.Min(e.Text.Length, e.Point + n);
            if (end < e.Text.Length && char.IsLowSurrogate(e.Text[end])) end++;
            e.Delete(e.Point, end);
        });
        W("delete-forward-char-or-exit", (e, k) =>
        {
            if (e.Text.Length == 0 && !e.MarkActive) { e.Result = "exit"; e.Done = true; return; }
            Widgets["delete-forward-char"](e, k);
        });
        W("delete-horizontal-space", (e, k) =>
        {
            int a = e.Point, b = e.Point;
            while (a > 0 && (e.Text[a - 1] == ' ' || e.Text[a - 1] == '\t')) a--;
            while (b < e.Text.Length && (e.Text[b] == ' ' || e.Text[b] == '\t')) b++;
            e.Delete(a, b);
        });
        W("kill-forward-line", (e, k) =>
        {
            int le = e.LineEnd(e.Point);
            if (le == e.Point && le < e.Text.Length) le++;
            e.Kill(e.Point, le);
        });
        W("kill-backward-line", (e, k) =>
        {
            int ls = e.LineStart(e.Point);
            if (ls == e.Point && ls > 0) ls--;
            e.Kill(ls, e.Point, prepend: true);
        });
        W("kill-whole-line", (e, k) =>
        {
            int ls = e.LineStart(e.Point), le = e.LineEnd(e.Point);
            if (le < e.Text.Length) le++; else if (ls > 0) ls--;
            e.Kill(ls, le);
        });
        W("kill-forward-cword", (e, k) => e.Kill(e.Point, TextOps.ForwardWord(e.Text, e.Point, TextOps.IsCWord)));
        W("kill-backward-cword", (e, k) => e.Kill(TextOps.BackwardWord(e.Text, e.Point, TextOps.IsCWord), e.Point, prepend: true));
        W("kill-forward-uword", (e, k) => e.Kill(e.Point, TextOps.ForwardWord(e.Text, e.Point, TextOps.IsUWord)));
        W("kill-backward-uword", (e, k) => e.Kill(TextOps.BackwardWord(e.Text, e.Point, TextOps.IsUWord), e.Point, prepend: true));
        Widgets["kill-word"] = Widgets["kill-forward-cword"];
        Widgets["backward-kill-word"] = Widgets["kill-backward-cword"];
        Widgets["unix-word-rubout"] = Widgets["kill-backward-uword"];
        Widgets["unix-line-discard"] = Widgets["kill-backward-line"];
        Widgets["kill-line"] = Widgets["kill-forward-line"];

        // ---- yank ----
        W("yank", (e, k) =>
        {
            if (KillRing.Count == 0) { e.Bell(); return; }
            int start = e.Point;
            e._yankIndex = 0;
            e.Insert(KillRing[0]);
            e._yankRange = (start, e.Point);
            e._yankFlag = true;
        });
        W("yank-pop", (e, k) =>
        {
            if (!e._lastWasYank || KillRing.Count == 0) { e.Bell(); return; }
            e._yankIndex = (e._yankIndex + 1) % KillRing.Count;
            var (a, b) = e._yankRange;
            e.Text = e.Text.Remove(a, b - a).Insert(a, KillRing[e._yankIndex]);
            e.Point = a + KillRing[e._yankIndex].Length;
            e._yankRange = (a, e.Point);
            e._yankFlag = true;
        });

        // ---- transposition & case ----
        W("transpose-chars", (e, k) =>
        {
            var t = e.Text; int p = e.Point;
            if (t.Length < 2 || p == 0) { e.Bell(); return; }
            if (p == t.Length || t[p] == '\n') p--;
            if (p == 0 || t[p - 1] == '\n') { e.Bell(); return; }
            var arr = t.ToCharArray();
            (arr[p - 1], arr[p]) = (arr[p], arr[p - 1]);
            e.Text = new string(arr);
            e.Point = p + 1;
        });
        W("transpose-words", (e, k) =>
        {
            var t = e.Text;
            int end2 = TextOps.ForwardWord(t, e.Point, TextOps.IsCWord);
            int start2 = TextOps.BackwardWord(t, end2, TextOps.IsCWord);
            int start1 = TextOps.BackwardWord(t, start2, TextOps.IsCWord);
            int end1 = TextOps.ForwardWord(t, start1, TextOps.IsCWord);
            if (start1 == start2 || end1 > start2) { e.Bell(); return; }
            var w1 = t.Substring(start1, end1 - start1);
            var mid = t.Substring(end1, start2 - end1);
            var w2 = t.Substring(start2, end2 - start2);
            e.Text = t.Substring(0, start1) + w2 + mid + w1 + t.Substring(end2);
            e.Point = end2;
        });
        W("upcase-word", (e, k) => e.CaseWord(s => s.ToUpperInvariant()));
        W("downcase-word", (e, k) => e.CaseWord(s => s.ToLowerInvariant()));
        W("capitalize-word", (e, k) => e.CaseWord(s =>
        {
            var chars = s.ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++) if (char.IsLetterOrDigit(chars[i])) { chars[i] = char.ToUpperInvariant(chars[i]); break; }
            return new string(chars);
        }));

        // ---- undo ----
        W("undo", (e, k) =>
        {
            int n = Math.Max(1, e.CountArg());
            for (int i = 0; i < n; i++)
            {
                if (e._undo.Count == 0) { e.Bell(); return; }
                e._redo.Push((e.Text, e.Point));
                var (t, p) = e._undo.Pop();
                e.SetText(t, p);
            }
        });
        W("redo", (e, k) =>
        {
            int n = Math.Max(1, e.CountArg());
            for (int i = 0; i < n; i++)
            {
                if (e._redo.Count == 0) { e.Bell(); return; }
                e._undo.Push((e.Text, e.Point));
                var (t, p) = e._redo.Pop();
                e.SetText(t, p);
            }
        });
        W("revert-line", (e, k) =>
        {
            if (e._undo.Count == 0) { e.Bell(); return; }
            e._redo.Push((e.Text, e.Point));
            var last = e._undo.Pop();
            while (e._undo.Count > 0) last = e._undo.Pop();
            e.SetText(last.Text, last.Point);
        });

        // ---- arguments ----
        W("digit-argument", (e, k) =>
        {
            char d = k.Name.Length > 0 ? k.Name[^1] : '0';
            if (d == '-') e.Arg = e.Arg.Length == 0 ? "-" : e.Arg;
            else if (char.IsDigit(d)) e.Arg = (e.Arg == "-" ? "-" : e.Arg) + d;
            e.Info = "(arg: " + e.Arg + ")";
        });

        // ---- accepting ----
        W("accept-line", (e, k) => e.AcceptLine());
        W("accept-or-newline", (e, k) =>
        {
            if (Options.GetBool("edit_paste_newline") && e.Term.KeyAvailable) { e.Insert("\n"); return; }
            if (Highlighter.IsIncomplete(e.Text)) { e.Insert("\n"); return; }
            e.AcceptLine();
        });
        W("accept-single-line-or-newline", (e, k) =>
        {
            if (e.Text.Contains('\n') || (Options.GetBool("edit_paste_newline") && e.Term.KeyAvailable)) { e.Insert("\n"); return; }
            if (Highlighter.IsIncomplete(e.Text)) { e.Insert("\n"); return; }
            e.AcceptLine();
        });
        W("accept-suggestion", (e, k) => { if (!e.AcceptListItem() && !e.AcceptSuggestion(false)) e.Bell(); });
        W("accept-suggestion-word", (e, k) => { if (!e.AcceptSuggestion(true)) e.Bell(); });
        W("accept-suggestion-and-execute", (e, k) => { e.AcceptSuggestion(false); e.AcceptLine(); });
        // ble.sh: S-RET inserts the suggestion when one is shown
        W("accept-suggestion-or-newline", (e, k) => { if (!e.AcceptListItem() && !e.AcceptSuggestion(false)) e.Insert("\n"); });
        W("discard-line", (e, k) =>
        {
            e.FinalSuffix = "^C";
            e.Result = "";
            e.Done = true;
        });
        W("insert-comment", (e, k) =>
        {
            var lines = e.Text.Split('\n');
            e.Text = string.Join('\n', lines.Select(l => "#" + l));
            e.Point = e.Text.Length;
            e.AcceptLine();
        });

        // ---- display ----
        W("clear-screen", (e, k) => e.RedrawPrompt(clear: true));
        W("redraw-line", (e, k) => { var m = e.BuildModel(); e.Renderer.Render(m); });
        W("display-shell-version", (e, k) =>
        {
            var v = typeof(Editor).Assembly.GetName().Version;
            var ps = typeof(PSObject).Assembly.GetName().Version;
            e.Info = $"ble.ps1, version {v?.ToString(3)} (PowerShell {ps?.ToString(3)}, {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription})";
        });
        W("edit-and-execute-command", (e, k) => e.EditAndExecute());
        W("bell", (e, k) => e.Bell());
        W("emacs-editing-mode", (e, k) => { Options.Set("default_keymap", "emacs"); e.SetKeymap("emacs"); });
        W("vi-editing-mode", (e, k) => { Options.Set("default_keymap", "vi"); e.SetKeymap("vi_imap"); });
    }

    static char ControlCharOf(string name)
    {
        switch (name)
        {
            case "RET": return '\r';
            case "TAB": return '\t';
            case "ESC": return '\x1b';
            case "DEL": return '\x7f';
            case "SP": return ' ';
        }
        if (name.Length == 3 && name.StartsWith("C-", StringComparison.Ordinal))
        {
            char c = name[2];
            if (c >= 'a' && c <= 'z') return (char)(c - 'a' + 1);
            return c switch { '@' => '\0', '\\' => '\x1c', ']' => '\x1d', '^' => '\x1e', '_' => '\x1f', '?' => '\x7f', _ => '\0' };
        }
        return '\0';
    }

    public void AcceptLine()
    {
        if (Point == Text.Length) ExpandAbbrev();
        Result = Text;
        Done = true;
    }

    void CaseWord(Func<string, string> f)
    {
        int end = TextOps.ForwardWord(Text, Point, TextOps.IsCWord);
        var part = Text.Substring(Point, end - Point);
        Text = Text.Substring(0, Point) + f(part) + Text.Substring(end);
        Point = end;
    }

    /// <summary>Moves the cursor by lines inside a multi-line buffer. Returns false at the boundary.</summary>
    public bool MoveLine(int delta)
    {
        int ls = LineStart(Point);
        if (_goalCol < 0) _goalCol = Point - ls;
        int p = Point;
        if (delta < 0)
        {
            for (int i = 0; i < -delta; i++)
            {
                int cur = LineStart(p);
                if (cur == 0) return i > 0 && Commit(p);
                p = LineStart(cur - 1);
            }
        }
        else
        {
            for (int i = 0; i < delta; i++)
            {
                int le = LineEnd(p);
                if (le >= Text.Length) return i > 0 && Commit(p);
                p = le + 1;
            }
        }
        return Commit(p);

        bool Commit(int lineAnchor)
        {
            int s = LineStart(lineAnchor), e = LineEnd(lineAnchor);
            Point = Math.Min(s + _goalCol, e);
            return true;
        }
    }

    void CharSearch(bool forward)
    {
        int count = Math.Max(1, CountArg());
        Info = forward ? "(char-search)" : "(char-search-backward)";
        Modal = key =>
        {
            Modal = null; Info = null;
            if (key.Char == '\0') { Bell(); return true; }
            int p = Point;
            for (int i = 0; i < count; i++)
            {
                int q = forward ? Text.IndexOf(key.Char, Math.Min(Text.Length, p + 1)) : (p > 0 ? Text.LastIndexOf(key.Char, p - 1) : -1);
                if (q < 0) { Bell(); return true; }
                p = q;
            }
            Point = p;
            return true;
        };
    }

    public void SetKeymap(string km)
    {
        if (Keymap == km) return;
        Keymap = km;
        _pending.Clear();
        ApplyCursorShape();
    }

    // ---- abbreviations (ble-sabbrev) ----

    public static readonly Dictionary<string, object> Abbrevs = new(StringComparer.Ordinal);

    public bool ExpandAbbrev()
    {
        if (Abbrevs.Count == 0) return false;
        int end = Point, start = end;
        while (start > 0 && !char.IsWhiteSpace(Text[start - 1]) && ";|&(){}".IndexOf(Text[start - 1]) < 0) start--;
        if (start == end) return false;
        var word = Text.Substring(start, end - start);
        if (!Abbrevs.TryGetValue(word, out var val)) return false;
        string rep = val is ScriptBlock sb ? Shell.Evaluate(sb) : val.ToString() ?? "";
        Text = Text.Substring(0, start) + rep + Text.Substring(end);
        Point = start + rep.Length;
        return true;
    }

    // ---- external editor ----

    void EditAndExecute()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ble-{Environment.ProcessId}-{DateTime.Now.Ticks}.ps1");
        File.WriteAllText(tmp, Text + "\n");
        var saved = Text;
        string edited = saved;
        RunAboveLine(() =>
        {
            var editor = Options.Get("editor");
            if (string.IsNullOrWhiteSpace(editor)) editor = Environment.GetEnvironmentVariable("VISUAL");
            if (string.IsNullOrWhiteSpace(editor)) editor = Environment.GetEnvironmentVariable("EDITOR");
            if (string.IsNullOrWhiteSpace(editor)) editor = OperatingSystem.IsWindows() ? "notepad" : "vi";
            var (exe, args) = SplitCommandLine(editor!);
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add(tmp);
            using (var p = Process.Start(psi)) p?.WaitForExit();
            edited = File.ReadAllText(tmp).Replace("\r\n", "\n").TrimEnd('\n');
        });
        try { File.Delete(tmp); } catch { }
        Text = edited;
        Point = Text.Length;
        if (Text.Trim().Length == 0) return;
        AcceptLine();
    }

    static (string Exe, List<string> Args) SplitCommandLine(string s)
    {
        var parts = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool q = false;
        foreach (var c in s.Trim())
        {
            if (c == '"') { q = !q; continue; }
            if (char.IsWhiteSpace(c) && !q) { if (cur.Length > 0) { parts.Add(cur.ToString()); cur.Clear(); } continue; }
            cur.Append(c);
        }
        if (cur.Length > 0) parts.Add(cur.ToString());
        if (parts.Count == 0) parts.Add(s);
        return (parts[0], parts.Skip(1).ToList());
    }

    // ---- history ----

    public void HistoryGoto(int idx, bool pointAtEnd = true)
    {
        idx = Math.Clamp(idx, 0, History.Count);
        if (HistIndex == History.Count || HistIndex >= History.Count) _histEdits[History.Count] = Text;
        else if (Text != History[HistIndex]) _histEdits[HistIndex] = Text;
        else _histEdits.Remove(HistIndex);
        HistIndex = idx;
        string t = _histEdits.TryGetValue(idx, out var ed) ? ed : idx < History.Count ? History[idx] : "";
        Text = t;
        Point = pointAtEnd ? t.Length : 0;
        MarkActive = false;
    }

    static void RegisterHistoryWidgets()
    {
        W("history-prev", (e, k) =>
        {
            int n = Math.Max(1, e.CountArg());
            if (e.HistIndex - n < 0) { if (e.HistIndex == 0) { e.Bell(); return; } n = e.HistIndex; }
            e.HistoryGoto(e.HistIndex - n);
        });
        W("history-next", (e, k) =>
        {
            int n = Math.Max(1, e.CountArg());
            if (e.HistIndex >= e.History.Count) { e.Bell(); return; }
            e.HistoryGoto(Math.Min(e.History.Count, e.HistIndex + n));
        });
        W("backward-line-or-history-prev", (e, k) =>
        {
            if (e.LineStart(e.Point) > 0) { e.MoveLine(-1); return; }
            Widgets["history-prev"](e, k);
        });
        W("forward-line-or-history-next", (e, k) =>
        {
            if (e.LineEnd(e.Point) < e.Text.Length) { e.MoveLine(1); return; }
            Widgets["history-next"](e, k);
        });
        W("history-beginning", (e, k) => { if (e.History.Count == 0) { e.Bell(); return; } e.HistoryGoto(0); });
        W("history-end", (e, k) => e.HistoryGoto(e.History.Count));
        W("history-search-backward", (e, k) => e.HistorySearchPrefix(true));
        W("history-search-forward", (e, k) => e.HistorySearchPrefix(false));
        W("history-isearch-backward", (e, k) => e.StartIsearch(true));
        W("history-isearch-forward", (e, k) => e.StartIsearch(false));
        W("yank-last-arg", (e, k) =>
        {
            bool repeat = e.LastWidget is "yank-last-arg" or "insert-last-argument" && e._ylaHistory >= 0;
            int idx = repeat ? e._ylaHistory - 1 : e.History.Count - 1;
            while (idx >= 0)
            {
                var arg = LastArgument(e.History[idx]);
                if (arg != null)
                {
                    if (repeat)
                    {
                        var (a, b) = e._ylaRange;
                        e.Text = e.Text.Remove(a, b - a);
                        e.Point = a;
                    }
                    int start = e.Point;
                    e.Text = e.Text.Insert(e.Point, arg);
                    e.Point += arg.Length;
                    e._ylaRange = (start, e.Point);
                    e._ylaHistory = idx;
                    return;
                }
                idx--;
            }
            e.Bell();
        });
    }

    static string? LastArgument(string line)
    {
        Parser.ParseInput(line, out var tokens, out _);
        for (int i = tokens.Length - 1; i >= 0; i--)
        {
            var t = tokens[i];
            if (t.Kind is TokenKind.EndOfInput or TokenKind.NewLine or TokenKind.Semi or TokenKind.Pipe or TokenKind.Comment) continue;
            return t.Text;
        }
        return null;
    }

    void HistorySearchPrefix(bool backward)
    {
        if (LastWidget is not ("history-search-backward" or "history-search-forward") || _histSearchPrefix == null)
            _histSearchPrefix = Text.Substring(0, Point);
        var prefix = _histSearchPrefix;
        int keep = prefix.Length;
        int idx = backward
            ? History.SearchPrefix(prefix, HistIndex - 1, true, Text)
            : History.SearchPrefix(prefix, HistIndex + 1, false, Text);
        if (idx < 0)
        {
            if (!backward && HistIndex < History.Count)
            {
                HistoryGoto(History.Count);
                Point = Math.Min(keep, Text.Length);
                return;
            }
            Bell();
            return;
        }
        HistoryGoto(idx);
        Point = Math.Min(keep, Text.Length);
    }

    // ---- incremental search ----

    sealed class IsearchState
    {
        public string Query = "";
        public bool Backward;
        public int Index;     // history index (History.Count = the line being edited)
        public int Pos;       // match position
        public bool Failed;
        public string OrigText = "";
        public int OrigPoint;
        public int OrigIndex;
        public readonly Stack<(string Query, int Index, int Pos, bool Failed, bool Backward)> Stack = new();
    }

    void StartIsearch(bool backward)
    {
        var st = new IsearchState { Backward = backward, Index = HistIndex, Pos = Point, OrigText = Text, OrigPoint = Point, OrigIndex = HistIndex };
        UpdateIsearchInfo(st);
        Modal = key => IsearchKey(st, key);
    }

    void UpdateIsearchInfo(IsearchState st)
    {
        var dir = st.Backward ? "reverse-i-search" : "i-search";
        Info = (st.Failed ? "(failed " + dir + ")" : "(" + dir + ")") + "`" + st.Query + "'";
        InfoFace = st.Failed ? "status_error" : "status_info";
        MatchRegion = st.Query.Length > 0 && !st.Failed ? (st.Pos, st.Pos + st.Query.Length) : null;
    }

    bool IsearchFind(IsearchState st, int fromIndex, int fromPos, bool backward, bool includeCurrent)
    {
        var q = st.Query;
        if (q.Length == 0) return true;
        var cmp = History.SmartCase(q);
        int idx = fromIndex;
        int pos = fromPos;
        bool first = true;
        while (idx >= 0 && idx <= History.Count)
        {
            var line = idx == HistIndex ? Text : (idx < History.Count ? History[idx] : _histEdits.GetValueOrDefault(History.Count, ""));
            int found;
            if (line.Length < q.Length) found = -1;
            else if (backward)
            {
                int start = first ? (includeCurrent ? Math.Min(pos, line.Length - q.Length) : pos - 1) : line.Length - q.Length;
                found = start < 0 ? -1 : line.LastIndexOf(q, Math.Min(line.Length - 1, start + q.Length - 1), cmp);
                if (found > start) found = -1;
            }
            else
            {
                int start = first ? (includeCurrent ? pos : pos + 1) : 0;
                found = start > line.Length ? -1 : line.IndexOf(q, Math.Min(start, line.Length), cmp);
            }
            if (found >= 0)
            {
                if (idx != HistIndex) HistoryGoto(idx);
                st.Index = idx; st.Pos = found;
                Point = found;
                return true;
            }
            first = false;
            idx += backward ? -1 : 1;
        }
        return false;
    }

    bool IsearchKey(IsearchState st, KeyInput key)
    {
        void Finish() { Modal = null; Info = null; MatchRegion = null; }
        switch (key.Name)
        {
            case "C-r":
            case "C-s":
                {
                    bool back = key.Name == "C-r";
                    st.Stack.Push((st.Query, st.Index, st.Pos, st.Failed, st.Backward));
                    st.Backward = back;
                    if (st.Query.Length == 0 && _lastIsearch.Length > 0) st.Query = _lastIsearch;
                    st.Failed = !IsearchFind(st, st.Index, st.Pos, back, includeCurrent: false);
                    UpdateIsearchInfo(st);
                    return true;
                }
            case "DEL":
            case "C-h":
                if (st.Stack.Count == 0) { Bell(); return true; }
                {
                    var (q, idx, pos, failed, back) = st.Stack.Pop();
                    st.Query = q; st.Backward = back; st.Failed = failed;
                    if (idx != HistIndex) HistoryGoto(idx);
                    st.Index = idx; st.Pos = pos; Point = Math.Min(pos, Text.Length);
                    UpdateIsearchInfo(st);
                    return true;
                }
            case "C-g":
                Finish();
                if (HistIndex != st.OrigIndex) HistoryGoto(st.OrigIndex);
                Text = st.OrigText; Point = st.OrigPoint;
                return true;
            case "RET":
                _lastIsearch = st.Query;
                Finish();
                AcceptLine();
                return true;
            case "C-j":
            case "ESC":
                _lastIsearch = st.Query;
                Finish();
                return true;
        }
        if (key.IsPrintable)
        {
            st.Stack.Push((st.Query, st.Index, st.Pos, st.Failed, st.Backward));
            st.Query += key.Char;
            if (!st.Failed) st.Failed = !IsearchFind(st, st.Index, st.Pos, st.Backward, includeCurrent: true);
            UpdateIsearchInfo(st);
            return true;
        }
        // any other key: leave isearch and process the key normally
        _lastIsearch = st.Query;
        Finish();
        return false;
    }

    string _lastIsearch = "";
}
