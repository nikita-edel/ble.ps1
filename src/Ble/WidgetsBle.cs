using System.Management.Automation;
using System.Management.Automation.Language;
using System.Text.RegularExpressions;

namespace Ble;

// Widgets that exist to match ble.sh's default key bindings (src/edit.sh, lib/keymap.*.sh).
public sealed partial class Editor
{
    static void RegisterBleShWidgets()
    {
        // ---- shell words (ble.sh "sword": a word of the command line, quotes included) ----
        W("forward-sword", (e, k) =>
        {
            if (e.Point == e.Text.Length && e.AcceptSuggestion(true, unixWord: true)) return;
            e.Point = e.ShellWordForward(e.Point);
        });
        W("backward-sword", (e, k) => e.Point = e.ShellWordBackward(e.Point));
        W("copy-forward-sword", (e, k) => { int b = e.ShellWordForward(e.Point); e.AddKill(e.Text.Substring(e.Point, b - e.Point)); });
        W("copy-backward-sword", (e, k) => { int a = e.ShellWordBackward(e.Point); e.AddKill(e.Text.Substring(a, e.Point - a), prepend: true); });
        W("delete-forward-cword", (e, k) => e.Delete(e.Point, TextOps.ForwardWord(e.Text, e.Point, TextOps.IsCWord)));
        W("delete-backward-cword", (e, k) => e.Delete(TextOps.BackwardWord(e.Text, e.Point, TextOps.IsCWord), e.Point));
        W("non-space-beginning-of-line", (e, k) => e.Point = TextOps.FirstNonBlank(e.Text, e.LineStart(e.Point)));
        foreach (var m in new[] { "forward-sword", "backward-sword", "non-space-beginning-of-line" })
        {
            var inner = m;
            W("shift-" + m, (e, k) =>
            {
                if (!e.MarkActive || e.MarkKind != MarkKind.Shift) { e.Mark = e.Point; e.MarkActive = true; e.MarkKind = MarkKind.Shift; }
                var s = e._suggest; e._suggest = null;
                Widgets[inner](e, k);
                e._suggest = s;
            });
        }

        // ---- kill ring ----
        W("yank-pop-backward", (e, k) =>
        {
            if (!e._lastWasYank || KillRing.Count == 0) { e.Bell(); return; }
            e._yankIndex = (e._yankIndex - 1 + KillRing.Count) % KillRing.Count;
            var (a, b) = e._yankRange;
            e.Text = e.Text.Remove(a, b - a).Insert(a, KillRing[e._yankIndex]);
            e.Point = a + KillRing[e._yankIndex].Length;
            e._yankRange = (a, e.Point);
            e._yankFlag = true;
        });
        W("zap-to-char", (e, k) =>
        {
            int count = Math.Max(1, e.CountArg());
            e.Info = "(zap-to-char)";
            e.Modal = key =>
            {
                e.Modal = null; e.Info = null;
                if (key.Char == '\0') { e.Bell(); return true; }
                int p = e.Point;
                for (int i = 0; i < count; i++)
                {
                    int q = e.Text.IndexOf(key.Char, Math.Min(e.Text.Length, p + (i == 0 ? 0 : 1)));
                    if (q < 0) { e.Bell(); return true; }
                    p = q;
                }
                var bt = e.Text; var bp = e.Point;
                e.Kill(e.Point, Math.Min(e.Text.Length, p + 1));
                e.PushUndo(bt, bp);
                return true;
            };
        });

        // ---- history ----
        W("history-substring-search-backward", (e, k) => e.HistorySubstringSearch(true));
        W("history-substring-search-forward", (e, k) => e.HistorySubstringSearch(false));
        Widgets["history-nsearch-backward"] = Widgets["history-substring-search-backward"];
        Widgets["history-nsearch-forward"] = Widgets["history-substring-search-forward"];
        Widgets["history-nsearch-backward-again"] = Widgets["history-substring-search-backward"];
        Widgets["history-nsearch-forward-again"] = Widgets["history-substring-search-forward"];
        Widgets["insert-last-argument"] = Widgets["yank-last-arg"];
        W("insert-nth-argument", (e, k) =>
        {
            int n = e.HasArg ? e.CountArg() : 1;
            if (e.History.Count == 0) { e.Bell(); return; }
            var args = Arguments(e.History[e.History.Count - 1]);
            if (n < 0) n = args.Count + n;
            if (n < 0 || n >= args.Count) { e.Bell(); return; }
            e.Insert(args[n]);
        });
        W("accept-and-next", (e, k) =>
        {
            // run the line and offer the following history entry on the next prompt
            int next = e.HistIndex + 1;
            e._acceptAndNext = e.HistIndex < e.History.Count && next < e.History.Count ? e.History[next] : null;
            e.AcceptLine();
        });

        // ---- completion with a forced context (ble.sh: complete context=...) ----
        W("complete-filename", (e, k) => e.CompleteContext("filename"));
        W("complete-variable", (e, k) => e.CompleteContext("variable"));
        W("complete-command", (e, k) => e.CompleteContext("command"));
        W("complete-glob", (e, k) => e.CompleteContext("glob"));
        W("complete-dynamic-history", (e, k) => e.CompleteContext("dynamic-history"));
        W("complete-filename-menu", (e, k) => e.CompleteContext("filename", menuOnly: true));
        W("complete-variable-menu", (e, k) => e.CompleteContext("variable", menuOnly: true));
        W("complete-command-menu", (e, k) => e.CompleteContext("command", menuOnly: true));
        W("complete-glob-menu", (e, k) => e.CompleteContext("glob", menuOnly: true));
        W("complete-glob-insert-all", (e, k) => e.CompleteContext("glob", insertAll: true));
        W("sabbrev-expand", (e, k) => { if (!e.ExpandAbbrev()) e.Bell(); });
        W("dabbrev-expand", (e, k) => e.DabbrevExpand());

        // ---- arguments ----
        W("universal-arg", (e, k) => { e.Arg = e.Arg.Length == 0 ? "4" : e.Arg; e._universal = true; e.Info = "(arg: " + e.Arg + ")"; });
        W("append-arg", (e, k) =>
        {
            // a digit continues a numeric argument in progress; otherwise it is inserted
            if (e.Arg.Length > 0 && e.LastWidget is "digit-argument" or "universal-arg" or "append-arg")
            {
                char d = k.Char != '\0' ? k.Char : k.Name[^1];
                e.Arg = e._universal ? d.ToString() : (d == '-' ? e.Arg : e.Arg + d);
                e._universal = false;
                e.Info = "(arg: " + e.Arg + ")";
                return;
            }
            if (k.Char != '\0') e.Insert(new string(k.Char, Math.Max(1, e.CountArg())));
        });

        // ---- keyboard macros (emacs C-x ( C-x ) C-x e) ----
        W("start-keyboard-macro", (e, k) =>
        {
            if (e._macroKeys != null) { e.Bell(); return; }
            e._macroReg = '\x01';
            e._macroKeys = new List<KeyInput>();
            e.Info = "Defining keyboard macro...";
        });
        W("end-keyboard-macro", (e, k) =>
        {
            if (e._macroKeys == null) { e.Bell(); return; }
            // drop the C-x ) keys themselves
            var keys = e._macroKeys;
            if (keys.Count >= 2) keys.RemoveRange(keys.Count - 2, 2);
            Macros['\x01'] = keys;
            e._macroKeys = null;
            e.Info = "Keyboard macro defined";
        });
        W("call-keyboard-macro", (e, k) =>
        {
            if (!Macros.TryGetValue('\x01', out var keys) || e._macroPlayDepth >= 10) { e.Bell(); return; }
            int n = Math.Max(1, e.CountArg());
            e._macroPlayDepth++;
            try { for (int i = 0; i < n && !e.Done; i++) foreach (var key in keys) e.Dispatch(key); }
            finally { e._macroPlayDepth--; }
        });
        W("print-keyboard-macro", (e, k) =>
        {
            e.Info = Macros.TryGetValue('\x01', out var keys) ? "macro: " + string.Join(' ', keys.Select(x => x.Name)) : "(no keyboard macro)";
        });

        // ---- M-x ----
        W("execute-named-command", (e, k) => e.ReadMiniBuffer("M-x ", name =>
        {
            name = name.Trim();
            if (name.Length == 0) return;
            if (Widgets.ContainsKey(name) || UserWidgets.ContainsKey(name)) e.InvokeWidget(name, default);
            else { e.Info = $"ble: unknown widget '{name}'"; e.InfoFace = "status_error"; }
        }, WidgetNames));

        // ---- expansions ----
        W("tilde-expand", (e, k) =>
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var t = Regex.Replace(e.Text, @"(?<=^|[\s=:;|(])~(?=[\\/]|$|\s)", home.Replace("$", "$$"));
            int delta = t.Length - e.Text.Length;
            e.Text = t; e.Point = Math.Clamp(e.Point + delta, 0, t.Length);
        });
        W("shell-expand-line", (e, k) => e.ShellExpandLine());

        // ---- help & info ----
        W("command-help", (e, k) =>
        {
            var name = e.CommandAtPoint();
            if (name == null) { e.Bell(); return; }
            e.RunAboveLine(() => e.Shell.RunCommand(ScriptBlock.Create("Get-Help -Name '" + name.Replace("'", "''") + "' | Out-Host -Paging")));
        });
        W("show-line-info", (e, k) =>
        {
            int line = e.LineNumber(e.Point) + 1;
            e.Info = $"line {line} of {e.LineCount} --{(e.Text.Length == 0 ? 0 : 100 * (e.Point + 1) / Math.Max(1, e.Text.Length))}%-- col {e.Point - e.LineStart(e.Point) + 1}";
        });

        // ---- vi insert mode (ble.sh vi_imap) ----
        W("vi_imap/kill-backward-word", (e, k) =>
        {
            int a = TextOps.ViPrevWordStart(e.Text, e.Point, false);
            if (a < e.LineStart(e.Point) && e.Point > e.LineStart(e.Point)) a = e.LineStart(e.Point);
            e.Kill(a, e.Point, prepend: true);
        });
        W("vi_imap/delete-backward-char", (e, k) =>
        {
            if (e.MarkActive && e.MarkKind is MarkKind.Shift or MarkKind.Emacs) { var (a, b) = e.RegionBounds(); e.Delete(a, b); e.MarkActive = false; return; }
            if (e.ViReplaceBackspace()) return;
            // delete-backward-indent: only blanks before the cursor -> back to the previous indentation stop
            int ls = e.LineStart(e.Point);
            if (e.Point > ls && e.Text.AsSpan(ls, e.Point - ls).IndexOfAnyExcept(' ') < 0 && !e.HasArg)
            {
                int col = e.Point - ls, width = Math.Max(1, Options.GetInt("indent_offset", 4));
                int target = (col - 1) / width * width;
                e.Delete(ls + target, e.Point);
                return;
            }
            Widgets["delete-backward-char"](e, k);
        });
        W("vi_imap/normal-mode-without-insert-leave", (e, k) => e.ViLeaveInsert());
        W("vi-command/cancel", (e, k) =>
        {
            e.Info = "Type  :q  and press <Enter> to exit PowerShell";
            e.Bell();
        });
    }

    static IEnumerable<string> WidgetNames() => Widgets.Keys.Concat(UserWidgets.Keys);

    bool _universal;
    string? _acceptAndNext;

    /// <summary>Text offered on the next prompt (accept-and-next).</summary>
    public string? TakeNextLineInitial() { var s = _acceptAndNext; _acceptAndNext = null; return s; }

    int ShellWordForward(int p)
    {
        Parser.ParseInput(Text, out var tokens, out _);
        foreach (var t in tokens)
            if (t.Kind is not (TokenKind.EndOfInput or TokenKind.NewLine) && t.Extent.EndOffset > p) return t.Extent.EndOffset;
        return Text.Length;
    }

    int ShellWordBackward(int p)
    {
        Parser.ParseInput(Text, out var tokens, out _);
        int best = 0;
        foreach (var t in tokens)
            if (t.Kind is not (TokenKind.EndOfInput or TokenKind.NewLine) && t.Extent.StartOffset < p) best = t.Extent.StartOffset;
        return best;
    }

    static List<string> Arguments(string line)
    {
        Parser.ParseInput(line, out var tokens, out _);
        return tokens.Where(t => t.Kind is not (TokenKind.EndOfInput or TokenKind.NewLine or TokenKind.Semi or TokenKind.Pipe or TokenKind.Comment))
            .Select(t => t.Text).ToList();
    }

    string? CommandAtPoint()
    {
        var ast = Parser.ParseInput(Text, out _, out _);
        CommandAst? best = null;
        foreach (var ca in ast.FindAll(n => n is CommandAst, true).Cast<CommandAst>())
            if (ca.Extent.StartOffset <= Point && Point <= ca.Extent.EndOffset) best = ca;
        best ??= ast.FindAll(n => n is CommandAst, true).Cast<CommandAst>().LastOrDefault();
        return best?.GetCommandName();
    }

    void HistorySubstringSearch(bool backward)
    {
        if (LastWidget is not ("history-substring-search-backward" or "history-substring-search-forward"
            or "history-nsearch-backward" or "history-nsearch-forward" or "history-nsearch-backward-again" or "history-nsearch-forward-again")
            || _histSearchPrefix == null)
            _histSearchPrefix = Text;
        var q = _histSearchPrefix;
        if (q.Length == 0) { Widgets[backward ? "history-prev" : "history-next"](this, default); return; }
        var cmp = History.SmartCase(q);
        if (backward)
        {
            for (int i = Math.Min(HistIndex, History.Count) - 1; i >= 0; i--)
                if (History[i].Contains(q, cmp) && History[i] != Text) { HistoryGoto(i); MatchRegion = MatchOf(q, cmp); return; }
        }
        else
        {
            for (int i = HistIndex + 1; i < History.Count; i++)
                if (History[i].Contains(q, cmp) && History[i] != Text) { HistoryGoto(i); MatchRegion = MatchOf(q, cmp); return; }
            if (HistIndex < History.Count) { HistoryGoto(History.Count); return; }
        }
        Bell();
    }

    (int, int)? MatchOf(string q, StringComparison cmp)
    {
        int at = Text.IndexOf(q, cmp);
        return at < 0 ? null : (at, at + q.Length);
    }

    /// <summary>Reads a string in the status line, then calls accept. TAB completes from 'candidates'.</summary>
    void ReadMiniBuffer(string prompt, Action<string> accept, Func<IEnumerable<string>>? candidates = null)
    {
        string q = "";
        Info = prompt;
        InfoFace = "status_line";
        Modal = key =>
        {
            switch (key.Name)
            {
                case "RET": case "C-m": case "C-j":
                    Modal = null; Info = null;
                    accept(q);
                    return true;
                case "ESC": case "C-c": case "C-g":
                    Modal = null; Info = null;
                    return true;
                case "DEL": case "C-h":
                    if (q.Length > 0) q = q.Substring(0, q.Length - 1);
                    Info = prompt + q;
                    return true;
                case "TAB":
                    if (candidates != null)
                    {
                        var m = candidates().Where(c => c.StartsWith(q, StringComparison.Ordinal)).OrderBy(c => c, StringComparer.Ordinal).ToList();
                        if (m.Count > 0) q = TextWidth.CommonPrefix(m, false);
                        Info = prompt + q + (m.Count > 1 ? "   {" + string.Join(" ", m.Take(8)) + (m.Count > 8 ? " ..." : "") + "}" : "");
                    }
                    return true;
            }
            if (key.Char != '\0') { q += key.Char; Info = prompt + q; }
            return true;
        };
    }

    void CompleteContext(string context, bool menuOnly = false, bool insertAll = false)
    {
        int ws = Point;
        while (ws > 0 && !char.IsWhiteSpace(Text[ws - 1]) && "|;(){}".IndexOf(Text[ws - 1]) < 0) ws--;
        var word = Text.Substring(ws, Point - ws);
        List<CompletionItem>? items = null;
        int ri = ws, rl = word.Length;
        switch (context)
        {
            case "filename":
            case "command":
            case "variable":
                {
                    var prefix = context switch { "filename" => "Get-Item -LiteralPath ", "variable" => "$", _ => "" };
                    var w = context == "variable" ? word.TrimStart('$') : word;
                    var q = prefix + w;
                    var cs = Shell.Complete(q, q.Length);
                    if (cs != null && cs.Items.Count > 0) items = cs.Items;
                    break;
                }
            case "glob":
                {
                    if (word.Length == 0) break;
                    var pat = word.IndexOfAny(['*', '?', '[']) >= 0 ? word : word + "*";
                    var dir = Path.GetDirectoryName(pat);
                    var baseDir = string.IsNullOrEmpty(dir) ? Shell.CurrentDirectory : Path.Combine(Shell.CurrentDirectory, dir);
                    try
                    {
                        var leaf = Path.GetFileName(pat);
                        items = Directory.EnumerateFileSystemEntries(baseDir, leaf)
                            .Select(p => string.IsNullOrEmpty(dir) ? Path.GetFileName(p) : Path.Combine(dir!, Path.GetFileName(p)))
                            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                            .Select(p => new CompletionItem(QuoteArgument(p), Path.GetFileName(p), p, CompletionResultType.ProviderItem)).ToList();
                    }
                    catch { }
                    break;
                }
            case "dynamic-history":
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    items = new List<CompletionItem>();
                    for (int i = History.Count - 1; i >= 0 && items.Count < 500; i--)
                        foreach (var a in Arguments(History[i]))
                            if (a.Length > word.Length && a.StartsWith(word, StringComparison.Ordinal) && seen.Add(a))
                                items.Add(new CompletionItem(a, a, a, CompletionResultType.History));
                    break;
                }
        }
        if (items == null || items.Count == 0) { Info = "[ble: no completions]"; Bell(); return; }
        if (insertAll) { ReplaceRange(ri, rl, string.Join(' ', items.Select(i => i.CompletionText)) + " "); return; }
        if (items.Count == 1 && !menuOnly)
        {
            ReplaceRange(ri, rl, items[0].CompletionText);
            if (NeedsSpaceAfter(items[0])) Insert(" ");
            return;
        }
        if (!menuOnly)
        {
            var lcp = TextWidth.CommonPrefix(items.Select(i => i.CompletionText).ToList(), ignoreCase: true);
            if (lcp.Length > word.Length) { ReplaceRange(ri, rl, lcp); rl = lcp.Length; }
        }
        Menu = new Menu(items, ri, rl, Text, Point);
    }

    int _dabbrevIndex;
    string? _dabbrevPrefix;
    (int Start, int End) _dabbrevRange;

    /// <summary>ble.sh dabbrev-expand: complete the word from words in history (repeat for older ones).</summary>
    void DabbrevExpand()
    {
        bool again = LastWidget == "dabbrev-expand" && _dabbrevPrefix != null;
        if (!again)
        {
            int ws = Point;
            while (ws > 0 && (char.IsLetterOrDigit(Text[ws - 1]) || "_-.".IndexOf(Text[ws - 1]) >= 0)) ws--;
            _dabbrevPrefix = Text.Substring(ws, Point - ws);
            _dabbrevRange = (ws, Point);
            _dabbrevIndex = 0;
            if (_dabbrevPrefix.Length == 0) { Bell(); _dabbrevPrefix = null; return; }
        }
        var prefix = _dabbrevPrefix!;
        var words = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = History.Count - 1; i >= 0; i--)
            foreach (Match m in Regex.Matches(History[i], @"[\w.\-]+"))
                if (m.Value.Length > prefix.Length && m.Value.StartsWith(prefix, StringComparison.Ordinal) && seen.Add(m.Value)) words.Add(m.Value);
        if (_dabbrevIndex >= words.Count) { Bell(); return; }
        var w = words[_dabbrevIndex++];
        var (a, b) = _dabbrevRange;
        Text = Text.Remove(a, b - a).Insert(a, w);
        Point = a + w.Length;
        _dabbrevRange = (a, Point);
    }

    /// <summary>ble.sh shell-expand-line: expand aliases in command position and simple variables.</summary>
    void ShellExpandLine()
    {
        var ast = Parser.ParseInput(Text, out var tokens, out _);
        var edits = new List<(int Start, int End, string With)>();
        foreach (var ca in ast.FindAll(n => n is CommandAst, true).Cast<CommandAst>())
        {
            if (ca.CommandElements.Count == 0 || ca.CommandElements[0] is not StringConstantExpressionAst name) continue;
            var target = Shell.Evaluate(ScriptBlock.Create("(Get-Alias -Name '" + name.Value.Replace("'", "''") + "' -ErrorAction Ignore).Definition"));
            if (target.Length > 0) edits.Add((name.Extent.StartOffset, name.Extent.EndOffset, target));
        }
        foreach (var t in tokens.OfType<VariableToken>())
        {
            if (t.Kind != TokenKind.Variable || t.VariablePath.IsDriveQualified && !string.Equals(t.VariablePath.DriveName, "env", StringComparison.OrdinalIgnoreCase)) continue;
            var value = Shell.Evaluate(ScriptBlock.Create("$" + t.VariablePath.UserPath));
            if (value.Length > 0 && !value.Contains('\n')) edits.Add((t.Extent.StartOffset, t.Extent.EndOffset, value.Contains(' ') ? QuoteArgument(value) : value));
        }
        foreach (var (s, e, w) in edits.OrderByDescending(x => x.Start))
        {
            Text = Text.Substring(0, s) + w + Text.Substring(e);
            if (Point > s) Point = Math.Max(s, Point + w.Length - (e - s));
        }
        Point = Math.Clamp(Point, 0, Text.Length);
    }
}
