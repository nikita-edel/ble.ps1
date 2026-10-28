namespace Ble;

/// <summary>Word boundary helpers.</summary>
public static class TextOps
{
    /// <summary>C-word: letters, digits and underscore.</summary>
    public static bool IsCWord(char c) => char.IsLetterOrDigit(c) || c == '_';
    /// <summary>U-word (unix word): any non-whitespace.</summary>
    public static bool IsUWord(char c) => !char.IsWhiteSpace(c);

    public static int ForwardWord(string t, int p, Func<char, bool> isWord)
    {
        int n = t.Length;
        while (p < n && !isWord(t[p])) p++;
        while (p < n && isWord(t[p])) p++;
        return p;
    }

    public static int BackwardWord(string t, int p, Func<char, bool> isWord)
    {
        while (p > 0 && !isWord(t[p - 1])) p--;
        while (p > 0 && isWord(t[p - 1])) p--;
        return p;
    }

    // vi word classes: 0 = blank, 1 = keyword, 2 = other
    public static int ViClass(char c, bool big)
    {
        if (char.IsWhiteSpace(c)) return 0;
        if (big) return 1;
        return IsCWord(c) ? 1 : 2;
    }

    public static int ViNextWordStart(string t, int p, bool big)
    {
        int n = t.Length;
        if (p >= n) return n;
        int cls = ViClass(t[p], big);
        if (cls != 0) while (p < n && ViClass(t[p], big) == cls) p++;
        while (p < n && ViClass(t[p], big) == 0)
        {
            // an empty line counts as a word
            if (t[p] == '\n' && p + 1 < n && t[p + 1] == '\n') return p + 1;
            p++;
        }
        return p;
    }

    public static int ViWordEnd(string t, int p, bool big)
    {
        int n = t.Length;
        if (n == 0) return 0;
        p++;
        while (p < n && ViClass(t[p], big) == 0) p++;
        if (p >= n) return n - 1;
        int cls = ViClass(t[p], big);
        while (p + 1 < n && ViClass(t[p + 1], big) == cls) p++;
        return p;
    }

    public static int ViPrevWordStart(string t, int p, bool big)
    {
        if (p <= 0) return 0;
        p--;
        while (p > 0 && ViClass(t[p], big) == 0) p--;
        int cls = ViClass(t[p], big);
        while (p > 0 && ViClass(t[p - 1], big) == cls) p--;
        return p;
    }

    public static int ViPrevWordEnd(string t, int p, bool big)
    {
        if (p <= 0) return 0;
        int cls = ViClass(t[Math.Min(p, t.Length - 1)], big);
        if (p < t.Length && cls != 0) while (p > 0 && ViClass(t[p], big) == cls) p--;
        while (p > 0 && ViClass(t[p], big) == 0) p--;
        return p;
    }

    public static int FirstNonBlank(string t, int lineStart)
    {
        int p = lineStart;
        while (p < t.Length && t[p] != '\n' && (t[p] == ' ' || t[p] == '\t')) p++;
        return p;
    }

    public static int MatchBracket(string t, int p)
    {
        const string open = "([{", close = ")]}";
        int n = t.Length;
        while (p < n && t[p] != '\n' && open.IndexOf(t[p]) < 0 && close.IndexOf(t[p]) < 0) p++;
        if (p >= n || t[p] == '\n') return -1;
        char c = t[p];
        int oi = open.IndexOf(c);
        if (oi >= 0)
        {
            char cc = close[oi]; int depth = 0;
            for (int i = p; i < n; i++)
            {
                if (t[i] == c) depth++;
                else if (t[i] == cc && --depth == 0) return i;
            }
        }
        else
        {
            int ci = close.IndexOf(c);
            char oc = open[ci]; int depth = 0;
            for (int i = p; i >= 0; i--)
            {
                if (t[i] == c) depth++;
                else if (t[i] == oc && --depth == 0) return i;
            }
        }
        return -1;
    }
}
