using System.Management.Automation;

namespace Ble;

/// <summary>
/// The suggestion shown after the cursor, from history first and from completion once the editor is
/// idle, how it is accepted, and the idle tasks. Which history entry is suggested: HistoryRanking.
/// </summary>
public sealed partial class Editor
{
    // auto-suggestion state
    internal string? _suggest;            // suggested text shown after the cursor
    string? _suggestFor;                  // buffer text the suggestion was computed for
    (int Start, string Word)? _suggestReplace;   // replace this word on accept (completion with different case)
    // ble.sh "ambiguous" auto-complete (types r/m/a/A): the word does not start the candidate, so the
    // candidate is shown as " [cand] " and accepting replaces the word from Start with it.
    (int Start, string Insert)? _suggestAmbig;
    bool _completionSuggestPending;
    string? _suggestDismissed;            // C-g hides the suggestion until the text changes
    bool _autoMenuPending;

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

    // S-RET takes the list entry over a " [correction] ", so the entry is what's shown
    public string? Suggestion => ListHint ?? _suggest;

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
        if (!ListViewEnabled && Options.GetBool("complete_auto_history") && !Options.ListHas("complete_auto_complete_opts", "history-disabled"))
        {
            var query = new HistoryQuery(Text.Substring(HistoryQueryStart));
            var h = NewHistoryRanking(HistoryRanking.InlineChecks).Best(History.PrefixMatches(query, EachHistoryLine));
            if (h != null) _suggest = query.Rest(HistoryQuery.Tidy(h));
        }
        // idle task: TabExpansion2, then similar history entries (complete_auto_history_fuzzy)
        if (_suggest == null && (CompletionSuggestWanted || (!ListViewEnabled && Options.GetBool("complete_auto_history_fuzzy"))))
            _completionSuggestPending = true;
        ScheduleAutoMenu();
    }

    bool CompletionSuggestWanted => Options.GetBool("complete_auto_completion") && Text.Length > 0 && !char.IsWhiteSpace(Text[^1]);

    static MultilineHistory MultilineHistoryMode => Options.Get("complete_auto_history_multiline") switch
    {
        "line" => MultilineHistory.Lines,
        "whole" => MultilineHistory.Whole,
        _ => MultilineHistory.None,   // none, and 0, empty or anything else
    };

    static bool EachHistoryLine => MultilineHistoryMode == MultilineHistory.Lines;

    // Where the text history is matched against starts. Each line on its own: the last line of the
    // input, after its indentation (the lines in history have none). Otherwise the whole input.
    int HistoryQueryStart
    {
        get
        {
            if (!EachHistoryLine) return 0;
            int b = LineStart(Text.Length);
            while (b < Text.Length && char.IsWhiteSpace(Text[b])) b++;
            return b;
        }
    }

    // HistoryRanking with the options: complete_auto_history_cwd, complete_auto_history_validate (or the one
    // named by validateOption), complete_auto_history_merge, complete_auto_history_multiline
    HistoryRanking NewHistoryRanking(int maxChecks, string validateOption = "complete_auto_history_validate")
    {
        var dir = Options.GetBool("complete_auto_history_cwd") ? Shell.CurrentDirectory : null;
        bool commands = Options.ListHas(validateOption, "command"), paths = Options.ListHas(validateOption, "path"), lines = EachHistoryLine;
        Func<string, bool>? valid = commands || paths ? line => Highlighter.IsValid(line, commands, paths, History.DirsOf(line, lines)) : null;
        return new HistoryRanking(History, dir, valid, maxChecks, HistoryKey(), MultilineHistoryMode);
    }

    // relative paths count from the current directory; plain words are paths if they exist there
    Func<string, string>? HistoryKey()
    {
        bool paths = Options.ListHas("complete_auto_history_merge", "path"), ignoreCase = Options.ListHas("complete_auto_history_merge", "case");
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
        return rank.Top(HistoryRanking.Similar(History, query, lines: EachHistoryLine), max, seen).ConvertAll(HistoryQuery.Tidy).Distinct().ToList();
    }

    // after the completion-based suggestion found nothing
    bool ComputeSimilarSuggestion()
    {
        if (_suggestFor != Text || _suggest != null || Point != Text.Length || Modal != null || ListViewEnabled) return false;
        int start = HistoryQueryStart;
        if (start == Text.Length) return false;
        var h = SimilarHistory(Text.Substring(start), 1);
        return h.Count > 0 && SetAmbiguousSuggestion(start, h[0]);
    }

    void ScheduleAutoMenu()
    {
        _autoMenuPending = Options.Get("complete_auto_menu").Length > 0 && Menu == null && Text.Length > 0
            && Point == Text.Length && !char.IsWhiteSpace(Text[^1]) && Modal == null && Keymap is "emacs" or "vi_imap";
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
            var first = cs.Items.FirstOrDefault(i => !HideCommandPath(i, word));
            if (first == null) return false;
            var c = UserSeparators(word, first.CompletionText);
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
        var best = amb.Items.FirstOrDefault(i => !HideCommandPath(i, w0));
        return best != null && SetAmbiguousSuggestion(amb.ReplacementIndex, UserSeparators(w0, best.CompletionText));
    }

    // complete_auto_hide_command_paths: PowerShell also offers /usr/bin/ls for ls when a function or
    // alias has the same name. A word with a separator in it is a path the user is typing, so that stays.
    static bool HideCommandPath(CompletionItem it, string word) =>
        it.Type == CompletionResultType.Command && Options.GetBool("complete_auto_hide_command_paths")
        && word.IndexOfAny(['/', '\\']) < 0 && it.CompletionText.IndexOfAny(['/', '\\']) >= 0;

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
        if (ListHint is { } hint)
        {
            // accepted like an inline suggestion; the entry's spelling replaces what was typed
            var entry = _listItems![0].Line;
            _suggest = hint; _suggestFor = Text; _suggestAmbig = null;
            _suggestReplace = entry.StartsWith(_listQuery!, StringComparison.Ordinal) ? null : (_listBase, entry);
        }
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
            ReplaceEnd(astart, prefix);
            _suggestAmbig = null; _suggestReplace = null;
            _suggest = prefix.Length < insert.Length ? insert.Substring(prefix.Length) : null;
            _suggestFor = _suggest != null ? Text : null;
            return true;
        }
        if (_suggestReplace is var (start, word) && !wordOnly)
        {
            ReplaceEnd(start, word);
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
        var rest = _suggest.Substring(part.Length);
        // keep the spelling of the replacement for the accepted part
        if (_suggestReplace is var (s2, w2)) Text = Text.Substring(0, s2) + w2.Substring(0, w2.Length - rest.Length);
        else Text += part;
        Point = Text.Length;
        _suggest = rest.Length > 0 ? rest : null;
        _suggestFor = _suggest != null ? Text : null;
        if (_suggest == null) _suggestReplace = null;
        return true;
    }

    // ---- idle tasks ----

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
                    var (ri, rl) = ReplacementIn(cs);
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
}
