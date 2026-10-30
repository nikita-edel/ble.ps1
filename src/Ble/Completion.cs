using System.Management.Automation;

namespace Ble;

/// <summary>Completion menu shown below the command line.</summary>
public sealed class Menu
{
    public List<CompletionItem> All;
    public List<CompletionItem> Items;
    public int Selected = -1;
    public int ReplacementIndex;
    public int ReplacementLength;
    public string Orig;           // text the replacement range refers to
    public int OrigPoint;
    public bool Selecting => Selected >= 0;
    public int Columns { get; private set; } = 1;
    public int VisibleRows { get; private set; } = 1;
    int _scroll;

    public Menu(List<CompletionItem> items, int ri, int rl, string orig, int origPoint)
    {
        All = items; Items = items;
        ReplacementIndex = ri; ReplacementLength = rl; Orig = orig; OrigPoint = origPoint;
    }

    public string Word => Orig.Substring(ReplacementIndex, Math.Min(ReplacementLength, Orig.Length - ReplacementIndex));

    static string FaceOf(CompletionResultType t) => t switch
    {
        CompletionResultType.ProviderContainer => "filename_directory",
        CompletionResultType.ProviderItem => "filename_other",
        CompletionResultType.Command => "command_builtin",
        CompletionResultType.ParameterName => "argument_option",
        CompletionResultType.Variable => "syntax_varname",
        CompletionResultType.Keyword => "command_keyword",
        CompletionResultType.Type => "syntax_type",
        CompletionResultType.Namespace => "syntax_type",
        CompletionResultType.Method or CompletionResultType.Property => "syntax_member",
        _ => "",
    };

    public List<PanelLine> Layout(int width, int maxLines)
    {
        var lines = new List<PanelLine>();
        int n = Items.Count;
        if (n == 0) return lines;
        var style = Options.Get("complete_menu_style");
        int avail = Math.Max(10, width - 1);
        var word = Word;
        string selSgr = Faces.Sgr("menu_complete_selected");
        string matchSgr = Options.Get("complete_menu_color_match") is "" or "off" or "0" ? "" : Faces.Sgr("menu_complete_match");

        if (style is "linewise" or "desc" or "desc-text")   // ble.sh: align(-nowrap), dense(-nowrap) use the grid
        {
            Columns = 1;
            int nameW = Math.Min(Items.Max(i => TextWidth.StringWidth(i.ListItemText)), Math.Max(8, avail / 2));
            int rows = n;
            bool needInfo = rows > maxLines;
            VisibleRows = Math.Max(1, needInfo ? maxLines - 1 : rows);
            Scroll(rows);
            for (int r = _scroll; r < Math.Min(rows, _scroll + VisibleRows); r++)
            {
                var it = Items[r];
                var pl = new PanelLine();
                var name = TextWidth.Truncate(it.ListItemText, nameW);
                AddItem(pl, name, it, r == Selected, word, selSgr, matchSgr);
                if (style == "desc" && it.ToolTip.Length > 0 && it.ToolTip.Trim() != it.ListItemText)
                {
                    pl.Add(new string(' ', nameW - TextWidth.StringWidth(name) + 2));
                    var desc = it.ToolTip.Replace("\r", "").Split('\n')[0].Trim();
                    pl.Add(TextWidth.Truncate(desc, Math.Max(0, avail - nameW - 2)), Faces.Sgr("menu_desc"));
                }
                lines.Add(pl);
            }
            if (needInfo) lines.Add(InfoLine(rows));
            return lines;
        }

        int maxw = Items.Max(i => TextWidth.StringWidth(i.ListItemText));
        int cellMax = Math.Max(4, Options.GetInt("menu_align_max", 32));
        int cell = Math.Max(Math.Min(maxw, cellMax), Math.Max(1, Options.GetInt("menu_align_min", 4))) + 2;
        Columns = Math.Max(1, avail / cell);
        if (Columns == 1) cell = Math.Min(cell, avail);
        int totalRows = (n + Columns - 1) / Columns;
        bool info = totalRows > maxLines;
        VisibleRows = Math.Max(1, info ? maxLines - 1 : totalRows);
        Scroll(totalRows);
        for (int r = _scroll; r < Math.Min(totalRows, _scroll + VisibleRows); r++)
        {
            var pl = new PanelLine();
            for (int c = 0; c < Columns; c++)
            {
                int i = r * Columns + c;
                if (i >= n) break;
                var it = Items[i];
                var name = TextWidth.Truncate(it.ListItemText, cell - 2);
                AddItem(pl, name, it, i == Selected, word, selSgr, matchSgr);
                if (c < Columns - 1 && i + 1 < n) pl.Add(new string(' ', cell - TextWidth.StringWidth(name)));
            }
            lines.Add(pl);
        }
        if (info) lines.Add(InfoLine(totalRows));
        return lines;
    }

    static void AddItem(PanelLine pl, string name, CompletionItem it, bool selected, string word, string selSgr, string matchSgr)
    {
        var face = Options.Get("complete_menu_color") is "" or "off" or "0" ? "" : Faces.Sgr(FaceOf(it.Type));
        if (selected) { pl.Add(name, face + selSgr); return; }
        var bare = word.TrimStart('\'', '"');
        int slash = Math.Max(bare.LastIndexOf('\\'), bare.LastIndexOf('/'));
        if (slash >= 0) bare = bare.Substring(slash + 1);
        if (bare.Length > 0 && name.StartsWith(bare, StringComparison.OrdinalIgnoreCase))
        {
            pl.Add(name.Substring(0, bare.Length), face + matchSgr);
            pl.Add(name.Substring(bare.Length), face);
        }
        else pl.Add(name, face);
    }

    PanelLine InfoLine(int totalRows)
    {
        int first = _scroll + 1, last = Math.Min(totalRows, _scroll + VisibleRows);
        var sel = Selected >= 0 ? $"{Selected + 1}/{Items.Count}" : $"{Items.Count} items";
        return PanelLine.Of($"({sel}, rows {first}-{last} of {totalRows})", "menu_info");
    }

    void Scroll(int totalRows)
    {
        if (Selected >= 0)
        {
            int row = Selected / Math.Max(1, Columns);
            if (row < _scroll) _scroll = row;
            if (row >= _scroll + VisibleRows) _scroll = row - VisibleRows + 1;
        }
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, totalRows - VisibleRows));
    }

    public void Move(int delta, bool wrap = true)
    {
        int n = Items.Count;
        if (n == 0) return;
        if (Selected < 0) { Selected = delta >= 0 ? 0 : n - 1; return; }
        int s = Selected + delta;
        if (wrap) s = ((s % n) + n) % n;
        else s = Math.Clamp(s, 0, n - 1);
        Selected = s;
    }

    public void MoveRow(int rows)
    {
        int n = Items.Count;
        if (n == 0) return;
        if (Selected < 0) { Selected = 0; return; }
        int cols = Math.Max(1, Columns);
        int s = Selected + rows * cols;
        if (s >= n)
        {
            // wrap to the top of the next column
            int col = Selected % cols;
            s = rows == 1 ? (col + 1) % cols : n - 1;
            if (s >= n) s = 0;
        }
        else if (s < 0)
        {
            int col = Selected % cols;
            int lastRow = (n - 1) / cols;
            int prevCol = (col - 1 + cols) % cols;
            s = rows == -1 ? Math.Min(n - 1, lastRow * cols + prevCol) : 0;
            if (s >= n) s -= cols;
        }
        Selected = Math.Clamp(s, 0, n - 1);
    }
}

public sealed partial class Editor
{
    public Menu? Menu;

    // auto-suggestion state
    internal string? _suggest;            // suggested text shown after the cursor
    string? _suggestFor;                  // buffer text the suggestion was computed for
    (int Start, string Word)? _suggestReplace;   // replace this word on accept (completion with different case)
    // ble.sh "ambiguous" auto-complete (types r/m/a/A): the word does not start the candidate, so the
    // candidate is shown as " [cand] " and accepting replaces the word from Start with it.
    (int Start, string Insert)? _suggestAmbig;
    bool _completionSuggestPending;
    string? _suggestDismissed;            // C-g hides the suggestion until the text changes

    void ClearSuggestion()
    {
        _suggest = null; _suggestReplace = null; _suggestAmbig = null; _completionSuggestPending = false;
    }

    /// <summary>Hides the current suggestion (or list view). Returns false if none was shown.</summary>
    public bool DismissSuggestion()
    {
        if (DismissListView()) return true;
        if (_suggest == null || Point != Text.Length) return false;
        ClearSuggestion();
        _suggestDismissed = Text; _suggestFor = Text;
        return true;
    }
    bool _autoMenuPending;

    static void RegisterCompletionWidgets()
    {
        W("complete", (e, k) => e.Complete(CompleteMode.Normal));
        W("menu-complete", (e, k) => e.Complete(CompleteMode.MenuForward));
        W("menu-complete-backward", (e, k) => e.Complete(CompleteMode.MenuBackward));
        W("possible-completions", (e, k) => e.Complete(CompleteMode.ShowOnly));
        W("insert-completions", (e, k) => e.Complete(CompleteMode.InsertAll));
        RegisterListViewWidgets();
    }

    enum CompleteMode { Normal, MenuForward, MenuBackward, ShowOnly, InsertAll }

    void Complete(CompleteMode mode)
    {
        if (Menu != null && !Menu.Selecting && mode != CompleteMode.ShowOnly && mode != CompleteMode.InsertAll && Options.GetBool("complete_menu_complete")
            && Text == Menu.Orig && Menu.Items.Count > 0)
        {
            StartMenuSelection(mode == CompleteMode.MenuBackward ? -1 : 1);
            return;
        }
        if (mode is CompleteMode.Normal or CompleteMode.MenuForward && TryFzfCompletion()) return;
        var cs = CompleteAt(Text, Point);
        bool ambiguous = false;
        if ((cs == null || cs.Items.Count == 0) && Options.GetBool("complete_ambiguous") && mode != CompleteMode.InsertAll)
        {
            cs = CompleteAmbiguous();
            ambiguous = cs != null;
        }
        if (cs == null || cs.Items.Count == 0)
        {
            Info = "[ble: no completions]";
            Bell();
            return;
        }
        int ri = Math.Clamp(cs.ReplacementIndex, 0, Text.Length);
        int rl = Math.Clamp(cs.ReplacementLength, 0, Text.Length - ri);
        var word = Text.Substring(ri, rl);
        var items = cs.Items;

        if (mode == CompleteMode.InsertAll)
        {
            var all = string.Join(' ', items.Select(i => i.CompletionText)) + " ";
            ReplaceRange(ri, rl, all);
            return;
        }
        if (items.Count == 1 && mode != CompleteMode.ShowOnly)
        {
            var it = items[0];
            var ins = it.CompletionText;
            ReplaceRange(ri, rl, ins);
            if (NeedsSpaceAfter(it) && (Point >= Text.Length || Text[Point] != ' ')) Insert(" ");
            return;
        }
        if (mode == CompleteMode.Normal && !ambiguous)
        {
            var lcp = TextWidth.CommonPrefix(items.Select(i => i.CompletionText).ToList(), ignoreCase: true);
            // also replaces a same-length prefix to fix its case (get-c -> Get-C)
            if (lcp.Length >= word.Length && !lcp.Equals(word, StringComparison.Ordinal))
            {
                ReplaceRange(ri, rl, lcp);
                rl = lcp.Length;
            }
        }
        Menu = new Menu(items, ri, rl, Text, Point);
        if (mode is CompleteMode.MenuForward or CompleteMode.MenuBackward)
            StartMenuSelection(mode == CompleteMode.MenuBackward ? -1 : 1);
    }

    static bool NeedsSpaceAfter(CompletionItem it)
    {
        var t = it.CompletionText;
        if (t.Length == 0) return false;
        if (it.Type is CompletionResultType.ProviderContainer) return false;
        char last = t[^1];
        if (last is '\\' or '/' or '(' or '.' or ':' or '=' or '[' or '{') return false;
        if (it.Type is CompletionResultType.Method or CompletionResultType.Property or CompletionResultType.Type or CompletionResultType.Namespace) return false;
        return true;
    }

    void ReplaceRange(int ri, int rl, string s)
    {
        Text = Text.Substring(0, ri) + s + Text.Substring(ri + rl);
        Point = ri + s.Length;
    }

    void StartMenuSelection(int dir)
    {
        var menu = Menu!;
        if (menu.Items.Count == 0) { CloseMenu(); return; }
        menu.Selected = -1;
        menu.Move(dir);
        ApplyMenuSelection();
        Modal = MenuKey;
    }

    void ApplyMenuSelection()
    {
        var m = Menu!;
        if (m.Selected < 0) return;
        var c = m.Items[m.Selected].CompletionText;
        int ri = m.ReplacementIndex;
        int tailStart = Math.Min(m.Orig.Length, ri + m.ReplacementLength);
        Text = m.Orig.Substring(0, ri) + c + m.Orig.Substring(tailStart);
        Point = ri + c.Length;
        InsertRegion = (ri, ri + c.Length);
    }

    public void CloseMenu()
    {
        Menu = null;
        InsertRegion = null;
        if (Modal == (Func<KeyInput, bool>)MenuKey) Modal = null;
    }

    bool MenuKey(KeyInput k)
    {
        var m = Menu;
        if (m == null) { Modal = null; return false; }
        switch (k.Name)
        {
            case "TAB": case "C-i": m.Move(1); break;
            case "S-TAB": m.Move(-1); break;
            case "down": case "C-n": m.MoveRow(1); break;
            case "up": case "C-p": m.MoveRow(-1); break;
            case "right": case "C-f": m.Move(1); break;
            case "left": case "C-b": m.Move(-1); break;
            case "next": m.Move(m.Columns * Math.Max(1, m.VisibleRows), wrap: false); break;
            case "prior": m.Move(-m.Columns * Math.Max(1, m.VisibleRows), wrap: false); break;
            case "RET":
            case "C-m":
                {
                    var before = m.Orig; var bp = m.OrigPoint;
                    var it = m.Items[m.Selected];
                    Modal = null; Menu = null; InsertRegion = null;
                    PushUndo(before, bp);
                    if (NeedsSpaceAfter(it) && (Point >= Text.Length || Text[Point] != ' ') && m.Items.Count == 1) Insert(" ");
                    return true;
                }
            case "C-g":
            case "ESC":
                Text = m.Orig; Point = m.OrigPoint;
                Modal = null; Menu = null; InsertRegion = null;
                return true;
            default:
                {
                    // accept the current candidate and process the key normally
                    var before = m.Orig; var bp = m.OrigPoint;
                    Modal = null; Menu = null; InsertRegion = null;
                    PushUndo(before, bp);
                    LastWidget = "menu-accept";
                    return false;
                }
        }
        ApplyMenuSelection();
        return true;
    }

    void RefilterMenu()
    {
        var m = Menu!;
        int ri = m.ReplacementIndex;
        string tail = m.Orig.Substring(Math.Min(m.Orig.Length, ri + m.ReplacementLength));
        if (Point < ri || Text.Length < ri || string.CompareOrdinal(Text, 0, m.Orig, 0, ri) != 0
            || !Text.EndsWith(tail, StringComparison.Ordinal) || Text.Length - tail.Length < Point)
        {
            CloseMenu();
            return;
        }
        var word = Text.Substring(ri, Point - ri);
        var w0 = word.TrimStart('\'', '"');
        var filtered = m.All.Where(c => c.CompletionText.TrimStart('\'', '"', '&', ' ').StartsWith(w0, StringComparison.OrdinalIgnoreCase)
                                     || c.ListItemText.StartsWith(w0, StringComparison.OrdinalIgnoreCase)).ToList();
        if (filtered.Count == 0 && Options.GetBool("complete_ambiguous") && w0.Length > 0)
        {
            // ble.sh complete_ambiguous: substring, then subsequence matches
            filtered = m.All.Where(c => c.ListItemText.Contains(w0, StringComparison.OrdinalIgnoreCase)).ToList();
            if (filtered.Count == 0) filtered = m.All.Where(c => IsSubsequence(w0, c.ListItemText)).ToList();
        }
        if (filtered.Count == 0) { CloseMenu(); return; }
        m.Items = filtered;
        m.ReplacementLength = Point - ri;
        m.Orig = Text;
        m.OrigPoint = Point;
        m.Selected = -1;
    }

    static bool IsSubsequence(string needle, string hay)
    {
        int j = 0;
        foreach (var ch in hay)
            if (j < needle.Length && char.ToUpperInvariant(ch) == char.ToUpperInvariant(needle[j])) j++;
        return j == needle.Length;
    }

    /// <summary>
    /// ble.sh complete_ambiguous: when nothing completes the word as a prefix, retry as a
    /// substring (*word*) and then as a subsequence (*w*o*r*d*), using PowerShell wildcards.
    /// </summary>
    CompletionSet? CompleteAmbiguous(bool subsequence = true)
    {
        int ws = Point;
        while (ws > 0 && !char.IsWhiteSpace(Text[ws - 1]) && "|;(){}=,&".IndexOf(Text[ws - 1]) < 0) ws--;
        var word = Text.Substring(ws, Point - ws);
        if (word.Length == 0 || word.IndexOfAny(['*', '?', '[', ']', '`', '\'', '"']) >= 0) return null;
        int slash = Math.Max(word.LastIndexOf('\\'), word.LastIndexOf('/'));
        var dir = word.Substring(0, slash + 1);
        var leaf = word.Substring(slash + 1);
        var lead = leaf.StartsWith('-') || leaf.StartsWith('$') ? leaf.Substring(0, 1) : "";
        var core = leaf.Substring(lead.Length);
        if (core.Length == 0) return null;
        foreach (var pattern in subsequence ? new[] { lead + "*" + core + "*", lead + "*" + string.Join('*', core.ToCharArray()) + "*" } : new[] { lead + "*" + core + "*" })
        {
            var t2 = Text.Substring(0, ws) + dir + pattern + Text.Substring(Point);
            int cur2 = ws + dir.Length + pattern.Length;
            var c2 = CompleteAt(t2, cur2);
            if (c2 == null || c2.Items.Count == 0) continue;
            int ri = c2.ReplacementIndex;
            if (ri < 0 || ri > ws + dir.Length || ri + c2.ReplacementLength != cur2) continue;
            return new CompletionSet(ri, Point - ri, c2.Items);
        }
        return null;
    }

    // ---- auto-suggestions ----

    public string? Suggestion => _suggest;

    static bool HasAutoOpt(string opt) => (":" + Options.Get("complete_auto_complete_opts") + ":").Contains(":" + opt + ":", StringComparison.Ordinal);

    public void UpdateSuggestion()
    {
        UpdateListView();
        if (!Options.GetBool("complete_auto_complete") || Text.Length == 0 || Point != Text.Length || Modal != null
            || Keymap is "vi_nmap" or "vi_xmap" || Menu?.Selecting == true || MarkActive
            || (ListViewEnabled && (_listSel >= 0 || !Options.GetBool("complete_auto_view_ambiguous"))))
        {
            ClearSuggestion(); _suggestFor = null;
            ScheduleAutoMenu();
            return;
        }
        if (_suggestFor == Text) return;
        if (_suggestDismissed == Text) { _suggest = null; _suggestFor = Text; return; }
        _suggestDismissed = null;
        // keep a suggestion the user is typing along
        if (_suggest != null && _suggestFor != null && _suggestReplace == null && _suggestAmbig == null && Text.StartsWith(_suggestFor, StringComparison.Ordinal))
        {
            var full = _suggestFor + _suggest;
            if (full.Length > Text.Length && full.StartsWith(Text, StringComparison.Ordinal))
            {
                _suggest = full.Substring(Text.Length);
                _suggestFor = Text;
                return;
            }
        }
        ClearSuggestion(); _suggestFor = Text;
        // with the list view only the " [correction] " form is shown inline (complete_auto_view_ambiguous)
        if (!ListViewEnabled && Options.GetBool("complete_auto_history") && !HasAutoOpt("history-disabled"))
        {
            var h = NewHistoryRanking(HistoryRanking.InlineChecks).Best(History.PrefixMatches(Text));
            if (h != null) _suggest = h.Substring(Text.Length);
        }
        // idle task: TabExpansion2, then similar history entries (complete_auto_history_fuzzy)
        if (_suggest == null && (CompletionSuggestWanted || (!ListViewEnabled && Options.GetBool("complete_auto_history_fuzzy"))))
            _completionSuggestPending = true;
        ScheduleAutoMenu();
    }

    bool CompletionSuggestWanted => Options.GetBool("complete_auto_completion") && Text.Length > 0 && !char.IsWhiteSpace(Text[^1]);

    // HistoryRanking with the options: complete_auto_history_cwd, complete_auto_history_validate (or the one
    // named by validateOption), complete_auto_history_merge
    HistoryRanking NewHistoryRanking(int maxChecks, string validateOption = "complete_auto_history_validate")
    {
        var dir = Options.GetBool("complete_auto_history_cwd") ? Shell.CurrentDirectory : null;
        var v = Options.Get(validateOption).Split(':');
        bool commands = v.Contains("command"), paths = v.Contains("path");
        Func<string, bool>? valid = commands || paths ? line => Highlighter.IsValid(line, commands, paths, History.DirsOf(line)) : null;
        return new HistoryRanking(History, dir, valid, maxChecks, HistoryKey());
    }

    // relative paths count from the current directory; plain words are paths if they exist there
    Func<string, string>? HistoryKey()
    {
        var m = Options.Get("complete_auto_history_merge").Split(':');
        bool paths = m.Contains("path"), ignoreCase = m.Contains("case");
        if (!paths && !ignoreCase) return null;
        var cwd = Shell.CurrentDirectory;
        if (_pathExistsDir != cwd) { _pathExists.Clear(); _pathExistsDir = cwd; }
        return line => HistoryRanking.Key(line, paths, ignoreCase, cwd, PathExists);
    }

    // file system checks for HistoryKey, kept while the line is edited in the same directory
    readonly Dictionary<string, bool> _pathExists = new(StringComparer.Ordinal);
    string? _pathExistsDir;

    bool PathExists(string p)
    {
        if (!_pathExists.TryGetValue(p, out var e))
        {
            try { e = Directory.Exists(p) || File.Exists(p); } catch { e = false; }
            _pathExists[p] = e;
        }
        return e;
    }

    // complete_auto_history_fuzzy: similar history entries for a query nothing else matched
    List<string> SimilarHistory(string query, int max, HashSet<string>? seen = null)
    {
        if (!Options.GetBool("complete_auto_history_fuzzy") || query.Contains('\n')) return new List<string>();
        var rank = NewHistoryRanking(HistoryRanking.ListChecks, "complete_auto_history_fuzzy_validate");
        seen ??= new HashSet<string>();
        if (rank.KeyOf(query) is { } qk) seen.Add(qk);
        return rank.Top(HistoryRanking.Similar(History, query), max, seen);
    }

    // after the completion-based suggestion found nothing
    bool ComputeSimilarSuggestion()
    {
        if (_suggestFor != Text || _suggest != null || Point != Text.Length || Modal != null || ListViewEnabled) return false;
        var h = SimilarHistory(Text, 1);
        return h.Count > 0 && SetAmbiguousSuggestion(0, h[0]);
    }

    void ScheduleAutoMenu()
    {
        _autoMenuPending = Options.Get("complete_auto_menu").Length > 0 && Menu == null && Text.Length > 0
            && Point == Text.Length && !char.IsWhiteSpace(Text[^1]) && Modal == null && Keymap is "emacs" or "vi_imap";
    }

    // Completion results for the current line. TAB, the inline suggestion and the list view ask for
    // the same text, and TabExpansion2 is the most expensive call in the editor.
    readonly Dictionary<(string Text, int Cursor), CompletionSet?> _completeCache = new();
    bool _idleTask;   // completions started by idle tasks stop as soon as a key is pressed

    CompletionSet? CompleteAt(string text, int cursor)
    {
        if (_completeCache.TryGetValue((text, cursor), out var cached)) return cached;
        bool stopped = false;
        var r = Shell.Complete(text, cursor, _idleTask ? () => stopped = stopped || Term.KeyAvailable : null);
        if (stopped) return null;   // incomplete: not cached
        if (_completeCache.Count >= 64) _completeCache.Clear();
        _completeCache[(text, cursor)] = r;
        return r;
    }

    internal void RunIdle()
    {
        long idle = Term.Now - _lastKeyTime;
        int delay = Options.GetInt("complete_auto_delay", 100);
        _idleTask = true;
        try
        {
            if (_completionSuggestPending && idle >= delay)
            {
                _completionSuggestPending = false;
                if ((CompletionSuggestWanted && ComputeCompletionSuggestion()) || ComputeSimilarSuggestion()) SafeRender();
            }
            if (_listCompletionPending && idle >= delay && ComputeListCompletions()) SafeRender();
            if (_autoMenuPending && idle >= Options.GetInt("complete_auto_menu", 0))
            {
                _autoMenuPending = false;
                var cs = CompleteAt(Text, Point);
                if (cs != null && cs.Items.Count > 1 && Point == Text.Length)
                {
                    int ri = Math.Clamp(cs.ReplacementIndex, 0, Text.Length);
                    int rl = Math.Clamp(cs.ReplacementLength, 0, Text.Length - ri);
                    if (rl > 0) { Menu = new Menu(cs.Items, ri, rl, Text, Point); SafeRender(); }
                }
            }
            // once per session, after a pause: build the command indexes and do the slow first completion
            if (!_warm && idle >= 500 && !_completionSuggestPending && !_listCompletionPending && !InputPending()) _warm = WarmUp();
        }
        finally { _idleTask = false; }
    }

    bool _warm;

    // also runs the code every key runs once: the first key would otherwise wait ~100 ms for .NET
    // to load and compile the highlighter, the history ranking and the parser
    bool WarmUp()
    {
        bool done = Shell.Warmup();
        try
        {
            new Highlighter(Shell).Highlight("Get-ChildItem -Path ./x | Where-Object { $_.Name -like \"a*\" } # x");
            NewHistoryRanking(HistoryRanking.InlineChecks).Top(History.PrefixMatches("g"), 1);
        }
        catch { }
        return done;
    }

    internal static bool IsSep(char c) => c is '\\' or '/';

    /// <summary>Case-insensitive prefix test that treats / and \ as the same character.</summary>
    static bool PathPrefix(string s, string prefix, bool ignoreCase)
    {
        if (prefix.Length > s.Length) return false;
        for (int i = 0; i < prefix.Length; i++)
        {
            char a = s[i], b = prefix[i];
            if (a == b || (IsSep(a) && IsSep(b))) continue;
            if (ignoreCase && char.ToUpperInvariant(a) == char.ToUpperInvariant(b)) continue;
            return false;
        }
        return true;
    }

    // keep the separator the user typed: ".local/b" completes to ".local/bin", not ".local\bin"
    // and drop the .\ that PowerShell puts before relative paths the user typed without it
    string UserSeparators(string word, string completion)
    {
        if (completion.Length > 2 && completion[0] == '.' && IsSep(completion[1]) && !(word.Length > 1 && word[0] == '.' && IsSep(word[1])))
            completion = completion.Substring(2);
        completion = word.IndexOf('/') >= 0 && word.IndexOf('\\') < 0 ? completion.Replace('\\', '/') : completion;
        return VariableForm(word, TildeForm(word, completion));
    }

    // PowerShell completes "~/d" to "C:\Users\me\dev": keep the ~ the user typed ("~/dev")
    static string TildeForm(string word, string completion)
    {
        if (!(word == "~" || (word.Length > 1 && word[0] == '~' && IsSep(word[1])))) return completion;
        return KeepHead(completion, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~");
    }

    // a variable at the start of a path: $name, ${name}, $env:NAME
    static readonly System.Text.RegularExpressions.Regex VariableHead =
        new(@"^\$(?:\{(?<n>[^}]+)\}|(?<n>(?:\w+:)?\w+))(?=[\\/]|$)", System.Text.RegularExpressions.RegexOptions.Compiled);

    // PowerShell completes "$ps_config\a" to "C:\...\Config\alias.ps1": keep the variable the user typed
    string VariableForm(string word, string completion)
    {
        var m = VariableHead.Match(word);
        if (!m.Success || Shell.GetVariableText(m.Groups["n"].Value) is not { Length: > 0 } value) return completion;
        var kept = KeepHead(completion, value, m.Value);
        // a variable in single quotes is not expanded: use double quotes, unless the path needs single ones
        if (kept != completion && kept[0] == '\'')
            kept = kept.IndexOfAny(['"', '`', '$'], 1 + m.Length) < 0 ? "\"" + kept[1..^1] + "\"" : completion;
        return kept;
    }

    // replaces the directory 'dir' at the start of a completion (maybe quoted) with 'head'
    static string KeepHead(string completion, string dir, string head)
    {
        dir = dir.TrimEnd('\\', '/');
        string quote = completion.Length > 0 && completion[0] is '\'' or '"' ? completion.Substring(0, 1) : "";
        var path = completion.Substring(quote.Length);
        if (dir.Length == 0 || !PathPrefix(path, dir, ignoreCase: true) || (path.Length > dir.Length && !IsSep(path[dir.Length]))) return completion;
        return quote + head + path.Substring(dir.Length);
    }

    bool ComputeCompletionSuggestion()
    {
        if (_suggestFor != Text || _suggest != null || Point != Text.Length || Modal != null) return false;
        var cs = CompleteAt(Text, Point);
        if (cs != null && cs.Items.Count > 0)
        {
            int ri = cs.ReplacementIndex, rl = cs.ReplacementLength;
            if (ri < 0 || rl <= 0 || ri + rl != Text.Length) return false;
            var word = Text.Substring(ri, rl);
            var c = UserSeparators(word, cs.Items[0].CompletionText);
            if (PathPrefix(c, word, ignoreCase: true))
            {
                if (c.Length <= word.Length || ListViewEnabled) return false;   // the list shows these
                _suggest = c.Substring(word.Length);
                _suggestReplace = PathPrefix(c, word, ignoreCase: false) ? null : (ri, c);
                return true;
            }
            // the completion rewrites the word (ble.sh type r)
            return SetAmbiguousSuggestion(ri, c);
        }
        if (!Options.GetBool("complete_ambiguous")) return false;
        var amb = CompleteAmbiguous();
        if (amb == null || amb.Items.Count == 0 || amb.ReplacementIndex + amb.ReplacementLength != Text.Length) return false;
        var w0 = Text.Substring(amb.ReplacementIndex, amb.ReplacementLength);
        return SetAmbiguousSuggestion(amb.ReplacementIndex, UserSeparators(w0, amb.Items[0].CompletionText));
    }

    bool SetAmbiguousSuggestion(int start, string insert)
    {
        if (insert.Length == 0 || insert == Text.Substring(start)) return false;
        _suggest = " [" + insert + "] ";
        _suggestAmbig = (start, insert);
        return true;
    }

    /// <summary>Accepts the shown suggestion (or its next word). Returns false if there was none.</summary>
    public bool AcceptSuggestion(bool wordOnly, bool unixWord = false)
    {
        if (_suggest == null || Point != Text.Length || _suggestFor != Text) return false;
        if (_suggestAmbig is var (astart, insert))
        {
            // ble.sh auto_complete/.insert-prefix: "hll [hello world]" + M-f -> "hello " with "world" suggested
            string prefix = insert;
            if (wordOnly)
            {
                int end = TextOps.ForwardWord(insert, 0, unixWord ? TextOps.IsUWord : TextOps.IsCWord);
                prefix = insert.Substring(0, Math.Max(1, end));
            }
            Text = Text.Substring(0, astart) + prefix;
            Point = Text.Length;
            _suggestAmbig = null; _suggestReplace = null;
            _suggest = prefix.Length < insert.Length ? insert.Substring(prefix.Length) : null;
            _suggestFor = _suggest != null ? Text : null;
            return true;
        }
        if (_suggestReplace is var (start, word) && !wordOnly)
        {
            Text = Text.Substring(0, start) + word;
            Point = Text.Length;
            ClearSuggestion(); _suggestFor = null;
            return true;
        }
        string part = _suggest;
        if (wordOnly)
        {
            var full = Text + _suggest;
            int end = TextOps.ForwardWord(full, Text.Length, unixWord ? TextOps.IsUWord : TextOps.IsCWord);
            part = full.Substring(Text.Length, end - Text.Length);
        }
        if (part.Length == 0) return false;
        if (_suggestReplace is var (s2, w2))
        {
            // keep the casing of the completion for the accepted part
            Text = Text.Substring(0, s2) + w2.Substring(0, Text.Length - s2) + part;
        }
        else Text += part;
        Point = Text.Length;
        var rest = _suggest.Substring(part.Length);
        _suggest = rest.Length > 0 ? rest : null;
        _suggestFor = _suggest != null ? Text : null;
        if (_suggest == null) _suggestReplace = null;
        return true;
    }
}
