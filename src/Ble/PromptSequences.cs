using System.Management.Automation;
using System.Text;

namespace Ble;

public sealed partial class Editor
{
    /// <summary>
    /// Expands bash/ble.sh prompt sequences in a prompt option (\w \u \h \t \e \n \q{...} ...).
    /// ScriptBlock values are evaluated instead.
    /// </summary>
    public string ExpandPrompt(object? raw)
    {
        if (raw is PSObject pso) raw = pso.BaseObject;
        if (raw is ScriptBlock sb) return Shell.Evaluate(sb);
        var s = raw as string ?? raw?.ToString() ?? "";
        if (s.IndexOf('\\') < 0) return s;
        var o = new StringBuilder();
        var now = DateTime.Now;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != '\\' || i + 1 >= s.Length) { o.Append(c); continue; }
            char n = s[++i];
            switch (n)
            {
                case 'a': o.Append('\a'); break;
                case 'e': o.Append('\x1b'); break;
                case 'n': o.Append('\n'); break;
                case 'r': o.Append('\r'); break;
                case '\\': o.Append('\\'); break;
                case '[': case ']': break;
                case 'd': o.Append(now.ToString("ddd MMM dd")); break;
                case 't': o.Append(now.ToString("HH:mm:ss")); break;
                case 'T': o.Append(now.ToString("hh:mm:ss")); break;
                case '@': o.Append(now.ToString("hh:mm tt")); break;
                case 'A': o.Append(now.ToString("HH:mm")); break;
                case 'u': o.Append(Environment.UserName); break;
                case 'h': o.Append(Environment.MachineName.Split('.')[0]); break;
                case 'H': o.Append(Environment.MachineName); break;
                case 's': o.Append("pwsh"); break;
                case 'v': o.Append(typeof(PSObject).Assembly.GetName().Version?.ToString(2)); break;
                case 'V': o.Append(typeof(PSObject).Assembly.GetName().Version?.ToString(3)); break;
                case 'w': o.Append(TildePath(Shell.CurrentDirectory)); break;
                case 'W':
                    {
                        var p = TildePath(Shell.CurrentDirectory);
                        var name = p == "~" ? "~" : Path.GetFileName(p.TrimEnd('\\', '/'));
                        o.Append(name.Length == 0 ? p : name);
                        break;
                    }
                case '#': case '!': o.Append(History.Count + 1); break;
                case 'j': o.Append('0'); break;
                case 'l': break;
                case '$': o.Append(IsElevated() ? '#' : '$'); break;
                case 'D':
                    if (i + 1 < s.Length && s[i + 1] == '{')
                    {
                        int close = s.IndexOf('}', i + 2);
                        if (close > 0)
                        {
                            var fmt = s.Substring(i + 2, close - i - 2);
                            o.Append(FormatStrftime(now, fmt.Length == 0 ? "%X" : fmt));
                            i = close;
                            break;
                        }
                    }
                    o.Append("\\D");
                    break;
                case 'q':
                    if (i + 1 < s.Length && s[i + 1] == '{')
                    {
                        int close = s.IndexOf('}', i + 2);
                        if (close > 0)
                        {
                            o.Append(PromptQ(s.Substring(i + 2, close - i - 2)));
                            i = close;
                            break;
                        }
                    }
                    o.Append("\\q");
                    break;
                default:
                    if (n >= '0' && n <= '7' && i + 2 < s.Length && s[i + 1] is >= '0' and <= '7' && s[i + 2] is >= '0' and <= '7')
                    {
                        o.Append((char)Convert.ToInt32(s.Substring(i, 3), 8));
                        i += 2;
                    }
                    else o.Append('\\').Append(n);
                    break;
            }
        }
        return o.ToString();
    }

    string PromptQ(string name) => name switch
    {
        "keymap:vi/mode-indicator" => ModeName(),
        "keymap:emacs/mode-indicator" => EmacsModeName(),
        "keymap:vi/mode-name" => Keymap switch { "vi_imap" => "insert", "vi_nmap" => "normal", "vi_xmap" => "visual", "vi_smap" => "select", _ => Keymap },
        "keymap" => Keymap,
        _ => "",
    };

    static string TildePath(string p)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 0 && p.StartsWith(home, StringComparison.OrdinalIgnoreCase)
            && (p.Length == home.Length || p[home.Length] is '\\' or '/'))
            return "~" + p.Substring(home.Length);
        return p;
    }

    static bool IsElevated()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            return Environment.UserName == "root";
        }
        catch { return false; }
    }

    static string FormatStrftime(DateTime t, string fmt)
    {
        if (fmt.IndexOf('%') < 0) return t.ToString(fmt);
        var o = new StringBuilder();
        for (int i = 0; i < fmt.Length; i++)
        {
            if (fmt[i] != '%' || i + 1 >= fmt.Length) { o.Append(fmt[i]); continue; }
            char c = fmt[++i];
            o.Append(c switch
            {
                'Y' => t.ToString("yyyy"), 'y' => t.ToString("yy"), 'm' => t.ToString("MM"), 'd' => t.ToString("dd"),
                'e' => t.Day.ToString().PadLeft(2), 'H' => t.ToString("HH"), 'I' => t.ToString("hh"), 'M' => t.ToString("mm"),
                'S' => t.ToString("ss"), 'p' => t.ToString("tt"), 'a' => t.ToString("ddd"), 'A' => t.ToString("dddd"),
                'b' => t.ToString("MMM"), 'h' => t.ToString("MMM"), 'B' => t.ToString("MMMM"), 'j' => t.DayOfYear.ToString("000"),
                'X' => t.ToString("HH:mm:ss"), 'T' => t.ToString("HH:mm:ss"), 'R' => t.ToString("HH:mm"), 'x' => t.ToString("d"),
                'D' => t.ToString("MM/dd/yy"), 'F' => t.ToString("yyyy-MM-dd"), 's' => new DateTimeOffset(t).ToUnixTimeSeconds().ToString(),
                'n' => "\n", 't' => "\t", '%' => "%",
                _ => "%" + c,
            });
        }
        return o.ToString();
    }

    PanelLine? StatusLine(int width)
    {
        var raw = Options.GetRaw("prompt_status_line");
        if (raw == null || (raw is string rs && rs.Length == 0)) return null;
        var text = ExpandPrompt(raw).Replace("\r", "").Replace('\n', ' ');
        int avail = Math.Max(1, width - 1);
        int tw = TextWidth.StringWidth(text);
        int pad = Options.Get("prompt_status_align") switch
        {
            "right" => Math.Max(0, avail - tw),
            "center" => Math.Max(0, (avail - tw) / 2),
            _ => 0,
        };
        var sgr = Faces.Sgr("prompt_status_line");
        var line = new PanelLine();
        line.Add(new string(' ', pad) + text + new string(' ', Math.Max(0, avail - pad - tw)), sgr);
        return line;
    }
}
