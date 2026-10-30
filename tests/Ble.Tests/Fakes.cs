using System.Management.Automation;
using System.Text;
using Ble;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace Ble.Tests;

public sealed class InputExhaustedException : Exception { }

/// <summary>Scriptable terminal. A null entry in the queue is a "pause": KeyAvailable is false until the editor idles once.</summary>
public sealed class FakeTerminal : ITerminal
{
    readonly Queue<ConsoleKeyInfo?> _q = new();
    public readonly StringBuilder Output = new();
    public int Width { get; set; } = 80;
    public int Height { get; set; } = 24;
    public int CursorLeft { get; set; }
    public int CursorTop { get; set; }
    public bool TreatControlCAsInput { get; set; }
    long _now = 1000;
    int _idle;
    public long Now => _now;

    public void Wait(int ms)
    {
        _now += ms;
        if (_q.Count > 0 && _q.Peek() == null) { _q.Dequeue(); return; }
        if (_q.Count == 0 && ++_idle > 2000) throw new InputExhaustedException();
    }

    public bool KeyAvailable => _q.Count > 0 && _q.Peek() != null;

    public ConsoleKeyInfo ReadKey()
    {
        while (_q.Count > 0 && _q.Peek() == null) _q.Dequeue();
        if (_q.Count == 0) throw new InputExhaustedException();
        _idle = 0;
        return _q.Dequeue()!.Value;
    }

    public void Write(string s) => Output.Append(s);

    public FakeTerminal Keys(params string[] names)
    {
        foreach (var n in names)
        {
            if (n == "<pause>") { _q.Enqueue(null); continue; }
            _q.Enqueue(KeyFactory.ToConsoleKey(n));
        }
        return this;
    }

    public FakeTerminal Type(string text)
    {
        foreach (var c in text) _q.Enqueue(KeyFactory.CharKey(c));
        return this;
    }

    public FakeTerminal Pause() { _q.Enqueue(null); return this; }
}

public static class KeyFactory
{
    public static ConsoleKeyInfo CharKey(char c)
    {
        var key = ConsoleKey.NoName;
        bool shift = false;
        if (c >= 'a' && c <= 'z') key = ConsoleKey.A + (c - 'a');
        else if (c >= 'A' && c <= 'Z') { key = ConsoleKey.A + (c - 'A'); shift = true; }
        else if (c >= '0' && c <= '9') key = ConsoleKey.D0 + (c - '0');
        else if (c == ' ') key = ConsoleKey.Spacebar;
        else if (c == '\n') return new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false);
        else if (c == '\t') return new ConsoleKeyInfo('\t', ConsoleKey.Tab, false, false, false);
        return new ConsoleKeyInfo(c, key, shift, false, false);
    }

    /// <summary>Inverse of KeyNames.FromConsoleKey for canonical names.</summary>
    public static ConsoleKeyInfo ToConsoleKey(string name)
    {
        bool ctrl = false, alt = false, shift = false;
        var b = name;
        while (b.Length > 2 && b[1] == '-' && "CMS".Contains(b[0]))
        {
            if (b[0] == 'C') ctrl = true; else if (b[0] == 'M') alt = true; else shift = true;
            b = b.Substring(2);
        }
        (char ch, ConsoleKey key) = b switch
        {
            "RET" => ('\r', ConsoleKey.Enter),
            "TAB" => ('\t', ConsoleKey.Tab),
            "ESC" => ('\x1b', ConsoleKey.Escape),
            "SP" => (' ', ConsoleKey.Spacebar),
            "DEL" => ('\b', ConsoleKey.Backspace),
            "delete" => ('\0', ConsoleKey.Delete),
            "insert" => ('\0', ConsoleKey.Insert),
            "home" => ('\0', ConsoleKey.Home),
            "end" => ('\0', ConsoleKey.End),
            "prior" => ('\0', ConsoleKey.PageUp),
            "next" => ('\0', ConsoleKey.PageDown),
            "up" => ('\0', ConsoleKey.UpArrow),
            "down" => ('\0', ConsoleKey.DownArrow),
            "left" => ('\0', ConsoleKey.LeftArrow),
            "right" => ('\0', ConsoleKey.RightArrow),
            _ => ('\0', ConsoleKey.NoName),
        };
        if (key == ConsoleKey.NoName && b.Length >= 2 && b[0] == 'f' && int.TryParse(b.AsSpan(1), out var fn)) key = ConsoleKey.F1 + (fn - 1);
        if (key == ConsoleKey.NoName)
        {
            if (b.Length != 1) throw new ArgumentException("cannot synthesize key " + name);
            char c = b[0];
            var ck = CharKey(c);
            key = ck.Key;
            ch = c;
            if (ctrl)
            {
                if (c >= 'a' && c <= 'z') ch = (char)(c - 'a' + 1);
                else ch = c switch { '@' => '\0', '[' => '\x1b', '\\' => '\x1c', ']' => '\x1d', '^' => '\x1e', '_' => '\x1f', _ => c };
                if (c == '_') key = ConsoleKey.OemMinus;
                if (c == ']') key = ConsoleKey.Oem6;
                if (c == '/') { key = ConsoleKey.Oem2; ch = '\0'; }
            }
            shift |= ck.Modifiers.HasFlag(ConsoleModifiers.Shift) && !alt;
            if (alt && !ctrl) shift = char.IsUpper(c);
        }
        return new ConsoleKeyInfo(ch, key, shift, alt, ctrl);
    }
}

public sealed class FakeShell : IShell
{
    public readonly Dictionary<string, CommandKind> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Get-ChildItem"] = CommandKind.Cmdlet, ["Get-Content"] = CommandKind.Cmdlet, ["Get-Command"] = CommandKind.Cmdlet,
        ["Where-Object"] = CommandKind.Cmdlet, ["ls"] = CommandKind.Alias, ["git"] = CommandKind.Application,
        ["prompt"] = CommandKind.Function, ["echo"] = CommandKind.Alias, ["Write-Output"] = CommandKind.Cmdlet,
    };
    public readonly HashSet<string> Variables = new(StringComparer.OrdinalIgnoreCase) { "HOME", "PWD", "x" };
    public Func<string, int, CompletionSet?>? Completer;
    public string Prompt = "PS> ";
    public string Cwd = Environment.CurrentDirectory;
    public readonly List<string> Ran = new();

    public int CompleteCalls;
    public CompletionSet? Complete(string text, int cursor, Func<bool>? cancel = null) { CompleteCalls++; return Completer?.Invoke(text, cursor); }
    public CommandKind ResolveCommand(string name) => Commands.TryGetValue(name, out var k) ? k : CommandKind.NotFound;
    public PathKind ResolvePath(string path) => FileSystemPaths.Resolve(path, Cwd);
    public readonly Dictionary<string, VarKind> VariableKinds = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string[]> Parameters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Get-ChildItem"] = ["Path", "LiteralPath", "Filter", "Recurse", "Force", "File", "Directory", "ErrorAction", "ea"],
        ["Where-Object"] = ["FilterScript", "Property", "Value", "EQ"],
    };
    public VarKind GetVariableKind(string name)
    {
        if (name.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
            return Environment.GetEnvironmentVariable(name.Substring(4)) != null ? VarKind.Export : VarKind.Unset;
        if (VariableKinds.TryGetValue(name, out var k)) return k;
        return Variables.Contains(name) ? VarKind.Normal : VarKind.Unset;
    }
    public readonly Dictionary<string, string> VariableTexts = new(StringComparer.OrdinalIgnoreCase);
    public string? GetVariableText(string name) => VariableTexts.TryGetValue(name, out var v) ? v : null;
    public IReadOnlyCollection<string>? GetParameterNames(string command) => Parameters.TryGetValue(command, out var p) ? p : null;
    public string? InvokePrompt() => Prompt;
    public string CurrentDirectory => Cwd;
    public void RunCommand(ScriptBlock sb) => Ran.Add(sb.ToString());
    public Func<string, int, int, (string, int, int)>? EditHandler;
    public (string Line, int Point, int Mark) RunEditCommand(ScriptBlock sb, string line, int point, int mark)
    {
        Ran.Add(sb.ToString());
        return EditHandler != null ? EditHandler(line, point, mark) : (line + "!", point + 1, mark);
    }
    public string Evaluate(ScriptBlock sb) => sb.ToString().Trim().Trim('\'', '"');

    /// <summary>Simple completer: completes the last word against a list of candidates.</summary>
    public void CompleteFrom(params string[] candidates)
    {
        Completer = (text, cursor) =>
        {
            int s = cursor;
            while (s > 0 && !char.IsWhiteSpace(text[s - 1])) s--;
            var w = text.Substring(s, cursor - s);
            var items = candidates.Where(c => c.StartsWith(w, StringComparison.OrdinalIgnoreCase))
                .Select(c => new CompletionItem(c, c, c, c.EndsWith('\\') ? CompletionResultType.ProviderContainer : CompletionResultType.Command)).ToList();
            return new CompletionSet(s, cursor - s, items);
        };
    }
}

/// <summary>Minimal VT100 screen used to check what the renderer draws.</summary>
public sealed class VtScreen
{
    public readonly int W, H;
    readonly List<char[]> _rows = new();
    public int Row, Col;
    bool _pendingWrap;

    public VtScreen(int w, int h = 200) { W = w; H = h; for (int i = 0; i < h; i++) _rows.Add(Blank()); }
    char[] Blank() { var a = new char[W]; Array.Fill(a, ' '); return a; }

    public void Feed(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\x1b')
            {
                if (i + 1 < s.Length && s[i + 1] == '[')
                {
                    int j = i + 2;
                    while (j < s.Length && (char.IsDigit(s[j]) || s[j] == ';' || s[j] == '?' || s[j] == ' ')) j++;
                    var param = s.Substring(i + 2, j - i - 2);
                    char fin = s[j];
                    Csi(param, fin);
                    i = j;
                }
                else
                {
                    if (i + 1 < s.Length && s[i + 1] == '7') _saved = (Row, Col);
                    if (i + 1 < s.Length && s[i + 1] == '8') { (Row, Col) = _saved; _pendingWrap = false; }
                    i++;
                }
                continue;
            }
            if (c == '\r') { Col = 0; _pendingWrap = false; continue; }
            if (c == '\n') { LineFeed(); _pendingWrap = false; continue; }
            if (c == '\a') continue;
            if (_pendingWrap) { LineFeed(); Col = 0; _pendingWrap = false; }
            int w = TextWidth.CharWidth(c);
            if (char.IsHighSurrogate(c) && i + 1 < s.Length) { w = TextWidth.CharWidth(char.ConvertToUtf32(c, s[i + 1])); i++; }
            if (w == 0) continue;
            if (Col + w > W) { LineFeed(); Col = 0; }
            _rows[Row][Col] = c;
            if (w == 2 && Col + 1 < W) _rows[Row][Col + 1] = '\0';
            Col += w;
            if (Col >= W) { Col = W - 1; _pendingWrap = true; }
        }
    }

    (int, int) _saved;
    public int Scrolled;   // rows scrolled off the top

    // at the last row the screen scrolls up
    void LineFeed()
    {
        if (Row < H - 1) { Row++; return; }
        _rows.RemoveAt(0);
        _rows.Add(Blank());
        Scrolled++;
    }

    void Csi(string p, char fin)
    {
        int n = int.TryParse(p.TrimStart('?'), out var v) ? v : 1;
        switch (fin)
        {
            case 'A': Row = Math.Max(0, Row - n); _pendingWrap = false; break;
            case 'B': Row = Math.Min(H - 1, Row + n); _pendingWrap = false; break;
            case 'C': Col = Math.Min(W - 1, Col + n); _pendingWrap = false; break;
            case 'D': Col = Math.Max(0, Col - n); _pendingWrap = false; break;
            case 'J':
                if (p == "" || p == "0")
                {
                    for (int c = Col; c < W; c++) _rows[Row][c] = ' ';
                    for (int r = Row + 1; r < H; r++) _rows[r] = Blank();
                }
                else if (p == "2") { for (int r = 0; r < H; r++) _rows[r] = Blank(); }
                break;
            case 'H':
                {
                    var rc = p.Split(';');
                    Row = Math.Clamp(rc[0].Length > 0 && int.TryParse(rc[0], out var r) ? r - 1 : 0, 0, H - 1);
                    Col = Math.Clamp(rc.Length > 1 && int.TryParse(rc[1], out var cc) ? cc - 1 : 0, 0, W - 1);
                    _pendingWrap = false;
                    break;
                }
            case 'K':
                for (int c = p == "2" ? 0 : Col; c < W; c++) _rows[Row][c] = ' ';
                break;
        }
    }

    public string Line(int r) => new string(_rows[r].Where(ch => ch != '\0').ToArray()).TrimEnd();

    public string[] Lines(int from, int count) => Enumerable.Range(from, count).Select(Line).ToArray();
}
