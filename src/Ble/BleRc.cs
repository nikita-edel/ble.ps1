using System.Text;
using System.Text.RegularExpressions;

namespace Ble;

/// <summary>
/// Loads a ble.sh configuration file (~/.blerc) written in bash. Only the ble.sh configuration
/// commands are carried out; everything else is bash and is skipped with a warning:
///   bleopt, ble-face (and the old ble-color-setface/defface), ble-bind (-f, -s, -m, --cursor),
///   ble-sabbrev, ble-import, set -o vi|emacs, bind 'set editing-mode ...', source FILE,
///   variable assignments (used by later $VAR / ${VAR:-x} expansions), function definitions
///   (run when called or by blehook/eval-after-load keymap_vi|keymap_emacs), { ...; } groups,
///   and &amp;&amp; / || chains. if/case/for/while blocks, [[ ]] tests and command substitutions
///   cannot be evaluated and are skipped. ble-bind -x/-c (bash commands) are skipped too.
/// </summary>
public sealed class BleRc
{
    public readonly List<string> Warnings = new();
    /// <summary>Number of configuration commands that were applied.</summary>
    public int Applied { get; private set; }

    readonly Dictionary<string, List<Node>> _functions = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> _vars = new(StringComparer.Ordinal);
    string _file = "";
    string? _hookKeymap;
    int _depth;

    // ---- entry points ----

    /// <summary>~/.blerc, then ${XDG_CONFIG_HOME:-~/.config}/blesh/init.sh (ble.sh's search order).</summary>
    public static string? FindDefault()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(xdg)) xdg = Path.Combine(home, ".config");
        foreach (var p in new[] { Path.Combine(home, ".blerc"), Path.Combine(xdg, "blesh", "init.sh") })
            if (File.Exists(p)) return p;
        return null;
    }

    public void LoadFile(string path)
    {
        if (_depth > 8) { Warn(0, $"'{path}': too many nested source/ble-import calls"); return; }
        var saved = _file;
        _file = path;
        _depth++;
        try { Run(File.ReadAllText(path)); }
        finally { _depth--; _file = saved; }
    }

    int _runDepth;

    public void Run(string script)
    {
        List<Node> nodes;
        try
        {
            var p = new ParserState(Lex(script.Replace("\r\n", "\n")));
            nodes = ParseList(p);
            if (p.Pos < p.Toks.Count) Warn(p.Toks[p.Pos].Line, $"unexpected '{p.Toks[p.Pos].Text}'; the rest of the file was skipped");
        }
        catch (FormatException ex) { Warn(0, ex.Message); return; }
        _runDepth++;
        try { foreach (var n in nodes) Exec(n); }
        finally { _runDepth--; }
        if (_runDepth == 0) RunDeferred();
    }

    void Warn(int line, string msg)
    {
        var where = _file.Length > 0 ? Path.GetFileName(_file) + (line > 0 ? ":" + line : "") + ": " : line > 0 ? "line " + line + ": " : "";
        Warnings.Add(where + msg);
    }

    // ---- lexer ----

    sealed class Tok
    {
        public bool IsWord;
        public string Text = "";
        public bool Quoted;              // any quoting or escape: not a reserved word
        public string Raw = "";          // source text; expanded again when the command runs
        public string? Unsupported;      // command substitution etc.
        public int Line;
        public bool Op(string s) => !IsWord && Text == s;
        public bool Word(string s) => IsWord && !Quoted && Text == s;
    }

    List<Tok> Lex(string s)
    {
        var toks = new List<Tok>();
        int i = 0, line = 1, n = s.Length;
        while (i < n)
        {
            char c = s[i];
            if (c is ' ' or '\t' or '\r') { i++; continue; }
            if (c == '\\' && i + 1 < n && s[i + 1] == '\n') { i += 2; line++; continue; }
            if (c == '\n') { toks.Add(new Tok { Text = "\n", Line = line }); line++; i++; continue; }
            if (c == '#') { while (i < n && s[i] != '\n') i++; continue; }
            if (c is ';' or '&' or '|')
            {
                string op = i + 1 < n && s[i + 1] == c ? new string(c, 2) : c.ToString();
                toks.Add(new Tok { Text = op, Line = line });
                i += op.Length;
                continue;
            }
            if (c is '(' or ')' or '<' or '>') { toks.Add(new Tok { Text = c.ToString(), Line = line }); i++; continue; }
            toks.Add(LexWord(s, ref i, ref line));
        }
        return toks;
    }

    Tok LexWord(string s, ref int i, ref int line)
    {
        var t = new Tok { IsWord = true, Line = line };
        int start = i;
        var sb = new StringBuilder();
        int n = s.Length;
        while (i < n)
        {
            char c = s[i];
            if (c is ' ' or '\t' or '\r' or '\n' or ';' or '&' or '|' or '(' or ')' or '<' or '>') break;
            if (c == '\\')
            {
                if (i + 1 < n && s[i + 1] == '\n') { i += 2; line++; continue; }
                if (i + 1 < n) sb.Append(s[i + 1]);
                i += 2; t.Quoted = true;
                continue;
            }
            if (c == '\'')
            {
                int e = s.IndexOf('\'', i + 1);
                if (e < 0) throw new FormatException($"line {line}: unterminated quote");
                var lit = s.Substring(i + 1, e - i - 1);
                line += lit.Count(ch => ch == '\n');
                sb.Append(lit);
                i = e + 1; t.Quoted = true;
                continue;
            }
            if (c == '$' && i + 1 < n && s[i + 1] == '\'')
            {
                i += 2;
                sb.Append(AnsiC(s, ref i, ref line));
                t.Quoted = true;
                continue;
            }
            if (c == '"')
            {
                i++;
                t.Quoted = true;
                while (true)
                {
                    if (i >= n) throw new FormatException($"line {line}: unterminated double quote");
                    char d = s[i];
                    if (d == '"') { i++; break; }
                    if (d == '\\' && i + 1 < n && s[i + 1] is '$' or '`' or '"' or '\\' or '\n')
                    {
                        if (s[i + 1] == '\n') line++;
                        else sb.Append(s[i + 1]);
                        i += 2;
                        continue;
                    }
                    if (d == '$') { sb.Append(Expand(s, ref i, t)); continue; }
                    if (d == '`') { SkipBackquote(s, ref i, t); continue; }
                    if (d == '\n') line++;
                    sb.Append(d);
                    i++;
                }
                continue;
            }
            if (c == '$') { sb.Append(Expand(s, ref i, t)); continue; }
            if (c == '`') { SkipBackquote(s, ref i, t); continue; }
            if (c == '~' && sb.Length == 0 && !t.Quoted && (i + 1 >= n || s[i + 1] is '/' or ' ' or '\t' or '\n' or ';' or ':'))
            {
                sb.Append(Var("HOME"));
                i++;
                continue;
            }
            sb.Append(c);
            i++;
        }
        t.Text = sb.ToString();
        t.Raw = s.Substring(start, i - start);
        return t;
    }

    static void SkipBackquote(string s, ref int i, Tok t)
    {
        int e = s.IndexOf('`', i + 1);
        i = e < 0 ? s.Length : e + 1;
        t.Unsupported ??= "command substitution `...`";
    }

    // bash $'...' strings
    static string AnsiC(string s, ref int i, ref int line)
    {
        var sb = new StringBuilder();
        int n = s.Length;
        while (true)
        {
            if (i >= n) throw new FormatException($"line {line}: unterminated $'...'");
            char c = s[i];
            if (c == '\'') { i++; return sb.ToString(); }
            if (c == '\n') line++;
            if (c != '\\' || i + 1 >= n) { sb.Append(c); i++; continue; }
            char e = s[i + 1];
            i += 2;
            switch (e)
            {
                case 'a': sb.Append('\a'); break;
                case 'b': sb.Append('\b'); break;
                case 'e': case 'E': sb.Append('\x1b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'v': sb.Append('\v'); break;
                case '\\': case '\'': case '"': case '?': sb.Append(e); break;
                case 'c':
                    if (i < n) { sb.Append((char)(char.ToUpperInvariant(s[i]) & 0x1f)); i++; }
                    break;
                case 'x': sb.Append(Code(s, ref i, 2, 16)); break;
                case 'u': sb.Append(Code(s, ref i, 4, 16)); break;
                case 'U': sb.Append(Code(s, ref i, 8, 16)); break;
                case >= '0' and <= '7':
                    i--;
                    sb.Append(Code(s, ref i, 3, 8));
                    break;
                default: sb.Append('\\').Append(e); break;
            }
        }
    }

    static string Code(string s, ref int i, int max, int radix)
    {
        int start = i;
        while (i < s.Length && i - start < max && Uri.IsHexDigit(s[i]) && (radix == 16 || s[i] <= '7')) i++;
        if (i == start) return "";
        int v = Convert.ToInt32(s.Substring(start, i - start), radix);
        return v <= 0x10FFFF ? char.ConvertFromUtf32(v >= 0xD800 && v <= 0xDFFF ? 0xFFFD : v) : "";
    }

    string Var(string name)
    {
        if (_vars.TryGetValue(name, out var v)) return v;
        var env = Environment.GetEnvironmentVariable(name);
        if (env != null) return env;
        return name switch
        {
            "HOME" => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "PWD" => Environment.CurrentDirectory,
            _ => "",
        };
    }

    // $NAME, ${NAME}, ${NAME:-word}, ${NAME-word}, ${NAME:=word}, ${NAME=word}, ${#NAME}; $(...) is not supported
    string Expand(string s, ref int i, Tok t)
    {
        int n = s.Length;
        i++; // '$'
        if (i >= n) return "$";
        char c = s[i];
        if (c == '(')
        {
            int depth = 0;
            for (; i < n; i++)
            {
                if (s[i] == '(') depth++;
                else if (s[i] == ')' && --depth == 0) { i++; break; }
            }
            t.Unsupported ??= "command substitution $(...)";
            return "";
        }
        if (c == '{')
        {
            int e = s.IndexOf('}', i);
            if (e < 0) throw new FormatException($"line {t.Line}: unterminated ${{");
            var body = s.Substring(i + 1, e - i - 1);
            i = e + 1;
            if (body.StartsWith('#')) return Var(body.Substring(1)).Length.ToString();
            var m = Regex.Match(body, @"^([A-Za-z_][A-Za-z0-9_]*)(?:(:?)([-=+])(.*))?$", RegexOptions.Singleline);
            if (!m.Success) { t.Unsupported ??= "${" + body + "}"; return ""; }
            var name = m.Groups[1].Value;
            var val = Var(name);
            if (!m.Groups[3].Success) return val;
            bool colon = m.Groups[2].Value == ":";
            bool unset = colon ? val.Length == 0 : !_vars.ContainsKey(name) && Environment.GetEnvironmentVariable(name) == null && name is not ("HOME" or "PWD");
            var word = m.Groups[4].Value;
            if (word.StartsWith('~')) word = Var("HOME") + word.Substring(1);
            switch (m.Groups[3].Value)
            {
                case "-": return unset ? word : val;
                case "=": if (unset) { _vars[name] = word; return word; } return val;
                default: return unset ? "" : word;   // +
            }
        }
        if (char.IsLetter(c) || c == '_')
        {
            int st = i;
            while (i < n && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
            return Var(s.Substring(st, i - st));
        }
        if (char.IsDigit(c) || c is '@' or '*' or '#' or '?' or '-' or '$' or '!') { i++; return ""; }
        return "$";
    }

    // ---- parser ----

    abstract class Node { public int Line; }
    sealed class Simple : Node { public List<Tok> Words = new(); }
    sealed class Group : Node { public List<Node> Body = new(); }
    sealed class FuncDef : Node { public string Name = ""; public List<Node> Body = new(); }
    sealed class Skip : Node { public string Reason = ""; }
    sealed class Chain : Node { public List<Node> Parts = new(); public List<string> Ops = new(); }

    sealed class ParserState
    {
        public readonly List<Tok> Toks;
        public int Pos;
        public ParserState(List<Tok> toks) { Toks = toks; }
        public Tok? Cur => Pos < Toks.Count ? Toks[Pos] : null;
        public Tok? Peek(int k) => Pos + k < Toks.Count ? Toks[Pos + k] : null;
    }

    static void SkipSeparators(ParserState p)
    {
        while (p.Cur is { IsWord: false } t && t.Text is "\n" or ";" or "&") p.Pos++;
    }

    static void SkipNewlines(ParserState p)
    {
        while (p.Cur is { IsWord: false, Text: "\n" }) p.Pos++;
    }

    List<Node> ParseList(ParserState p, string? stop = null)
    {
        var list = new List<Node>();
        while (true)
        {
            SkipSeparators(p);
            var t = p.Cur;
            if (t == null) break;
            if (stop != null && t.Word(stop)) break;
            if (t.Op(")")) break;
            list.Add(ParseAndOr(p));
        }
        return list;
    }

    Node ParseAndOr(ParserState p)
    {
        var first = ParseCommand(p);
        if (p.Cur is not { IsWord: false } op || op.Text is not ("&&" or "||" or "|")) return first;
        var chain = new Chain { Line = first.Line };
        chain.Parts.Add(first);
        while (p.Cur is { IsWord: false } o && o.Text is "&&" or "||" or "|")
        {
            p.Pos++;
            SkipNewlines(p);
            chain.Ops.Add(o.Text);
            chain.Parts.Add(ParseCommand(p));
        }
        return chain;
    }

    Node ParseCommand(ParserState p)
    {
        var t = p.Cur!;
        int line = t.Line;
        if (t.Op("("))
        {
            SkipBalanced(p, "(", ")");
            return new Skip { Line = line, Reason = "subshell ( ... )" };
        }
        if (t.IsWord && !t.Quoted)
        {
            switch (t.Text)
            {
                case "{":
                    {
                        p.Pos++;
                        var body = ParseList(p, "}");
                        Expect(p, "}", line);
                        return new Group { Line = line, Body = body };
                    }
                case "function":
                    {
                        p.Pos++;
                        var name = p.Cur is { IsWord: true } nt ? nt.Text : throw new FormatException($"line {line}: function name expected");
                        p.Pos++;
                        if (p.Cur?.Op("(") == true && p.Peek(1)?.Op(")") == true) p.Pos += 2;
                        return ParseFunctionBody(p, name, line);
                    }
                case "if": return SkipCompound(p, "if", "fi");
                case "case": return SkipCompound(p, "case", "esac");
                case "for": case "while": case "until": case "select": return SkipCompound(p, t.Text, "done");
                case "[[":
                    while (p.Cur != null && !p.Cur.Word("]]")) p.Pos++;
                    if (p.Cur != null) p.Pos++;
                    return new Skip { Line = line, Reason = "test [[ ... ]]" };
            }
            // name() { ... }
            if (p.Peek(1)?.Op("(") == true && p.Peek(2)?.Op(")") == true)
            {
                p.Pos += 3;
                return ParseFunctionBody(p, t.Text, line);
            }
        }
        var cmd = new Simple { Line = line };
        while (p.Cur != null)
        {
            var w = p.Cur;
            if (w.IsWord) { cmd.Words.Add(w); p.Pos++; continue; }
            if (w.Text is "<" or ">")
            {
                // redirections: drop the operator and its target (e.g. >/dev/null)
                p.Pos++;
                if (p.Cur?.Op("&") == true) p.Pos++;
                if (p.Cur is { IsWord: true }) p.Pos++;
                continue;
            }
            break;
        }
        return cmd;
    }

    Node ParseFunctionBody(ParserState p, string name, int line)
    {
        SkipNewlines(p);
        if (p.Cur?.Word("{") != true)
        {
            if (p.Cur?.Op("(") == true) SkipBalanced(p, "(", ")");
            return new Skip { Line = line, Reason = $"function '{name}' (only {{ ... }} bodies are read)" };
        }
        p.Pos++;
        var body = ParseList(p, "}");
        Expect(p, "}", line);
        return new FuncDef { Line = line, Name = name, Body = body };
    }

    static void Expect(ParserState p, string word, int line)
    {
        if (p.Cur?.Word(word) != true) throw new FormatException($"line {line}: '{word}' expected");
        p.Pos++;
    }

    static void SkipBalanced(ParserState p, string open, string close)
    {
        int depth = 0;
        while (p.Cur != null)
        {
            if (p.Cur.Op(open)) depth++;
            else if (p.Cur.Op(close) && --depth == 0) { p.Pos++; return; }
            p.Pos++;
        }
    }

    static readonly Dictionary<string, string> Closers = new()
    {
        ["if"] = "fi", ["case"] = "esac", ["for"] = "done", ["while"] = "done", ["until"] = "done", ["select"] = "done",
    };

    static Node SkipCompound(ParserState p, string kw, string close)
    {
        int line = p.Cur!.Line;
        var stack = new Stack<string>();
        stack.Push(close);
        p.Pos++;
        while (p.Cur != null && stack.Count > 0)
        {
            var t = p.Cur;
            if (t.IsWord && !t.Quoted)
            {
                if (Closers.TryGetValue(t.Text, out var c)) stack.Push(c);
                else if (t.Text == stack.Peek()) stack.Pop();
            }
            p.Pos++;
        }
        return new Skip { Line = line, Reason = $"bash '{kw}' block" };
    }

    // ---- execution ----

    enum Status { Ok, Failed, Unsupported }

    Status Exec(Node n)
    {
        switch (n)
        {
            case Simple s: return ExecSimple(s);
            case Group g:
                {
                    var st = Status.Ok;
                    foreach (var x in g.Body) st = Exec(x);
                    return st;
                }
            case FuncDef f:
                _functions[f.Name] = f.Body;
                return Status.Ok;
            case Skip k:
                Warn(k.Line, $"skipped {k.Reason}: ble.ps1 cannot run bash code");
                return Status.Unsupported;
            case Chain c:
                {
                    if (c.Ops.Contains("|")) { Warn(c.Line, "skipped a pipeline: ble.ps1 cannot run bash code"); return Status.Unsupported; }
                    var st = Exec(c.Parts[0]);
                    for (int i = 0; i < c.Ops.Count; i++)
                    {
                        if (st == Status.Unsupported)
                        {
                            Warn(c.Line, "skipped the rest of an && / || list (its condition is bash)");
                            return st;
                        }
                        bool run = c.Ops[i] == "&&" ? st == Status.Ok : st == Status.Failed;
                        if (run) st = Exec(c.Parts[i + 1]);
                    }
                    return st;
                }
        }
        return Status.Ok;
    }

    static readonly Regex AssignRx = new(@"^([A-Za-z_][A-Za-z0-9_]*)(\+?)=(.*)$", RegexOptions.Singleline);

    void Assign(string word, int line)
    {
        var m = AssignRx.Match(word);
        if (!m.Success) return;
        var name = m.Groups[1].Value;
        var val = m.Groups[3].Value;
        if (name.StartsWith("bleopt_", StringComparison.Ordinal))
        {
            // old style configuration: bleopt_name=value
            Try(line, () => Options.Apply(name.Substring(7) + (m.Groups[2].Value == "+" ? "+=" : "=") + val));
            return;
        }
        _vars[name] = m.Groups[2].Value == "+" && _vars.TryGetValue(name, out var old) ? old + val : val;
    }

    bool Try(int line, Action a)
    {
        try { a(); Applied++; return true; }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            Warn(line, ex.Message);
            return false;
        }
    }

    Status ExecSimple(Simple s)
    {
        if (s.Words.Count == 0) return Status.Ok;
        int line = s.Line;
        // expand now (not while lexing), so $VAR sees assignments made by earlier commands;
        // leading assignments (VAR=value [cmd ...]) take effect one by one
        var w = new List<string>();
        foreach (var word in s.Words)
        {
            int i = 0, l = word.Line;
            var t = LexWord(word.Raw, ref i, ref l);
            if (t.Unsupported != null)
            {
                Warn(line, $"skipped '{s.Words[0].Text}': {t.Unsupported} is not supported");
                return Status.Unsupported;
            }
            if (w.Count == 0 && AssignRx.IsMatch(t.Text)) { Assign(t.Text, line); continue; }
            w.Add(t.Text);
        }
        if (w.Count == 0) return Status.Ok;
        var cmd = w[0];
        var args = w.Skip(1).ToList();
        switch (cmd)
        {
            case "bleopt":
                {
                    bool ok = true;
                    foreach (var a in args.Where(a => !a.StartsWith('-')))
                        if (a.Contains('=')) ok &= Try(line, () => { if (!Options.Apply(a)) throw new ArgumentException($"bleopt: unrecognized argument '{a}'"); });
                    return ok ? Status.Ok : Status.Failed;
                }
            case "ble-face":
                return ExecFace(args, line);
            case "ble-color-setface":
            case "ble-color-defface":
                if (args.Count < 2) { Warn(line, $"{cmd}: FACE SPEC expected"); return Status.Failed; }
                return Try(line, () => Faces.Set(args[0], args[1])) ? Status.Ok : Status.Failed;
            case "ble-bind":
                return ExecBind(args, line);
            case "ble-sabbrev":
                return ExecSabbrev(args, line);
            case "ble-import":
                return ExecImport(args, line);
            case "blehook/eval-after-load":
                {
                    if (args.Count < 2) { Warn(line, "blehook/eval-after-load: NAME COMMAND expected"); return Status.Failed; }
                    // all keymaps are loaded in ble.ps1, so the hook runs now
                    var saved = _hookKeymap;
                    _hookKeymap = args[0] switch { "keymap_vi" => "vi_imap", "keymap_emacs" => "emacs", _ => _hookKeymap };
                    try { RunNested(string.Join(' ', args.Skip(1)), line); }
                    finally { _hookKeymap = saved; }
                    return Status.Ok;
                }
            case "set":
                {
                    for (int i = 0; i < args.Count; i++)
                        if (args[i] == "-o" && i + 1 < args.Count && args[i + 1] is "vi" or "emacs")
                        {
                            var mode = args[++i];
                            Try(line, () => Options.Set("default_keymap", mode));
                        }
                    return Status.Ok;
                }
            case "bind":
                {
                    foreach (var a in args)
                    {
                        var m = Regex.Match(a, @"^\s*set\s+editing-mode\s+(vi|emacs)\s*$");
                        if (m.Success) Try(line, () => Options.Set("default_keymap", m.Groups[1].Value));
                        else if (!a.StartsWith('-')) Warn(line, $"skipped readline setting 'bind {a}'");
                    }
                    return Status.Ok;
                }
            case "source":
            case ".":
                {
                    if (args.Count == 0) return Status.Failed;
                    var path = ResolvePath(args[0]);
                    if (!File.Exists(path)) { Warn(line, $"source: '{args[0]}' not found"); return Status.Failed; }
                    LoadFile(path);
                    return Status.Ok;
                }
            case "export": case "declare": case "typeset": case "local": case "readonly":
                foreach (var a in args) if (!a.StartsWith('-')) Assign(a, line);
                return Status.Ok;
            case "unset":
                foreach (var a in args) _vars.Remove(a);
                return Status.Ok;
            case "true": case ":": case "ble-attach": case "return":
                return Status.Ok;
            case "false":
                return Status.Failed;
        }
        if (_functions.TryGetValue(cmd, out var body))
        {
            if (_depth > 16) { Warn(line, $"'{cmd}': recursion too deep"); return Status.Failed; }
            _depth++;
            try
            {
                var st = Status.Ok;
                foreach (var n in body) st = Exec(n);
                return st;
            }
            finally { _depth--; }
        }
        Warn(line, $"skipped bash command '{cmd}'");
        return Status.Unsupported;
    }

    // blehook/eval-after-load: a function name or a short command string. A function that is
    // defined further down in the file runs once the whole file has been read.
    void RunNested(string script, int line)
    {
        var name = script.Trim();
        if (Regex.IsMatch(name, @"^[^\s;&|()<>]+$") && !_functions.ContainsKey(name) && !IsCommand(name))
        {
            _deferred.Add((name, _hookKeymap, line));
            return;
        }
        Run(script);
    }

    static bool IsCommand(string name) => name is "bleopt" or "ble-face" or "ble-bind" or "ble-sabbrev" or "ble-import" or "true" or ":";

    readonly List<(string Name, string? Keymap, int Line)> _deferred = new();

    void RunDeferred()
    {
        while (_deferred.Count > 0)
        {
            var (name, km, line) = _deferred[0];
            _deferred.RemoveAt(0);
            if (!_functions.ContainsKey(name)) { Warn(line, $"blehook/eval-after-load: function '{name}' is not defined"); continue; }
            var saved = _hookKeymap;
            _hookKeymap = km;
            try { Run(name); }
            finally { _hookKeymap = saved; }
        }
    }

    string ResolvePath(string p)
    {
        if (p.StartsWith('~')) p = Var("HOME") + p.Substring(1);
        if (!Path.IsPathRooted(p) && _file.Length > 0) p = Path.Combine(Path.GetDirectoryName(_file) ?? "", p);
        return p;
    }

    Status ExecFace(List<string> args, int line)
    {
        bool reset = false, ok = true;
        for (int i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a is "-s" or "-d")
            {
                if (i + 2 >= args.Count) { Warn(line, $"ble-face: missing option argument for '{a} FACE SPEC'"); return Status.Failed; }
                var name = args[++i];
                var spec = args[++i];
                ok &= Try(line, () => Faces.Apply(name + (a == "-d" ? ":=" : "=") + spec));
                continue;
            }
            if (a is "-r" or "--reset") { reset = true; continue; }
            if (a.StartsWith('-')) continue;   // --color, -u, ...
            if (a.Contains('='))
            {
                ok &= Try(line, () => { if (!Faces.Apply(a)) throw new ArgumentException($"ble-face: unrecognized setting '{a}'"); });
                continue;
            }
            if (reset) ok &= Try(line, () => { foreach (var n in Faces.Match(a)) Faces.ResetFace(n); });
        }
        return ok ? Status.Ok : Status.Failed;
    }

    string DefaultKeymap() => _hookKeymap ?? (Options.Get("default_keymap") == "vi" ? "vi_imap" : "emacs");

    Status ExecBind(List<string> args, int line)
    {
        string? km = null;
        bool ok = true;
        for (int i = 0; i < args.Count; i++)
        {
            var a = args[i];
            string? Next() => i + 1 < args.Count ? args[++i] : null;
            switch (a)
            {
                case "-m": km = Next(); continue;
                case "--cursor":
                    {
                        var v = Next();
                        if (v != null) ok &= SetCursor(km ?? DefaultKeymap(), v, line);
                        continue;
                    }
                case "-f": case "-s": case "-x": case "-c":
                    {
                        var keys = Next();
                        var action = Next();
                        if (keys == null || action == null) { Warn(line, $"ble-bind {a}: KEYS and an argument expected"); return Status.Failed; }
                        ok &= Bind(km ?? DefaultKeymap(), a, keys, action, line);
                        continue;
                    }
                case "-T":
                    i += 2;
                    Warn(line, "ble-bind -T (key timeouts) is not supported");
                    continue;
            }
            if (a.StartsWith("--cursor=", StringComparison.Ordinal)) { ok &= SetCursor(km ?? DefaultKeymap(), a.Substring(9), line); continue; }
            if (a.StartsWith("-m", StringComparison.Ordinal) && a.Length > 2) { km = a.Substring(2); continue; }
            if (a.StartsWith('-')) continue;   // -P, -L, -D, -k ...: printing
            Warn(line, $"ble-bind: unexpected argument '{a}'");
        }
        return ok ? Status.Ok : Status.Failed;
    }

    bool SetCursor(string keymap, string value, int line)
    {
        var opt = $"keymap_{keymap}_cursor";
        if (!Options.Exists(opt)) { Warn(line, $"ble-bind --cursor: not supported for keymap '{keymap}'"); return false; }
        return Try(line, () => Options.Set(opt, value));
    }

    bool Bind(string keymap, string kind, string keys, string action, int line)
    {
        if (!Keymaps.Exists(keymap))
        {
            // removing a binding from a keymap ble.ps1 does not have: there is nothing to remove
            if (kind == "-f" && action.Trim() is "" or "-") return true;
            Warn(line, $"ble-bind -m {keymap}: keymap not available in ble.ps1 (available: {string.Join(", ", Keymaps.Names)})");
            return false;
        }
        if (kind is "-x" or "-c")
        {
            Warn(line, $"skipped ble-bind {kind} '{keys}': the command is bash (use ble-bind {kind} with a PowerShell scriptblock instead)");
            return false;
        }
        return Try(line, () =>
        {
            var seq = KeyNames.ParseSpec(keys);
            var km = Keymaps.Get(keymap);
            if (kind == "-s")
            {
                KeyNames.ParseSpec(action);
                km.Bind(seq, new Binding(BindingKind.Macro, action));
                return;
            }
            var widget = action.Trim();
            if (widget.Length == 0 || widget == "-") { km.Bind(seq, null); return; }
            // ble.sh widgets may take arguments ('history-search-backward immediate-accept'); the name comes first
            widget = widget.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries)[0];
            if (widget.StartsWith("ble/widget/", StringComparison.Ordinal)) widget = widget.Substring(11);
            if (Editor.Widgets.ContainsKey(widget)) km.Bind(seq, Binding.W(widget));
            else if (Editor.UserWidgets.ContainsKey(widget)) km.Bind(seq, new Binding(BindingKind.UserWidget, widget));
            else if (_functions.ContainsKey("ble/widget/" + widget))
                throw new ArgumentException($"ble-bind '{keys}': widget '{widget}' is a bash function in this file, which ble.ps1 cannot run "
                    + "(define it in PowerShell with Register-BleWidget, or bind a key macro with ble-bind -s)");
            else throw new ArgumentException($"ble-bind: unknown widget '{widget}' (see ble-bind -L)");
        });
    }

    Status ExecSabbrev(List<string> args, int line)
    {
        bool ok = true;
        foreach (var a in args)
        {
            if (a == "-m") { Warn(line, "skipped ble-sabbrev -m: dynamic abbreviations are bash functions (use a scriptblock: ble-sabbrev NAME { ... })"); return Status.Unsupported; }
            if (a.StartsWith('-')) continue;   // -c, -w, -i, -l, -r ...
            int eq = a.IndexOf('=');
            if (eq <= 0) continue;
            var name = a.Substring(0, eq);
            var val = a.Substring(eq + 1);
            ok &= Try(line, () => Editor.Abbrevs[name] = val);
        }
        return ok ? Status.Ok : Status.Failed;
    }

    Status ExecImport(List<string> args, int line)
    {
        bool ok = true;
        foreach (var a in args)
        {
            if (a.StartsWith('-')) continue;   // -d (delay), -C, -q, -f
            switch (BleModules.TryImport(a))
            {
                case BleModules.Result.Imported:
                case BleModules.Result.ImportedNeedsFzf:
                    Applied++;
                    continue;
            }
            var path = ResolvePath(a);
            if (File.Exists(path) && !path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                LoadFile(path);
                continue;
            }
            Warn(line, $"ble-import: '{a}' is not available in ble.ps1");
            ok = false;
        }
        return ok ? Status.Ok : Status.Failed;
    }
}

/// <summary>The modules ble-import knows (ble.sh names; contrib/ and .sh are accepted).</summary>
public static class BleModules
{
    public enum Result { NotBuiltin, Imported, ImportedNeedsFzf }

    static readonly string[] FzfKeymaps = ["emacs", "vi_imap", "vi_nmap"];

    public static Result TryImport(string name)
    {
        var key = name.Trim().Replace("contrib/", "integration/");
        if (key.EndsWith(".sh", StringComparison.Ordinal)) key = key.Substring(0, key.Length - 3);
        switch (key)
        {
            case "keymap.vi": case "vi":
                Options.Set("default_keymap", "vi");
                return Result.Imported;
            case "keymap.emacs": case "emacs":
                Options.Set("default_keymap", "emacs");
                return Result.Imported;
            case "integration/fzf-key-bindings": case "fzf-key-bindings":
                BindFzfKeys();
                return Result.ImportedNeedsFzf;
            case "integration/fzf-completion": case "fzf-completion":
                Options.Set("complete_fzf_trigger", "**");
                return Result.ImportedNeedsFzf;
            case "integration/fzf": case "fzf":
                BindFzfKeys();
                Options.Set("complete_fzf_trigger", "**");
                return Result.ImportedNeedsFzf;
        }
        return Result.NotBuiltin;
    }

    static void BindFzfKeys()
    {
        foreach (var km in FzfKeymaps)
        {
            Keymaps.Get(km).Bind(KeyNames.ParseSpec("C-t"), Binding.W("fzf-file-widget"));
            Keymaps.Get(km).Bind(KeyNames.ParseSpec("M-c"), Binding.W("fzf-cd-widget"));
            Keymaps.Get(km).Bind(KeyNames.ParseSpec("C-r"), Binding.W("fzf-history-widget"));
        }
    }
}
