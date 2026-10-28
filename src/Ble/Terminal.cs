using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Ble;

/// <summary>Abstraction over the console so the editor can be driven by tests.</summary>
public interface ITerminal
{
    ConsoleKeyInfo ReadKey();
    bool KeyAvailable { get; }
    void Write(string s);
    int Width { get; }
    int Height { get; }
    int CursorLeft { get; }
    int CursorTop { get; }
    bool TreatControlCAsInput { get; set; }
    /// <summary>Milliseconds clock used for idle processing.</summary>
    long Now { get; }
    /// <summary>Sleeps briefly while waiting for input.</summary>
    void Wait(int ms);
}

public sealed class ConsoleTerminal : ITerminal
{
    public ConsoleKeyInfo ReadKey() => Console.ReadKey(true);
    public bool KeyAvailable { get { bool a = Console.KeyAvailable; if (a) _signaled = 0; return a; } }

    public void Write(string s)
    {
        var o = Console.Out;
        o.Write(s);
        o.Flush();
    }

    public int Width
    {
        get
        {
            try
            {
                int w = OperatingSystem.IsWindows() ? Console.BufferWidth : Console.WindowWidth;
                return w > 0 ? w : 80;
            }
            catch { return 80; }
        }
    }

    public int Height
    {
        get { try { int h = Console.WindowHeight; return h > 0 ? h : 24; } catch { return 24; } }
    }

    public int CursorLeft { get { try { return Console.CursorLeft; } catch { return 0; } } }
    public int CursorTop { get { try { return Console.CursorTop; } catch { return 0; } } }

    public bool TreatControlCAsInput
    {
        get { try { return Console.TreatControlCAsInput; } catch { return false; } }
        set { try { Console.TreatControlCAsInput = value; } catch { } }
    }

    public long Now => Environment.TickCount64;

    // Thread.Sleep(10) sleeps a whole timer tick (~15 ms) on Windows, so a key pressed while idle waited
    // that long. Waiting on the console input handle returns as soon as input arrives.
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern IntPtr GetStdHandle(int nStdHandle);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    IntPtr _stdin;
    int _signaled;

    public void Wait(int ms)
    {
        if (OperatingSystem.IsWindows() && !Console.IsInputRedirected)
        {
            if (_stdin == IntPtr.Zero) _stdin = GetStdHandle(-10);   // STD_INPUT_HANDLE
            // the handle also signals non-key events (focus, mouse, key-up), which Console.KeyAvailable
            // removes; if it keeps signaling without a key, fall back to sleeping so this never spins
            if (_stdin != IntPtr.Zero && _stdin != new IntPtr(-1) && _signaled < 8)
            {
                _signaled = WaitForSingleObject(_stdin, (uint)ms) == 0 ? _signaled + 1 : 0;
                return;
            }
            _signaled = 0;
        }
        Thread.Sleep(ms);
    }
}

public static class TextWidth
{
    // Ranges of East Asian Wide/Fullwidth characters and wide emoji (inclusive pairs)
    static readonly int[] Wide =
    [
        0x1100, 0x115F, 0x231A, 0x231B, 0x2329, 0x232A, 0x23E9, 0x23EC, 0x23F0, 0x23F0, 0x23F3, 0x23F3,
        0x25FD, 0x25FE, 0x2614, 0x2615, 0x2648, 0x2653, 0x267F, 0x267F, 0x2693, 0x2693, 0x26A1, 0x26A1,
        0x26AA, 0x26AB, 0x26BD, 0x26BE, 0x26C4, 0x26C5, 0x26CE, 0x26CE, 0x26D4, 0x26D4, 0x26EA, 0x26EA,
        0x26F2, 0x26F3, 0x26F5, 0x26F5, 0x26FA, 0x26FA, 0x26FD, 0x26FD, 0x2705, 0x2705, 0x270A, 0x270B,
        0x2728, 0x2728, 0x274C, 0x274C, 0x274E, 0x274E, 0x2753, 0x2755, 0x2757, 0x2757, 0x2795, 0x2797,
        0x27B0, 0x27B0, 0x27BF, 0x27BF, 0x2B1B, 0x2B1C, 0x2B50, 0x2B50, 0x2B55, 0x2B55,
        0x2E80, 0x303E, 0x3041, 0x33FF, 0x3400, 0x4DBF, 0x4E00, 0x9FFF, 0xA000, 0xA4CF, 0xA960, 0xA97F,
        0xAC00, 0xD7A3, 0xF900, 0xFAFF, 0xFE10, 0xFE19, 0xFE30, 0xFE6F, 0xFF00, 0xFF60, 0xFFE0, 0xFFE6,
        0x16FE0, 0x16FE4, 0x17000, 0x18CFF, 0x1B000, 0x1B2FF, 0x1F004, 0x1F004, 0x1F0CF, 0x1F0CF,
        0x1F18E, 0x1F18E, 0x1F191, 0x1F19A, 0x1F200, 0x1F251, 0x1F300, 0x1F320, 0x1F32D, 0x1F335,
        0x1F337, 0x1F37C, 0x1F37E, 0x1F393, 0x1F3A0, 0x1F3CA, 0x1F3CF, 0x1F3D3, 0x1F3E0, 0x1F3F0,
        0x1F3F4, 0x1F3F4, 0x1F3F8, 0x1F43E, 0x1F440, 0x1F440, 0x1F442, 0x1F4FC, 0x1F4FF, 0x1F53D,
        0x1F54B, 0x1F54E, 0x1F550, 0x1F567, 0x1F57A, 0x1F57A, 0x1F595, 0x1F596, 0x1F5A4, 0x1F5A4,
        0x1F5FB, 0x1F64F, 0x1F680, 0x1F6C5, 0x1F6CC, 0x1F6CC, 0x1F6D0, 0x1F6D2, 0x1F6D5, 0x1F6D7,
        0x1F6DC, 0x1F6DF, 0x1F6EB, 0x1F6EC, 0x1F6F4, 0x1F6FC, 0x1F7E0, 0x1F7EB, 0x1F7F0, 0x1F7F0,
        0x1F90C, 0x1F93A, 0x1F93C, 0x1F945, 0x1F947, 0x1F9FF, 0x1FA70, 0x1FAFF, 0x20000, 0x2FFFD,
        0x30000, 0x3FFFD,
    ];

    static readonly Regex Ansi = new(
        @"\x1b\[[0-9;:?<=>]*[ -/]*[@-~]|\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)|\x1b[PX^_][^\x1b]*\x1b\\|\x1b[@-Z\\-_]|\x1b.",
        RegexOptions.Compiled);

    /// <summary>Display width of a code point. Control characters are drawn as ^X (width 2).</summary>
    public static int CharWidth(int cp)
    {
        if (cp < 0x20 || cp == 0x7F) return 2;
        if (cp < 0x300) return cp >= 0x80 && cp < 0xA0 ? 2 : 1;
        if (cp is 0x200B or 0x200C or 0x200D or 0x2060 or 0xFEFF) return 0;
        if ((cp >= 0xFE00 && cp <= 0xFE0F) || (cp >= 0xE0100 && cp <= 0xE01EF)) return 0;
        var cat = CharUnicodeInfo.GetUnicodeCategory(cp);
        if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format) return 0;
        if (cp < Wide[0]) return 1;
        int lo = 0, hi = Wide.Length / 2 - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (cp < Wide[2 * mid]) hi = mid - 1;
            else if (cp > Wide[2 * mid + 1]) lo = mid + 1;
            else return 2;
        }
        return 1;
    }

    public static string StripAnsi(string? s) => string.IsNullOrEmpty(s) ? "" : Ansi.Replace(s, "");

    /// <summary>Display width of a single-line string; ANSI sequences are ignored.</summary>
    public static int StringWidth(string? s)
    {
        s = StripAnsi(s);
        int w = 0;
        for (int i = 0; i < s.Length; i++)
        {
            int c = s[i];
            if (c >= 0x20 && c < 0x7F) { w++; continue; }
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { c = char.ConvertToUtf32(s[i], s[i + 1]); i++; }
            w += CharWidth(c);
        }
        return w;
    }

    /// <summary>Simulates printing text from column startCol. Returns rows advanced and final column.</summary>
    public static (int Rows, int Col) Measure(string? s, int width, int startCol = 0)
    {
        s = StripAnsi(s);
        if (width <= 0) width = 80;
        int row = 0, col = startCol;
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            if (ch == '\n') { row++; col = 0; continue; }
            if (ch == '\r') { col = 0; continue; }
            if (ch == '\t') { col = Math.Min(width - 1, col - col % 8 + 8); continue; }
            int c = ch;
            if (char.IsHighSurrogate(ch) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { c = char.ConvertToUtf32(ch, s[i + 1]); i++; }
            if (c < 0x20) continue;
            int w = c < 0x7F ? 1 : CharWidth(c);
            if (col + w > width) { row++; col = 0; }
            col += w;
            if (col >= width) { row++; col = 0; }
        }
        return (row, col);
    }

    /// <summary>Truncates a plain string to a display width, adding an ellipsis when cut.</summary>
    public static string Truncate(string s, int max)
    {
        if (max <= 0) return "";
        if (StringWidth(s) <= max) return s;
        var sb = new StringBuilder();
        int w = 0;
        for (int i = 0; i < s.Length; i++)
        {
            string str = s[i].ToString(); int c = s[i];
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length) { str = s.Substring(i, 2); c = char.ConvertToUtf32(s[i], s[i + 1]); i++; }
            int cw = c < 0x20 ? 0 : CharWidth(c);
            if (w + cw > max - 1) break;
            sb.Append(str); w += cw;
        }
        sb.Append('…');
        return sb.ToString();
    }

    public static string CommonPrefix(IReadOnlyList<string> items, bool ignoreCase)
    {
        if (items.Count == 0) return "";
        var p = items[0];
        var cmp = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var it in items)
        {
            int n = Math.Min(p.Length, it.Length), k = 0;
            while (k < n && string.Compare(p, k, it, k, 1, cmp) == 0) k++;
            p = p.Substring(0, k);
            if (p.Length == 0) break;
        }
        return p;
    }
}
