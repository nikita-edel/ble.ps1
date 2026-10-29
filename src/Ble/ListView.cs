using System.Management.Automation;

namespace Ble;

/// <summary>
/// List view of suggestions (ble.ps1; like PSReadLine's PredictionViewStyle ListView, not part of ble.sh).
/// With bleopt complete_auto_view=list, entries matching the input are listed below the command
/// line instead of the inline suggestion:
///   history      history entries (prefix matches first, then substring matches)
///   completion   TabExpansion2 results for the word at the cursor (files, parameters ...), computed
///                after complete_auto_delay
/// up/down (and keys bound to history widgets) select an entry and the line shows it; moving past
/// either end returns to the typed text. S-RET takes the selected (or first) entry and keeps
/// listing for the new text, C-g restores the typed text and hides the list, typing continues
/// from the shown entry, and f2 switches between inline and list.
/// In multi-line input the current line is the query (complete_auto_view_multiline).
/// </summary>
public sealed partial class Editor
{
    // Line replaces the query; Display (if set) is shown instead of it, with Match highlighted in it
    public sealed record ListEntry(string Line, string Source, string? Display = null, string? Match = null);

    List<ListEntry>? _listItems;
    List<ListEntry> _listHistory = new();
    int _listSel = -1;
    int _listBase;               // the query is Text[_listBase..]; entries replace it
    string? _listQuery;          // the typed text the list was computed for
    string? _listFor;            // the whole Text the list was computed for
    string? _listDismissed;
    bool _listCompletionPending;

    public bool ListViewActive => _listItems is { Count: > 0 };
    public IReadOnlyList<string>? ListViewItems => ListViewActive ? _listItems!.Select(x => x.Line).ToList() : null;
    public IReadOnlyList<ListEntry>? ListViewEntries => ListViewActive ? _listItems : null;
    public int ListViewSelected => _listSel;

    void ResetListView()
    {
        _listItems = null; _listHistory = new(); _listSel = -1; _listQuery = null; _listFor = null;
        _listDismissed = null; _listCompletionPending = false;
    }

    static bool ListViewEnabled => Options.Get("complete_auto_view") == "list" && Options.GetBool("complete_auto_complete");

    static bool ListSource(string name) => (":" + Options.Get("complete_auto_view_source") + ":").Contains(":" + name + ":", StringComparison.Ordinal);

    static int ListMax => Math.Max(1, Options.GetInt("complete_auto_view_maxlines", 10));

    void HideListView()
    {
        _listItems = null; _listSel = -1; _listQuery = null; _listFor = null; _listCompletionPending = false;
    }

    void UpdateListView()
    {
        if (!ListViewEnabled || Text.Length == 0 || Point != Text.Length || Modal != null || Menu != null || MarkActive
            || Keymap is not ("emacs" or "vi_imap"))
        {
            HideListView();
            return;
        }
        int b = 0;
        if (Text.Contains('\n'))
        {
            // a new line usually starts a new command: its text is the query
            if (!Options.GetBool("complete_auto_view_multiline")) { HideListView(); return; }
            b = LineStart(Text.Length);
        }
        // navigating: the line shows the selected entry
        if (_listItems != null && _listSel >= 0 && _listSel < _listItems.Count && b == _listBase
            && Text.Length >= _listBase && Text.Substring(_listBase) == _listItems[_listSel].Line) return;
        if (_listFor == Text && _listSel < 0) return;
        _listSel = -1;
        _listBase = b;
        _listFor = Text;
        _listQuery = Text.Substring(b);
        _listCompletionPending = false;
        if (_listQuery.Trim().Length == 0 || _listDismissed == Text) { _listItems = null; _listHistory = new(); return; }
        _listDismissed = null;
        _listHistory = ListSource("history") ? SearchListView(_listQuery, ListMax) : new();
        _listItems = _listHistory.Count > 0 ? new List<ListEntry>(_listHistory) : null;
        // after a space only argument values are listed (e.g. -ErrorAction <here>: Stop, Continue ...)
        _listCompletionPending = ListSource("completion");
    }

    /// <summary>History entries for the list view: prefix matches first, then substring matches,
    /// each group ordered by HistoryRanking.</summary>
    List<ListEntry> SearchListView(string query, int max)
    {
        var cmp = History.SmartCase(query);
        var rank = NewHistoryRanking(HistoryRanking.ListChecks);
        IEnumerable<string> Newest()
        {
            for (int i = History.Count - 1; i >= 0; i--)
                if (History[i] != query) yield return History[i];
        }
        var prefix = rank.Top(Newest().Where(h => h.StartsWith(query, cmp)), max);
        var sub = rank.Top(Newest().Where(h => !h.StartsWith(query, cmp) && h.Contains(query, cmp)), max - prefix.Count);
        return prefix.Concat(sub).Select(h => new ListEntry(h, "History")).ToList();
    }

    /// <summary>Idle task: adds TabExpansion2 results (files, parameters ...) to the list.</summary>
    bool ComputeListCompletions()
    {
        if (!_listCompletionPending || _listFor != Text || _listSel >= 0) return false;
        _listCompletionPending = false;
        var cs = CompleteAt(Text, Text.Length);
        // substring matches only (subsequence matches are too noisy for a list), and only without history entries
        bool afterSpace = char.IsWhiteSpace(Text[^1]);
        if ((cs == null || cs.Items.Count == 0) && Options.GetBool("complete_ambiguous") && _listHistory.Count == 0 && !afterSpace) cs = CompleteAmbiguous(subsequence: false);
        if (cs == null || cs.Items.Count == 0) return false;
        int ri = Math.Clamp(cs.ReplacementIndex, _listBase, Text.Length);
        int rl = Math.Clamp(cs.ReplacementLength, 0, Text.Length - ri);
        var word = Text.Substring(ri, rl);
        var head = Text.Substring(_listBase, ri - _listBase);
        var tail = Text.Substring(ri + rl);
        var comps = new List<ListEntry>();
        // complete_auto_view_completion=word: show only the completed word ("cp -r a/b " is not repeated)
        bool wordOnly = Options.Get("complete_auto_view_completion") != "line";
        foreach (var it in cs.Items)
        {
            if (comps.Count >= ListMax) break;
            if (afterSpace && it.Type != CompletionResultType.ParameterValue) continue;
            var c = UserSeparators(word, it.CompletionText);
            var line = head + c + tail;
            if (line == _listQuery || _listHistory.Any(h => h.Line == line)) continue;
            var src = it.Type is CompletionResultType.ProviderItem or CompletionResultType.ProviderContainer ? "File" : "Completion";
            comps.Add(wordOnly ? new ListEntry(line, src, c, word) : new ListEntry(line, src));
        }
        if (comps.Count == 0) return false;
        // history keeps the top rows, but completions get at least half of the list
        int max = ListMax;
        int histN = Math.Min(_listHistory.Count, max - Math.Min(comps.Count, Math.Max(1, max / 2)));
        var merged = _listHistory.Take(histN).Concat(comps).Take(max).ToList();
        _listItems = merged;
        return true;
    }

    static readonly HashSet<string> ListUpWidgets = new(StringComparer.Ordinal)
    {
        "backward-line-or-history-prev", "history-prev", "history-search-forward", "history-substring-search-forward",
    };
    static readonly HashSet<string> ListDownWidgets = new(StringComparer.Ordinal)
    {
        "forward-line-or-history-next", "history-next", "history-search-backward", "history-substring-search-backward",
    };

    /// <summary>Moves through the list while it is shown. Returns true if the key was consumed.</summary>
    bool ListViewKey(KeyInput k)
    {
        if (!ListViewActive || Modal != null || Keymap is not ("emacs" or "vi_imap") || _listQuery == null || _pending.Count > 0) return false;
        int dir;
        if (k.Name == "up") dir = -1;
        else if (k.Name == "down") dir = 1;
        else if (Keymaps.Get(Keymap).TryGet(k.Name, out var b) && b.Kind == BindingKind.Widget)
        {
            // keys bound to history widgets move through the list too: ...-prev / ...-search-forward go up
            // (newer), ...-next / ...-search-backward go down (older, like vi's j)
            if (ListUpWidgets.Contains(b.Widget)) dir = -1;
            else if (ListDownWidgets.Contains(b.Widget)) dir = 1;
            else return false;
        }
        else return false;
        int n = _listItems!.Count;
        if (dir > 0) _listSel = _listSel + 1 >= n ? -1 : _listSel + 1;
        else _listSel = _listSel < 0 ? n - 1 : _listSel - 1;
        Text = Text.Substring(0, _listBase) + (_listSel >= 0 ? _listItems[_listSel].Line : _listQuery);
        Point = Text.Length;
        return true;
    }

    /// <summary>S-RET in the list view: takes the selected (or first) entry and lists again for the new text.</summary>
    public bool AcceptListItem()
    {
        if (!ListViewActive || Point != Text.Length) return false;
        var e = _listItems![Math.Max(0, _listSel)];
        Text = Text.Substring(0, _listBase) + e.Line;
        Point = Text.Length;
        HideListView();
        return true;
    }

    /// <summary>C-g: back to the typed text and hide the list until the text changes.</summary>
    bool DismissListView()
    {
        if (!ListViewActive || _listQuery == null) return false;
        Text = Text.Substring(0, _listBase) + _listQuery;
        Point = Text.Length;
        _listDismissed = Text;
        HideListView();
        return true;
    }

    static void RegisterListViewWidgets()
    {
        W("toggle-suggestion-view", (e, k) =>
        {
            Options.Set("complete_auto_view", Options.Get("complete_auto_view") == "list" ? "inline" : "list");
            e._suggestFor = null; e._listDismissed = null; e.HideListView(); e.ClearSuggestion();
        });
    }

    void AddListViewPanels(RenderModel m, int width, int maxRows)
    {
        if (!ListViewActive || Menu != null || Modal != null || maxRows <= 0) return;
        var items = _listItems!;
        // complete_auto_view_selected: line = the whole row gets list_view_selected, entry = only the entry's
        // text gets it, color = only the entry's text color changes (list_view_selected_color)
        var mode = Options.Get("complete_auto_view_selected");
        string selSgr = Faces.Sgr(mode == "color" ? "list_view_selected_color" : "list_view_selected");
        bool wholeRow = mode is not ("entry" or "text" or "color");
        string matchSgr = Faces.Sgr("menu_complete_match");
        string descSgr = Faces.Sgr("menu_desc");
        int avail = Math.Max(10, width - 1);
        int srcW = items.Max(x => x.Source.Length) + 2;
        int textW = Math.Max(4, avail - 2 - 1 - srcW);
        int rows = Math.Min(items.Count, maxRows);
        int first = _listSel >= rows ? _listSel - rows + 1 : 0;
        for (int r = first; r < first + rows; r++)
        {
            bool sel = r == _listSel;
            var pl = new PanelLine();
            var query = items[r].Match ?? _listQuery ?? "";
            var cmp = History.SmartCase(query);
            var text = TextWidth.Truncate((items[r].Display ?? items[r].Line).Replace('\n', ' '), textW);
            // the selected row (or only its text) gets list_view_selected; the "> " marker stays either way
            string extra = sel ? selSgr : "";
            string rowExtra = wholeRow ? extra : "";
            pl.Add(sel ? "> " : "  ", rowExtra);
            int mi = query.Length > 0 ? text.IndexOf(query, cmp) : -1;
            if (mi >= 0)
            {
                pl.Add(text.Substring(0, mi), extra);
                pl.Add(text.Substring(mi, query.Length), matchSgr + extra);
                pl.Add(text.Substring(mi + query.Length), extra);
            }
            else pl.Add(text, extra);
            var src = "[" + items[r].Source + "]";
            pl.Add(new string(' ', textW - TextWidth.StringWidth(text) + 1 + srcW - src.Length), rowExtra);
            pl.Add(src, sel && wholeRow ? extra : descSgr);
            m.Panels.Add(pl);
        }
        if (items.Count > rows || _listSel >= 0)
        {
            var pos = _listSel >= 0 ? $"{_listSel + 1}/{items.Count}" : $"{items.Count} items";
            m.Panels.Add(PanelLine.Of($"({pos}, up/down: select, S-RET: take, C-g: cancel)", "menu_info"));
        }
    }
}
