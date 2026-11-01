using System.Text;
using System.Text.RegularExpressions;

namespace Ble;

/// <summary>vi ex commands (:).</summary>
public sealed partial class Editor
{
    string? _viLastEx;
    (int First, int Last)? _viVisualLines;
    string? _viLastSubstitute;

    void ViReadEx(string initial)
    {
        string q = initial;
        Info = ":" + q;
        InfoFace = "status_line";
        Modal = key =>
        {
            switch (key.Name)
            {
                case "RET": case "C-m": case "C-j":
                    Modal = null; Info = null;
                    if (q.Trim().Length > 0) RunEx(q);
                    return true;
                case "ESC": case "C-c": case "C-g":
                    Modal = null; Info = null;
                    return true;
                case "C-u":
                    q = ""; Info = ":";
                    return true;
                case "DEL": case "C-h":
                    if (q.Length == 0) { Modal = null; Info = null; return true; }
                    q = q.Substring(0, q.Length - 1);
                    Info = ":" + q;
                    return true;
            }
            if (key.Char != '\0') { q += key.Char; Info = ":" + q; }
            return true;
        };
    }

    void RunEx(string cmd)
    {
        var before = Text; var bp = Point;
        bool rep = _viReplaying;
        _viReplaying = true;   // nested commands (e.g. :normal) must not push their own undo entries
        try { ExecuteEx(cmd); }
        catch (Exception ex) { ShowError("E: " + ex.Message); }
        finally { _viReplaying = rep; }
        if (Text != before) PushUndo(before, bp);
        if (Keymap == "vi_nmap") ViClampNormal();
    }

    /// <summary>Executes an ex command line such as "%s/a/b/g", "2,4d", ":set name=value", "normal Ax".</summary>
    public void ExecuteEx(string cmd)
    {
        _viLastEx = cmd;
        cmd = cmd.Trim();
        if (cmd.StartsWith(':')) cmd = cmd.Substring(1).TrimStart();
        int i = 0;
        int cur = LineNumber(Point), last = LineCount - 1;
        int? r1 = null, r2 = null;

        int? Addr()
        {
            int? v = null;
            if (i < cmd.Length && cmd[i] == '.') { v = cur; i++; }
            else if (i < cmd.Length && cmd[i] == '$') { v = last; i++; }
            else if (i < cmd.Length && char.IsDigit(cmd[i]))
            {
                int st = i; while (i < cmd.Length && char.IsDigit(cmd[i])) i++;
                v = int.Parse(cmd.AsSpan(st, i - st)) - 1;
            }
            else if (i + 1 < cmd.Length && cmd[i] == '\'')
            {
                char m = cmd[i + 1]; i += 2;
                if (m == '<') v = _viVisualLines?.First ?? cur;
                else if (m == '>') v = _viVisualLines?.Last ?? cur;
                else if (_viMarks.TryGetValue(m, out var mp)) v = LineNumber(mp);
                else throw new InvalidOperationException("E20: Mark not set");
            }
            while (i < cmd.Length && (cmd[i] == '+' || cmd[i] == '-'))
            {
                int sign = cmd[i] == '+' ? 1 : -1; i++;
                int st = i; while (i < cmd.Length && char.IsDigit(cmd[i])) i++;
                int d = i > st ? int.Parse(cmd.AsSpan(st, i - st)) : 1;
                v = (v ?? cur) + sign * d;
            }
            return v;
        }

        if (i < cmd.Length && cmd[i] == '%') { r1 = 0; r2 = last; i++; }
        else
        {
            r1 = Addr();
            if (r1 != null) r2 = r1;
            if (i < cmd.Length && (cmd[i] == ',' || cmd[i] == ';'))
            {
                i++;
                r2 = Addr() ?? throw new InvalidOperationException("E14: Invalid address");
            }
        }
        var rest = cmd.Substring(i).Trim();
        if (r1 != null && r2 != null && r1 > r2) (r1, r2) = (r2, r1);
        int a = Math.Clamp(r1 ?? cur, 0, last), b = Math.Clamp(r2 ?? cur, 0, last);

        if (rest.Length == 0)
        {
            if (r2 != null) Point = TextOps.FirstNonBlank(Text, LineStartOfNumber(b));
            return;
        }

        // command name: letters (plus !), or a single symbol
        int ni = 0;
        while (ni < rest.Length && char.IsLetter(rest[ni])) ni++;
        if (ni == 0) ni = 1;
        var name = rest.Substring(0, ni);
        bool bang = ni < rest.Length && rest[ni] == '!';
        var arg = rest.Substring(Math.Min(rest.Length, ni + (bang ? 1 : 0)));
        int lsA = LineStartOfNumber(a), leB = LineEnd(LineStartOfNumber(b));

        switch (name)
        {
            case "s": case "su": case "substitute": case "&":
                Substitute(name == "&" ? "" : arg, a, b);
                return;
            case "d": case "de": case "del": case "delete":
                ViOperator("d", lsA, leB, true, arg.Trim().Length > 0 ? arg.Trim()[0] : '\0', 1, Text, Point);
                return;
            case "y": case "ya": case "yank":
                ViOperator("y", lsA, leB, true, arg.Trim().Length > 0 ? arg.Trim()[0] : '\0', 1, Text, Point);
                return;
            case "j": case "join":
                {
                    Point = lsA;
                    int ii = 0;
                    ViSimple("J", Math.Max(2, b - a + 1), '\0', [], ref ii, Text, Point);
                    return;
                }
            case ">": case "<":
                {
                    int times = 1 + arg.Count(ch => ch == name[0]);
                    for (int t = 0; t < times; t++) ViOperator(name, LineStartOfNumber(a), LineEnd(LineStartOfNumber(b)), true, '\0', 1, Text, Point);
                    return;
                }
            case "q": case "qa": case "quit": case "cq":
                // ble.sh: ":q" in normal mode exits the shell
                Result = "exit";
                Done = true;
                return;
            case "w": case "wq": case "x": case "xit": case "exit": case "up": case "update": case "wa": case "wqa": case "xa":
                AcceptLine();
                return;
            case "noh": case "nohlsearch":
                return;
            case "u": case "un": case "undo":
                Widgets["undo"](this, default);
                return;
            case "red": case "redo":
                Widgets["redo"](this, default);
                return;
            case "se": case "set":
                ExSet(arg.Trim());
                return;
            case "norm": case "normal":
                ExNormal(arg.TrimStart(), a, b, r1 != null);
                return;
            case "ma": case "mark": case "k":
                {
                    var m = arg.Trim();
                    if (m.Length == 1 && char.IsLetter(m[0])) _viMarks[m[0]] = LineStartOfNumber(b);
                    else throw new InvalidOperationException("E191: Argument must be a letter");
                    return;
                }
        }
        ShowError("E492: Not an editor command: " + cmd);
    }

    void ExSet(string arg)
    {
        if (arg.Length == 0) { Info = "usage: :set name=value | name? | name | noname"; return; }
        foreach (var part in arg.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq > 0) { Options.Set(part.Substring(0, eq), part.Substring(eq + 1)); continue; }
            if (part.EndsWith('?')) { var n = part[..^1]; Info = Options.Exists(n) ? $"{n}={Options.Get(n)}" : $"E518: Unknown option: {n}"; continue; }
            if (Options.Exists(part)) { Options.Set(part, "1"); continue; }
            if (part.StartsWith("no", StringComparison.Ordinal) && Options.Exists(part.Substring(2))) { Options.Set(part.Substring(2), ""); continue; }
            throw new InvalidOperationException("E518: Unknown option: " + part);
        }
    }

    void ExNormal(string keys, int a, int b, bool hasRange)
    {
        if (keys.Length == 0) return;
        int from = hasRange ? a : LineNumber(Point), to = hasRange ? b : LineNumber(Point);
        for (int n = from; n <= to && n < LineCount; n++)
        {
            if (hasRange) Point = LineStartOfNumber(n);
            SetKeymap("vi_nmap");
            _viKeys.Clear();
            foreach (var ch in keys) Dispatch(KeyInput.FromName(ch == ' ' ? "SP" : ch.ToString()));
            if (Keymap == "vi_imap") ViLeaveInsert();
            if (Keymap is "vi_xmap" or "vi_smap") { MarkActive = false; MarkKind = MarkKind.None; SetKeymap("vi_nmap"); }
            _viKeys.Clear();
        }
    }

    void Substitute(string arg, int a, int b)
    {
        if (arg.Length == 0)
        {
            if (_viLastSubstitute == null) throw new InvalidOperationException("E35: No previous regular expression");
            arg = _viLastSubstitute;
        }
        char delim = arg[0];
        if (char.IsLetterOrDigit(delim) || delim == '\\' || delim == '"' || delim == '|')
            throw new InvalidOperationException("E146: Regular expressions can't be delimited by letters");
        var parts = SplitDelimited(arg.Substring(1), delim);
        var pat = parts.Count > 0 ? parts[0] : "";
        var rep = parts.Count > 1 ? parts[1] : "";
        var flags = parts.Count > 2 ? parts[2] : "";
        if (pat.Length == 0) pat = _viSearch;
        if (pat.Length == 0) throw new InvalidOperationException("E35: No previous regular expression");
        _viLastSubstitute = delim + pat + delim + rep + delim + flags;
        _viSearch = pat;
        var opts = RegexOptions.Multiline;
        if (flags.Contains('i')) opts |= RegexOptions.IgnoreCase;
        var re = new Regex(VimRegexToNet(pat), opts);
        var replacement = VimReplacementToNet(rep);
        bool global = flags.Contains('g');
        int changed = 0, lastLine = a;
        for (int n = b; n >= a; n--)
        {
            int s = LineStartOfNumber(n), e = LineEnd(s);
            var line = Text.Substring(s, e - s);
            if (!re.IsMatch(line)) continue;
            var nl = global ? re.Replace(line, replacement) : re.Replace(line, replacement, 1);
            Splice(s, e, nl);
            changed++;
            if (changed == 1) lastLine = n;
        }
        if (changed == 0) throw new InvalidOperationException("E486: Pattern not found: " + pat);
        Point = TextOps.FirstNonBlank(Text, LineStartOfNumber(Math.Min(lastLine, LineCount - 1)));
    }

    static List<string> SplitDelimited(string s, char delim)
    {
        var parts = new List<string>();
        var cur = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                if (s[i + 1] == delim) { cur.Append(delim); i++; continue; }
                cur.Append(s[i]).Append(s[i + 1]); i++; continue;
            }
            if (s[i] == delim) { parts.Add(cur.ToString()); cur.Clear(); continue; }
            cur.Append(s[i]);
        }
        parts.Add(cur.ToString());
        return parts;
    }

    /// <summary>Converts a vim "magic" regular expression to .NET syntax.</summary>
    public static string VimRegexToNet(string p)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < p.Length; i++)
        {
            char c = p[i];
            if (c == '\\' && i + 1 < p.Length)
            {
                char n = p[++i];
                switch (n)
                {
                    case '(': case ')': case '|': case '{': case '}': case '+': case '?':
                        sb.Append(n); break;
                    case '=': sb.Append('?'); break;
                    case '<': case '>': sb.Append(@"\b"); break;
                    default: sb.Append('\\').Append(n); break;
                }
                continue;
            }
            if ("(){}+?|".Contains(c)) { sb.Append('\\').Append(c); continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Converts a vim replacement string (&amp;, \1, \n) to a .NET replacement pattern.</summary>
    public static string VimReplacementToNet(string r)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < r.Length; i++)
        {
            char c = r[i];
            if (c == '\\' && i + 1 < r.Length)
            {
                char n = r[++i];
                if (char.IsDigit(n)) sb.Append("${").Append(n).Append('}');
                else if (n is 'n' or 'r') sb.Append('\n');
                else if (n == 't') sb.Append('\t');
                else if (n == '$') sb.Append("$$");
                else sb.Append(n);
                continue;
            }
            if (c == '&') { sb.Append("$0"); continue; }
            if (c == '$') { sb.Append("$$"); continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
