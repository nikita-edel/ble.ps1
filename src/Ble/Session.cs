using System.Diagnostics;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Text;

namespace Ble;

/// <summary>Process-wide state and the entry points called from ble.psm1.</summary>
public static class Session
{
    static Editor? _editor;
    static readonly Stopwatch Elapsed = new();
    static bool _execPending;
    static bool _execNative;

    public static readonly History History = new();
    public static string DefaultHistoryFile { get; set; } = "";
    public static string? PromptText { get; private set; }
    public static string? LastAcceptDirectory { get; private set; }
    public static Exception? LastError { get; private set; }
    public static Editor? Editor => _editor;

    public static string Version => typeof(Session).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Called by PSConsoleHostReadLine.</summary>
    public static string ReadLine(EngineIntrinsics? engine, bool lastRunStatus)
    {
        if (Console.IsInputRedirected)
            return Console.ReadLine() ?? "exit";
        try
        {
            EnsureHistory();
            var shell = new PsShell(engine);
            if (_editor == null) _editor = new Editor(new ConsoleTerminal(), shell, History);
            else _editor.SetShell(shell);
        }
        catch (Exception ex)
        {
            LastError = ex;
            return Console.ReadLine() ?? "exit";
        }
        try
        {
            _editor.AboveRows = _aboveRows;
            _editor.AbovePromptLine = _abovePromptLine;
            _aboveRows = 0;
            return _editor.ReadLine();
        }
        catch (Exception ex)
        {
            LastError = ex;
            try { Console.Write($"\x1b[0m\r\n\x1b[91m[ble: internal error: {ex.GetType().Name}: {ex.Message}] (falling back to plain input; see [Ble.Session]::LastError)\x1b[m\r\n"); }
            catch { }
            return Console.ReadLine() ?? "";
        }
    }

    public static void EnsureHistory()
    {
        var path = Options.Get("history_file");
        if (string.IsNullOrEmpty(path)) path = DefaultHistoryFile;
        History.MaxSize = Math.Max(1, Options.GetInt("history_size", 10000));
        if (!string.Equals(History.FilePath ?? "", path ?? "", StringComparison.Ordinal))
            History.Load(path);
    }

    internal static void MarkExecStart(string line, Editor e)
    {
        _execPending = true;
        LastAcceptDirectory = e.Shell.CurrentDirectory;
        _execNative = false;
        try
        {
            var ast = Parser.ParseInput(line, out _, out _);
            foreach (var ca in ast.FindAll(n => n is CommandAst, true).Cast<CommandAst>())
            {
                var name = ca.GetCommandName();
                if (name == null) continue;
                var kind = e.Highlighter.ResolveCommandKind(name);
                if (kind is CommandKind.Application or CommandKind.Script) { _execNative = true; break; }
            }
        }
        catch { }
        Elapsed.Restart();
    }

    static string FormatElapsed(TimeSpan t)
    {
        if (t.TotalSeconds < 60) return $"{t.TotalSeconds:0.000}s";
        if (t.TotalHours < 1) return $"{(int)t.TotalMinutes}m{t.Seconds:00}.{t.Milliseconds / 100}s";
        return $"{(int)t.TotalHours}h{t.Minutes:00}m{t.Seconds:00}s";
    }

    /// <summary>
    /// Elapsed-time threshold in ms, or -1 if disabled. ble.sh's exec_elapsed_enabled ('usr+sys>=10000')
    /// wins when it was changed; ble.ps1 measures wall time, so only its number is used.
    /// </summary>
    internal static long ElapsedThreshold()
    {
        var en = Options.Get("exec_elapsed_enabled");
        if (en != (string)Options.Defaults["exec_elapsed_enabled"])
        {
            var m = System.Text.RegularExpressions.Regex.Match(en, @">=?\s*(\d+)");
            return m.Success && long.TryParse(m.Groups[1].Value, out var n) ? n : -1;
        }
        var t = Options.Get("exec_elapsed_threshold");
        return t.Length > 0 && long.TryParse(t, out var ms) ? ms : -1;
    }

    // first %s: the elapsed time; a second %s (ble.sh's CPU usage) is not measured; %% is a percent sign
    internal static string FormatElapsedMark(string mark, string elapsed)
    {
        var sb = new StringBuilder();
        int n = 0;
        for (int i = 0; i < mark.Length; i++)
        {
            if (mark[i] == '%' && i + 1 < mark.Length)
            {
                char c = mark[i + 1];
                if (c == 's') { sb.Append(n++ == 0 ? elapsed : "-"); i++; continue; }
                if (c == '%') { sb.Append('%'); i++; continue; }
            }
            sb.Append(mark[i]);
        }
        return sb.ToString();
    }

    /// <summary>Called by the prompt wrapper before the user's prompt runs. Prints status marks.</summary>
    public static void BeforePrompt(bool lastStatus, object? lastExitCode)
    {
        try
        {
            var sb = new StringBuilder();
            if (_execPending)
            {
                _execPending = false;
                Elapsed.Stop();
                if (!lastStatus)
                {
                    int code = 0;
                    var v = lastExitCode is PSObject p ? p.BaseObject : lastExitCode;
                    if (v is int i) code = i;
                    else if (v != null) int.TryParse(v.ToString(), out code);
                    var mark = _execNative && code != 0
                        ? Options.Get("exec_errexit_mark").Replace("%d", code.ToString())
                        : Options.Get("exec_error_mark");
                    if (mark.Length > 0) sb.Append(mark).Append("\x1b[m\r\n");
                }
                long threshold = ElapsedThreshold();
                var em = Options.Get("exec_elapsed_mark");
                if (threshold >= 0 && em.Length > 0 && Elapsed.ElapsedMilliseconds >= threshold)
                    sb.Append(FormatElapsedMark(em, FormatElapsed(Elapsed.Elapsed))).Append("\x1b[m\r\n");
            }
            var ruler = Options.Get("prompt_ruler");
            if (ruler.Length > 0 && !Console.IsOutputRedirected)
            {
                int w = Math.Max(1, (OperatingSystem.IsWindows() ? Console.BufferWidth : Console.WindowWidth) - 1);
                var plain = TextWidth.StripAnsi(ruler);
                var line = new StringBuilder();
                while (TextWidth.StringWidth(line.ToString()) + Math.Max(1, TextWidth.StringWidth(plain)) <= w) line.Append(plain);
                sb.Append(Faces.Sgr("disabled")).Append(line).Append("\x1b[m\r\n");
            }
            if (sb.Length > 0) { Console.Out.Write(sb.ToString()); Console.Out.Flush(); }
            MakeRoomAtTop();
        }
        catch { }
    }

    /// <summary>
    /// Called by the prompt wrapper with the prompt function's output; returns the prompt to print.
    /// Inserts empty rows before the prompt's last line for the rows at position 'above'.
    /// </summary>
    public static object? AfterPrompt(object? output)
    {
        var v = output is PSObject p ? p.BaseObject : output;
        var s = v as string;
        _aboveRows = 0;
        _abovePromptLine = "";
        int n = s != null ? Editor.RowsWanted("above") : 0;
        if (n > 0)
        {
            int nl = s!.LastIndexOf('\n');
            _abovePromptLine = s.Substring(nl + 1);
            s = s.Substring(0, nl + 1) + new string('\n', n) + _abovePromptLine;
            _aboveRows = n;
            output = s;
        }
        PromptText = s;
        return output;
    }

    static int _aboveRows;
    static string _abovePromptLine = "";

    // rows docked to the top of the window cover the first rows: start the prompt below them
    static void MakeRoomAtTop()
    {
        int top = Editor.RowsWanted("top");
        if (top == 0 || Console.IsOutputRedirected) return;
        int row = Console.CursorTop - (OperatingSystem.IsWindows() ? Console.WindowTop : 0);
        if (row < top)
        {
            Console.Out.Write(new string('\n', top - row));
            Console.Out.Flush();
        }
    }

    /// <summary>Clears per-session state (used by tests and ble-reload).</summary>
    public static void ResetForTests()
    {
        _editor = null;
        _execPending = false;
        PromptText = null;
        _aboveRows = 0;
    }
}
