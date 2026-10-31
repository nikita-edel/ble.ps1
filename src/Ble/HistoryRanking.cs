namespace Ble;

/// <summary>
/// Chooses and orders history entries for suggestions; the inline suggestion and the list view both
/// use it. The rules, in order:
///   1. Entries that would fail here are dropped (complete_auto_history_validate, Highlighter.IsValid):
///        command  a command name that doesn't exist
///        path     a path that doesn't exist from the current directory: a word that looks like a
///                 path (.\x, ~\x, C:\x, dir\x where dir exists), or a word that was a file or
///                 directory in a directory the entry ran in ("cd dev" from ~ fails in ~\dev)
///   2. Entries that ran in the current directory come before the others (complete_auto_history_cwd).
///   3. Newer entries come before older ones.
/// Multi-line entries are left out unless complete_auto_history_multiline is on.
/// Rule 1 looks at the file system, so it runs for at most maxChecks entries per lookup; the
/// entries after that are left out.
/// Top also shows entries that mean the same command once (complete_auto_history_merge, see Key):
/// the best of them stays.
/// Similar finds entries for a query that matches none (complete_auto_history_fuzzy).
/// </summary>
public sealed class HistoryRanking
{
    public const int InlineChecks = 50, ListChecks = 100;

    readonly History _history;
    readonly string? _dir;
    readonly Func<string, bool>? _valid;
    readonly Func<string, string>? _key;
    readonly int _maxChecks;
    readonly bool _multiline;
    int _checks;

    /// <param name="dir">the current directory for rule 2, or null to turn it off</param>
    /// <param name="valid">rule 1, or null to turn it off</param>
    /// <param name="key">entries with the same key are the same command (Key), or null to keep all</param>
    /// <param name="multiline">false: entries with several lines are never suggested</param>
    public HistoryRanking(History history, string? dir, Func<string, bool>? valid, int maxChecks, Func<string, string>? key = null, bool multiline = true)
    {
        _history = history; _dir = dir; _valid = valid; _maxChecks = maxChecks; _key = key; _multiline = multiline;
    }

    public string? KeyOf(string entry) => _key?.Invoke(entry);

    bool RanHere(string entry) => _dir != null && _history.RanIn(entry, _dir);

    bool Allowed(string entry) => _multiline || !entry.Contains('\n');

    bool Exhausted => _valid != null && _checks >= _maxChecks;

    bool Passes(string entry)
    {
        if (_valid == null) return true;
        _checks++;
        return _valid(entry);
    }

    /// <summary>The best of the candidates (given newest first), or null.</summary>
    public string? Best(IEnumerable<string> candidates)
    {
        string? elsewhere = null;
        foreach (var h in candidates.Where(Allowed))
        {
            bool here = RanHere(h);
            if (!here && elsewhere != null) continue;   // only an entry from here can still beat it
            if (Exhausted) break;
            if (!Passes(h)) continue;
            if (here) return h;
            elsewhere = h;
            if (_dir == null) break;                    // rule 2 is off: nothing can beat it
        }
        return elsewhere;
    }

    /// <summary>
    /// Up to max of the candidates (given newest first), best first. Entries whose key is in 'seen'
    /// are left out, and the keys of the result are added to it.
    /// </summary>
    public List<string> Top(IEnumerable<string> candidates, int max, HashSet<string>? seen = null)
    {
        var result = new List<(string Entry, string? Key)>();
        int here = 0;   // entries from here, at the start of result
        if (max <= 0) return new List<string>();
        foreach (var h in candidates.Where(Allowed))
        {
            bool isHere = RanHere(h);
            if (result.Count >= max)
            {
                // full: only an entry from here can still get in (it pushes out one from elsewhere)
                if (_dir == null || here >= max) break;
                if (!isHere) continue;
            }
            var k = _key?.Invoke(h);
            int dup = -1;
            if (k != null)
            {
                if (seen != null && seen.Contains(k)) continue;
                dup = result.FindIndex(r => r.Key == k);
                // the same command is in already: one from here only takes the place of one from elsewhere
                if (dup >= 0 && (!isHere || dup < here)) continue;
            }
            if (Exhausted) break;
            if (!Passes(h)) continue;
            if (dup >= 0) result.RemoveAt(dup);
            result.Insert(isHere ? here++ : result.Count, (h, k));
            if (result.Count > max) result.RemoveAt(result.Count - 1);
        }
        if (seen != null) foreach (var r in result) if (r.Key != null) seen.Add(r.Key);
        return result.ConvertAll(r => r.Entry);
    }

    // ---- same command (complete_auto_history_merge) ----

    /// <summary>
    /// What an entry is compared by. paths: a word is a path if it has / \ ~ or a leading dot, or
    /// if exists() says so (a file or folder in dir); paths count by where they point: relative
    /// ones from dir (if given), ~ and the home folder are the same, / and \ are the same, and
    /// .\, ..\, a trailing \, doubled separators and quotes around the word don't count. The
    /// command (first word) and parameters (-x) are never paths. ignoreCase: upper and lower case
    /// are the same. Spaces between words never count.
    /// </summary>
    public static string Key(string line, bool paths, bool ignoreCase, string? dir = null, Func<string, bool>? exists = null)
    {
        var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (paths)
            for (int i = 1; i < words.Length; i++) words[i] = PathKey(words[i], dir, exists);
        var s = string.Join(' ', words);
        return ignoreCase ? s.ToLowerInvariant() : s;
    }

    static string PathKey(string w, string? dir, Func<string, bool>? exists)
    {
        if (w.Length >= 2 && w[0] is '\'' or '"' && w[^1] == w[0]) w = w.Substring(1, w.Length - 2);
        if (w.Length == 0 || w[0] == '-' || w.IndexOfAny(['$', '*', '?']) >= 0) return w;
        bool looks = w.IndexOfAny(['/', '\\']) >= 0 || w[0] is '~' or '.';
        if (!looks && !(dir != null && exists != null && exists(Path.Combine(dir, w)))) return w;
        // PowerShell takes \ as a separator everywhere; .NET only on Windows
        if (!OperatingSystem.IsWindows()) w = w.Replace('\\', '/');
        if (w[0] == '~')
        {
            if (w.Length > 1 && !Editor.IsSep(w[1])) return w;   // ~user
            w = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + w.Substring(1);
        }
        try
        {
            if (dir != null || Path.IsPathRooted(w)) w = Path.GetFullPath(dir != null ? Path.Combine(dir, w) : w);
        }
        catch { }
        var trimmed = w.TrimEnd('\\', '/');
        if (trimmed.Length > 0 && trimmed[^1] != ':') w = trimmed;
        w = Editor.TildePath(w).Replace('/', '\\');
        while (w.Contains(@"\\")) w = w.Replace(@"\\", @"\");
        while (w.StartsWith(@".\", StringComparison.Ordinal) && w.Length > 2) w = w.Substring(2);
        return w;
    }

    // ---- similar entries (complete_auto_history_fuzzy) ----

    /// <summary>
    /// History entries similar to a query, best first: fewest typos, where each doubling of how often
    /// an entry was run outweighs one typo (so an old typo of yours doesn't beat the command you
    /// meant), then newest. Similar means:
    /// word by word, in order, ignoring case and / vs \; the first word (the command) must match;
    /// the last word may be the start of a longer one; at most 2 words of the entry are skipped;
    /// per word at most 1 typo (3-5 letters) or 2 (6 and more), none in shorter words. A typo is a
    /// wrong, missing, extra or swapped letter. Queries with fewer than 3 letters find nothing.
    /// </summary>
    public static List<string> Similar(History history, string query, int limit = 500)
    {
        var found = new List<(int Cost, int Age, string Entry)>();
        var q = Words(query);
        if (q.Length == 0 || q.Sum(w => w.Length) < 3) return new List<string>();
        bool partial = !char.IsWhiteSpace(query[^1]);
        for (int i = history.Count - 1, age = 0; i >= 0 && found.Count < limit; i--, age++)
        {
            var e = history[i];   // entries are unique (History keeps one of each)
            if (e == query) continue;
            if (Similarity(q, partial, Words(e)) is int cost)
                found.Add((cost - 4 * (int)Math.Log2(Math.Max(1, history.Runs(e))), age, e));
        }
        return found.OrderBy(f => f.Cost).ThenBy(f => f.Age).Select(f => f.Entry).ToList();
    }

    static string[] Words(string s) =>
        s.Replace('/', '\\').ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Typos plus skipped words (typos count more), or null if not similar.</summary>
    internal static int? Similarity(string[] q, bool partial, string[] e)
    {
        int cost = 0, skipped = 0, j = 0;
        for (int i = 0; i < q.Length; i++)
        {
            bool prefix = partial && i == q.Length - 1;
            int allowed = q[i].Length <= 2 ? 0 : q[i].Length <= 5 ? 1 : 2;
            while (true)
            {
                if (j >= e.Length) return null;
                int c = Typos(q[i], e[j], prefix);
                j++;
                if (c <= allowed) { cost += c; break; }
                if (i == 0 || ++skipped > 2) return null;
            }
        }
        return cost * 4 + skipped;
    }

    /// <summary>
    /// Typos between a query word and an entry word (optimal string alignment: wrong, missing, extra
    /// or swapped letters); 3 means "3 or more". prefix: against the best start of the entry word
    /// (one letter shorter to one letter longer than the query word).
    /// </summary>
    static int Typos(string a, string b, bool prefix)
    {
        if (prefix) b = b.Length > a.Length + 1 ? b.Substring(0, a.Length + 1) : b;
        int from = prefix ? Math.Max(0, a.Length - 1) : b.Length;   // the end columns that count
        if (from > b.Length || Math.Abs(a.Length - b.Length) > 2 + (prefix ? 1 : 0)) return 3;
        int n = b.Length + 1;
        Span<int> prev2 = n <= 64 ? stackalloc int[n] : new int[n];
        Span<int> prev = n <= 64 ? stackalloc int[n] : new int[n];
        Span<int> cur = n <= 64 ? stackalloc int[n] : new int[n];
        for (int j = 0; j < n; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j < n; j++)
            {
                int d = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d = Math.Min(d, prev2[j - 2] + 1);
                cur[j] = d;
            }
            var t = prev2; prev2 = prev; prev = cur; cur = t;
        }
        int best = 3;
        for (int j = from; j < n; j++) best = Math.Min(best, prev[j]);
        return best;
    }
}
