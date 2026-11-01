namespace Ble;

/// <summary>
/// What typed text matches in history. PowerShell doesn't care how much space is between words, so
/// neither does this: spaces in front are ignored, and any run of spaces and tabs matches any other.
/// Inside quotes and after a backtick the text has to be the same, there a space is part of a word.
/// A query without words matches nothing.
/// </summary>
public sealed class HistoryQuery
{
    readonly List<string> _words;
    readonly bool _endsInSpace;
    readonly StringComparison _cmp;

    public HistoryQuery(string query, StringComparison cmp = StringComparison.Ordinal)
    {
        _words = Words(query, out _endsInSpace);
        _cmp = cmp;
    }

    /// <summary>The first word: every match contains it as it is. Empty for a query without words.</summary>
    public string Head => _words.Count > 0 ? _words[0] : "";

    /// <summary>A history entry the way it is suggested: one space between words, none around them.
    /// Entries with several lines stay (here-strings, indentation).</summary>
    public static string Tidy(string entry) => entry.Contains('\n') ? entry : string.Join(' ', Words(entry, out _));

    static bool IsSpace(char c) => c is ' ' or '\t';

    static List<string> Words(string text, out bool endsInSpace)
    {
        var words = new List<string>();
        int start = 0;
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '`' && quote != '\'') i++;
            else if (quote != '\0') { if (c == quote) quote = '\0'; }
            else if (c is '\'' or '"') quote = c;
            else if (IsSpace(c))
            {
                if (i > start) words.Add(text.Substring(start, i - start));
                start = i + 1;
            }
        }
        endsInSpace = start == text.Length;
        if (!endsInSpace) words.Add(text.Substring(start));
        return words;
    }

    // where the query ends if it matches at index 'at', otherwise -1
    int MatchAt(string text, int at)
    {
        for (int w = 0; w < _words.Count; w++)
        {
            var word = _words[w];
            if (text.Length - at < word.Length || !text.AsSpan(at, word.Length).Equals(word, _cmp)) return -1;
            at += word.Length;
            if (w == _words.Count - 1 && !_endsInSpace) break;
            int next = at;
            while (next < text.Length && IsSpace(text[next])) next++;
            if (next == at) return -1;
            at = next;
        }
        return at;
    }

    /// <summary>How much of the entry the query covers if the entry starts with it, otherwise -1.</summary>
    public int PrefixLength(string entry) => _words.Count > 0 ? MatchAt(entry, 0) : -1;

    /// <summary>What the entry adds to the query, or null if it doesn't start with it or adds nothing.</summary>
    public string? Rest(string entry)
    {
        int n = PrefixLength(entry);
        return n >= 0 && n < entry.Length ? entry.Substring(n) : null;
    }

    /// <summary>Where the query is in the text, or null.</summary>
    public (int Index, int Length)? Find(string text)
    {
        if (_words.Count == 0) return null;
        for (int i = text.IndexOf(Head, _cmp); i >= 0; i = text.IndexOf(Head, i + 1, _cmp))
        {
            int end = MatchAt(text, i);
            if (end >= 0) return (i, end - i);
        }
        return null;
    }
}
