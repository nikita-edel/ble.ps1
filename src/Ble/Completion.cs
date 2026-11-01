using System.Management.Automation;
using System.Text.RegularExpressions;

namespace Ble;

/// <summary>TAB completion: the menu, ambiguous matches, and how completions keep what the user typed.</summary>
public sealed partial class Editor
{
    public Menu? Menu;

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
        var (ri, rl) = ReplacementIn(cs);
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

    // what a completion replaces, inside the text and not before 'min'
    (int Index, int Length) ReplacementIn(CompletionSet cs, int min = 0)
    {
        int index = Math.Clamp(cs.ReplacementIndex, min, Text.Length);
        return (index, Math.Clamp(cs.ReplacementLength, 0, Text.Length - index));
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
        Splice(ri, ri + rl, s);
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
                    CloseMenu();
                    PushUndo(m.Orig, m.OrigPoint);
                    if (m.Items.Count == 1 && NeedsSpaceAfter(m.Items[m.Selected]) && (Point >= Text.Length || Text[Point] != ' ')) Insert(" ");
                    return true;
                }
            case "C-g":
            case "ESC":
                Text = m.Orig; Point = m.OrigPoint;
                CloseMenu();
                return true;
            default:
                // accept the current candidate and process the key normally
                CloseMenu();
                PushUndo(m.Orig, m.OrigPoint);
                LastWidget = "menu-accept";
                return false;
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
        while (ws > 0 && !char.IsWhiteSpace(Text[ws - 1]) && !"|;(){}=,&".Contains(Text[ws - 1])) ws--;
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

    // Completion results for the current line. TAB, the inline suggestion and the list view ask for
    // the same text, and TabExpansion2 is the most expensive call in the editor.
    readonly Dictionary<(string Text, int Cursor), CompletionSet?> _completeCache = new();
    bool _idleTask;   // completions started by idle tasks stop as soon as a key is pressed

    CompletionSet? CompleteAt(string text, int cursor)
    {
        if (_completeCache.TryGetValue((text, cursor), out var cached)) return cached;
        // an idle task can ask several times (prefix, then ambiguous): the key goes first
        if (_idleTask && Term.KeyAvailable) return null;
        bool stopped = false;
        var r = Shell.Complete(text, cursor, _idleTask ? () => stopped = stopped || Term.KeyAvailable : null);
        if (stopped) return null;   // incomplete: not cached
        if (_completeCache.Count >= 64) _completeCache.Clear();
        _completeCache[(text, cursor)] = r;
        return r;
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
        completion = VariableForm(word, TildeForm(word, completion));
        bool slash = word.Contains('/'), backslash = word.Contains('\\');
        if (slash && !backslash) return completion.Replace('\\', '/');
        // PowerShell on Linux/macOS completes with /: keep the \ typed, except in a path from the root (/usr/bin)
        if (backslash && !slash && !OperatingSystem.IsWindows() && !completion.TrimStart('\'', '"').StartsWith('/'))
            return completion.Replace('/', '\\');
        return completion;
    }

    // PowerShell completes "~/d" to "C:\Users\me\dev": keep the ~ the user typed ("~/dev")
    static string TildeForm(string word, string completion)
    {
        if (!(word == "~" || (word.Length > 1 && word[0] == '~' && IsSep(word[1])))) return completion;
        return KeepHead(completion, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~");
    }

    // a variable at the start of a path: $name, ${name}, $env:NAME
    static readonly Regex VariableHead =
        new(@"^\$(?:\{(?<n>[^}]+)\}|(?<n>(?:\w+:)?\w+))(?=[\\/]|$)", RegexOptions.Compiled);

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
}
