namespace Ble;

/// <summary>A decoded key press. Name uses ble.sh style names: 'a', 'C-x', 'M-f', 'RET', 'S-TAB', 'C-left'.</summary>
public readonly record struct KeyInput(string Name, char Char)
{
    /// <summary>True if this key inserts a character (no Control/Meta prefix).</summary>
    public bool IsPrintable => Char != '\0' && !Name.StartsWith("M-", StringComparison.Ordinal) && !Name.StartsWith("C-", StringComparison.Ordinal);

    public override string ToString() => Name;

    /// <summary>Builds a key from its canonical name (used for replay and tests).</summary>
    public static KeyInput FromName(string name)
    {
        if (name.Length == 1) return new KeyInput(name, name[0]);
        if (name.Length == 2 && char.IsSurrogatePair(name[0], name[1])) return new KeyInput(name, name[0]);
        if (name == "SP") return new KeyInput("SP", ' ');
        return new KeyInput(name, '\0');
    }
}

public static class KeyNames
{
    static readonly Dictionary<string, string> Named = new(StringComparer.Ordinal)
    {
        ["ret"] = "RET", ["enter"] = "RET", ["return"] = "RET", ["cr"] = "RET",
        ["tab"] = "TAB",
        ["esc"] = "ESC", ["escape"] = "ESC",
        ["sp"] = "SP", ["space"] = "SP", ["spc"] = "SP",
        ["backspace"] = "DEL", ["rubout"] = "DEL",
        ["delete"] = "delete", ["ins"] = "insert", ["insert"] = "insert",
        ["home"] = "home", ["end"] = "end",
        ["prior"] = "prior", ["pgup"] = "prior", ["pageup"] = "prior",
        ["next"] = "next", ["pgdn"] = "next", ["pagedown"] = "next",
        ["up"] = "up", ["down"] = "down", ["left"] = "left", ["right"] = "right",
    };

    static readonly System.Text.RegularExpressions.Regex PlusModifier =
        new(@"^(ctrl|control|alt|meta|shift)\+(?=.)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    static readonly Dictionary<ConsoleKey, char> OemChars = new()
    {
        [ConsoleKey.Oem1] = ';', [ConsoleKey.OemPlus] = '=', [ConsoleKey.OemComma] = ',', [ConsoleKey.OemMinus] = '-',
        [ConsoleKey.OemPeriod] = '.', [ConsoleKey.Oem2] = '/', [ConsoleKey.Oem3] = '`', [ConsoleKey.Oem4] = '[',
        [ConsoleKey.Oem5] = '\\', [ConsoleKey.Oem6] = ']', [ConsoleKey.Oem7] = '\'', [ConsoleKey.Oem102] = '\\',
    };

    /// <summary>Canonical key name from modifiers and base name. Order of prefixes: C- M- S-.</summary>
    public static string Format(bool ctrl, bool alt, bool shift, string b)
    {
        if (ctrl)
        {
            if (b == "SP") b = "@";
            if (!shift)
            {
                switch (b)
                {
                    case "m": b = "RET"; ctrl = false; break;
                    case "i": b = "TAB"; ctrl = false; break;
                    case "[": b = "ESC"; ctrl = false; break;
                }
            }
        }
        // Shift is implicit for printable characters: S-f (or M-S-f) is the key that produces F (M-F)
        if (shift && !ctrl && (b.Length == 1 || b == "SP"))
        {
            if (b.Length == 1 && char.IsLetter(b[0])) b = b.ToUpperInvariant();
            shift = false;
        }
        var p = "";
        if (ctrl) p += "C-";
        if (alt) p += "M-";
        if (shift) p += "S-";
        return p + b;
    }

    /// <summary>Decodes a ConsoleKeyInfo. Returns null for keys that carry no meaning.</summary>
    public static KeyInput? FromConsoleKey(ConsoleKeyInfo k)
    {
        bool ctrl = (k.Modifiers & ConsoleModifiers.Control) != 0;
        bool alt = (k.Modifiers & ConsoleModifiers.Alt) != 0;
        bool shift = (k.Modifiers & ConsoleModifiers.Shift) != 0;
        char ch = k.KeyChar;
        int code = ch;
        var key = k.Key;

        // AltGr (reported as Ctrl+Alt) producing a printable character
        if (ctrl && alt && code >= 0x20 && code != 0x7F)
        {
            bool isLetterOfKey = key >= ConsoleKey.A && key <= ConsoleKey.Z && char.ToUpperInvariant(ch) == (char)('A' + (key - ConsoleKey.A));
            if (!isLetterOfKey) return new KeyInput(ch == ' ' ? "SP" : ch.ToString(), ch);
        }

        string? b = null;
        switch (key)
        {
            case ConsoleKey.Enter:
                if (code == 10 && !ctrl) return new KeyInput(alt ? "C-M-j" : "C-j", '\0');
                b = "RET"; break;
            case ConsoleKey.Tab: b = "TAB"; break;
            case ConsoleKey.Backspace:
                if (code == 8 && ctrl && !alt && !shift && OperatingSystem.IsWindows() == false) { b = "h"; }
                else b = "DEL";
                break;
            case ConsoleKey.Escape: b = "ESC"; break;
            case ConsoleKey.Delete: b = "delete"; break;
            case ConsoleKey.Insert: b = "insert"; break;
            case ConsoleKey.Home: b = "home"; break;
            case ConsoleKey.End: b = "end"; break;
            case ConsoleKey.PageUp: b = "prior"; break;
            case ConsoleKey.PageDown: b = "next"; break;
            case ConsoleKey.UpArrow: b = "up"; break;
            case ConsoleKey.DownArrow: b = "down"; break;
            case ConsoleKey.LeftArrow: b = "left"; break;
            case ConsoleKey.RightArrow: b = "right"; break;
            case ConsoleKey.Spacebar:
                if (ctrl) return new KeyInput(Format(true, alt, false, "@"), '\0');
                if (alt) return new KeyInput("M-SP", '\0');
                return new KeyInput("SP", ' ');
        }
        if (b == null && key >= ConsoleKey.F1 && key <= ConsoleKey.F24)
            b = "f" + (key - ConsoleKey.F1 + 1);
        if (b != null)
            return new KeyInput(Format(ctrl, alt, shift, b), '\0');

        if (ctrl)
        {
            if (key >= ConsoleKey.A && key <= ConsoleKey.Z)
                return new KeyInput(Format(true, alt, shift, ((char)('a' + (key - ConsoleKey.A))).ToString()), '\0');
            if (key >= ConsoleKey.D0 && key <= ConsoleKey.D9 && (code < 0x20 || char.IsDigit(ch)))
                return new KeyInput(Format(true, alt, false, ((char)('0' + (key - ConsoleKey.D0))).ToString()), '\0');
            string? cb = code switch
            {
                0 => "@",
                0x1B => "[",
                0x1C => "\\",
                0x1D => "]",
                0x1E => "^",
                0x1F => "_",
                0x7F => "?",
                _ => null,
            };
            if (code == 0 && OemChars.TryGetValue(key, out var oc)) cb = oc.ToString();
            else if (cb == null && code >= 0x20) cb = ch.ToString();
            else if (cb == null && code >= 1 && code <= 26) cb = ((char)(96 + code)).ToString();
            if (cb == null) return null;
            return new KeyInput(Format(true, alt, false, cb), '\0');
        }

        if (code == 0) return null;
        if (code < 0x20 || code == 0x7F)
        {
            string n = code switch
            {
                9 => "TAB",
                13 => "RET",
                27 => "ESC",
                0x7F => "DEL",
                0x1C => "C-\\",
                0x1D => "C-]",
                0x1E => "C-^",
                0x1F => "C-_",
                0 => "C-@",
                _ => "C-" + (char)(96 + code),
            };
            if (alt) n = n.StartsWith("C-", StringComparison.Ordinal) ? "C-M-" + n.Substring(2) : "M-" + n;
            return new KeyInput(n, '\0');
        }
        if (alt) return new KeyInput("M-" + ch, '\0');
        return new KeyInput(ch.ToString(), ch);
    }

    /// <summary>Parses one key token, e.g. 'C-x', 'M-DEL', 'S-TAB', 'Ctrl+a', 'up', 'a'.</summary>
    public static string ParseToken(string tok)
    {
        if (string.IsNullOrEmpty(tok)) throw new FormatException("empty key");
        bool ctrl = false, alt = false, shift = false;
        // PSReadLine style: Ctrl+Shift+a
        System.Text.RegularExpressions.Match pm;
        while ((pm = PlusModifier.Match(tok)).Success)
        {
            var m = pm.Groups[1].Value.ToLowerInvariant();
            if (m is "ctrl" or "control") ctrl = true;
            else if (m is "alt" or "meta") alt = true;
            else shift = true;
            tok = tok.Substring(pm.Length);
        }
        while (tok.Length > 2 && tok[1] == '-' && "CMSAs".Contains(tok[0]))
        {
            switch (tok[0])
            {
                case 'C': ctrl = true; break;
                case 'M': case 'A': alt = true; break;
                case 'S': shift = true; break;
            }
            tok = tok.Substring(2);
        }
        string b;
        if (tok.Length == 1 || (tok.Length == 2 && char.IsSurrogatePair(tok[0], tok[1])))
        {
            b = tok == " " ? "SP" : tok;
            if (ctrl && b.Length == 1 && char.IsUpper(b[0])) b = b.ToLowerInvariant();
        }
        else if (tok is "RET" or "TAB" or "ESC" or "SP" or "DEL")
            b = tok;
        else if (tok == "BS") { b = "h"; ctrl = true; }
        else if (tok == "NUL") { b = "@"; ctrl = true; }
        else if (Named.TryGetValue(tok.ToLowerInvariant(), out var nb)) b = nb;
        else if (tok.Length >= 2 && (tok[0] == 'f' || tok[0] == 'F') && int.TryParse(tok.AsSpan(1), out var fn) && fn >= 1 && fn <= 24)
            b = "f" + fn;
        else throw new FormatException($"unknown key name '{tok}'");
        return Format(ctrl, alt, shift, b);
    }

    /// <summary>'C-x C-e' -> ["C-x", "C-e"].</summary>
    public static string[] ParseSpec(string spec)
    {
        var parts = spec.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new FormatException("empty key specification");
        return parts.Select(ParseToken).ToArray();
    }
}
