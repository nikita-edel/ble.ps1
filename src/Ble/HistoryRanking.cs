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
/// Rule 1 looks at the file system, so it runs for at most maxChecks entries per lookup; the
/// entries after that are left out.
/// </summary>
public sealed class HistoryRanking
{
    public const int InlineChecks = 50, ListChecks = 100;

    readonly History _history;
    readonly string? _dir;
    readonly Func<string, bool>? _valid;
    readonly int _maxChecks;
    int _checks;

    /// <param name="dir">the current directory for rule 2, or null to turn it off</param>
    /// <param name="valid">rule 1, or null to turn it off</param>
    public HistoryRanking(History history, string? dir, Func<string, bool>? valid, int maxChecks)
    {
        _history = history; _dir = dir; _valid = valid; _maxChecks = maxChecks;
    }

    bool RanHere(string entry) => _dir != null && _history.RanIn(entry, _dir);

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
        foreach (var h in candidates)
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

    /// <summary>Up to max of the candidates (given newest first), best first.</summary>
    public List<string> Top(IEnumerable<string> candidates, int max)
    {
        var result = new List<string>();
        int here = 0;   // entries from here, at the start of result
        if (max <= 0) return result;
        foreach (var h in candidates)
        {
            bool isHere = RanHere(h);
            if (result.Count >= max)
            {
                // full: only an entry from here can still get in (it pushes out one from elsewhere)
                if (_dir == null || here >= max) break;
                if (!isHere) continue;
            }
            if (Exhausted) break;
            if (!Passes(h)) continue;
            result.Insert(isHere ? here++ : result.Count, h);
            if (result.Count > max) result.RemoveAt(result.Count - 1);
        }
        return result;
    }
}
