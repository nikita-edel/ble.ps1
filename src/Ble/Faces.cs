using System.Management.Automation;

namespace Ble;

/// <summary>ble-face: named display attributes, e.g. "fg=green,bold".</summary>
public static class Faces
{
    // Defaults are ble.sh's own (lib/core-syntax-def.sh, lib/core-complete.sh, src/edit.sh ...).
    // Faces marked "ble.ps1" have no ble.sh counterpart (PowerShell-only syntax or ble.ps1 UI).
    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        ["syntax_default"] = "none",
        ["syntax_command"] = "fg=brown",
        ["syntax_quoted"] = "fg=green",
        ["syntax_quotation"] = "fg=green,bold",
        ["syntax_escape"] = "fg=magenta",
        ["syntax_expr"] = "fg=33",
        ["syntax_error"] = "bg=203,fg=231",
        ["syntax_varname"] = "fg=202",
        ["syntax_delimiter"] = "bold",
        ["syntax_param_expansion"] = "fg=133",
        ["syntax_history_expansion"] = "bg=94,fg=231",
        ["syntax_function_name"] = "fg=99,bold",
        ["syntax_comment"] = "fg=242",
        ["syntax_glob"] = "fg=198,bold",
        ["syntax_brace"] = "fg=37,bold",
        ["syntax_tilde"] = "fg=63,bold",
        ["syntax_document"] = "fg=100",
        ["syntax_document_begin"] = "fg=100,bold",
        ["syntax_number"] = "none",                 // ble.ps1
        ["syntax_type"] = "none",                   // ble.ps1
        ["syntax_member"] = "none",                 // ble.ps1
        ["syntax_label"] = "ref:syntax_varname",    // ble.ps1
        ["command_builtin_dot"] = "fg=red,bold",
        ["command_builtin"] = "fg=red",
        ["command_alias"] = "fg=teal",
        ["command_function"] = "fg=99",
        ["command_file"] = "fg=green",
        ["command_keyword"] = "fg=blue",
        ["command_jobs"] = "fg=red,bold",
        ["command_directory"] = "fg=33,underline",
        ["command_suffix"] = "fg=231,bg=28",
        ["command_suffix_new"] = "fg=231,bg=124",
        ["filename_directory"] = "underline,fg=33",
        ["filename_directory_sticky"] = "underline,fg=231,bg=26",
        ["filename_link"] = "underline,fg=teal",
        ["filename_orphan"] = "underline,fg=16,bg=224",
        ["filename_executable"] = "underline,fg=green",
        ["filename_setuid"] = "underline,fg=black,bg=220",
        ["filename_setgid"] = "underline,fg=black,bg=191",
        ["filename_other"] = "underline",
        ["filename_socket"] = "underline,fg=cyan,bg=black",
        ["filename_pipe"] = "underline,fg=lime,bg=black",
        ["filename_character"] = "underline,fg=231,bg=black",
        ["filename_block"] = "underline,fg=yellow,bg=black",
        ["filename_warning"] = "underline,fg=red",
        ["filename_url"] = "underline,fg=blue",
        ["filename_ls_colors"] = "underline",
        ["varname_array"] = "fg=orange,bold",
        ["varname_empty"] = "fg=31",
        ["varname_export"] = "fg=200,bold",
        ["varname_expr"] = "fg=99,bold",
        ["varname_hash"] = "fg=70,bold",
        ["varname_number"] = "fg=64",
        ["varname_readonly"] = "fg=200",
        ["varname_transform"] = "fg=29,bold",
        ["varname_unset"] = "fg=245",
        ["varname_new"] = "fg=34",
        ["argument_option"] = "fg=teal",
        ["argument_error"] = "fg=black,bg=225",
        ["auto_complete"] = "bg=254,fg=238",
        ["cmdinfo_cd_cdpath"] = "fg=26,bg=155",
        ["region"] = "bg=60,fg=231",
        ["region_target"] = "bg=153,fg=black",
        ["region_match"] = "bg=55,fg=231",
        ["region_insert"] = "fg=27,bg=254",
        ["disabled"] = "fg=242",
        ["overwrite_mode"] = "fg=black,bg=51",
        ["menu_complete_match"] = "bold",
        ["menu_complete_selected"] = "reverse",
        ["menu_desc_default"] = "none",
        ["menu_desc_type"] = "ref:syntax_delimiter",
        ["menu_desc_quote"] = "ref:syntax_quoted",
        ["menu_filter_fixed"] = "bold",
        ["menu_filter_input"] = "fg=16,bg=229",
        ["menu_desc"] = "ref:menu_desc_default",   // ble.ps1
        ["menu_info"] = "fg=242",                  // ble.ps1
        ["list_view_selected"] = "ref:menu_complete_selected",   // ble.ps1: selected entry of the list view
        ["list_view_selected_color"] = "fg=33,bold",              // ble.ps1: the same with complete_auto_view_selected=color
        ["prompt_status_line"] = "fg=231,bg=240",
        ["prompt_top"] = "none", ["prompt_above"] = "none",       // ble.ps1: the prompt_{top,above,below,bottom}_*
        ["prompt_below"] = "none", ["prompt_bottom"] = "none",    // rows (the whole row, like a bar)
        ["prompt_ps2"] = "none",                   // ble.ps1
        ["status_line"] = "bold",                  // ble.ps1: vi mode line
        ["status_info"] = "fg=242",                // ble.ps1
        ["status_error"] = "fg=red",               // ble.ps1
        ["vbell"] = "reverse",
        ["vbell_erase"] = "bg=252",
        ["vbell_flash"] = "reverse,fg=green",
    };

    static readonly Dictionary<string, string> Specs = new(Defaults);
    static readonly List<string> Order = Defaults.Keys.ToList();
    static readonly Dictionary<string, string> Cache = new();

    static readonly Dictionary<string, int> ColorNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // ble.sh names (src/color.sh) first; the rest are ble.ps1 extras
        ["black"] = 0, ["brown"] = 1, ["maroon"] = 1, ["green"] = 2, ["olive"] = 3, ["navy"] = 4, ["purple"] = 5, ["teal"] = 6, ["silver"] = 7,
        ["gray"] = 8, ["grey"] = 8, ["red"] = 9, ["lime"] = 10, ["yellow"] = 11, ["blue"] = 12, ["fuchsia"] = 13, ["magenta"] = 13,
        ["aqua"] = 14, ["cyan"] = 14, ["white"] = 15, ["orange"] = 202, ["pink"] = 218,
        ["darkred"] = 88, ["darkgreen"] = 22, ["darkblue"] = 18, ["darkcyan"] = 30, ["darkmagenta"] = 90, ["darkyellow"] = 136, ["darkgray"] = 238,
    };

    public static IEnumerable<string> Names => Order;
    public static string? GetSpec(string name) => Specs.TryGetValue(name, out var s) ? s : null;

    public static void Set(string name, string spec)
    {
        spec = spec.Trim();
        // copy:FACE (and the obsolete face:FACE) take the other face's current value; ref:FACE follows it
        foreach (var p in new[] { "copy:", "face:" })
            if (spec.StartsWith(p, StringComparison.Ordinal))
                spec = GetSpec(spec.Substring(p.Length)) ?? throw new FormatException($"ble-face: face '{spec.Substring(p.Length)}' not found");
        ToSgr(spec); // validate
        if (!Specs.ContainsKey(name)) Order.Add(name);
        Specs[name] = spec;
        Cache.Clear();
    }

    public static void Reset()
    {
        foreach (var name in Order.Where(n => !Defaults.ContainsKey(n)).ToList()) { Specs.Remove(name); Order.Remove(name); }
        foreach (var kv in Defaults) Specs[kv.Key] = kv.Value;
        Cache.Clear();
    }

    /// <summary>ble-face -r: restores the default of one face.</summary>
    public static void ResetFace(string name)
    {
        if (Defaults.TryGetValue(name, out var d)) Specs[name] = d;
        Cache.Clear();
    }

    /// <summary>Face names matching a name or a ble.sh pattern (*, ? and @ as wildcards). Throws if none match.</summary>
    public static List<string> Match(string pattern)
    {
        if (pattern.IndexOfAny(['*', '?', '@']) < 0)
            return Specs.ContainsKey(pattern) ? [pattern] : throw new ArgumentException($"ble-face: face '{pattern}' not found");
        var wp = new WildcardPattern(pattern.Replace('@', '*'), WildcardOptions.IgnoreCase);
        var list = Order.Where(n => wp.IsMatch(n)).ToList();
        return list.Count > 0 ? list : throw new ArgumentException($"ble-face: face '{pattern}' not found");
    }

    /// <summary>Applies one ble.sh ble-face setting: FACE=SPEC (FACE may be a pattern or a comma list) or FACE:=SPEC (define).</summary>
    public static bool Apply(string arg)
    {
        var m = System.Text.RegularExpressions.Regex.Match(arg, @"^([_a-zA-Z0-9@*?,]+)(:?=)(.*)$", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (!m.Success) return false;
        foreach (var pat in m.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (m.Groups[2].Value == ":=") Set(pat, m.Groups[3].Value);
            else foreach (var n in Match(pat)) Set(n, m.Groups[3].Value);
        }
        return true;
    }

    public static string Sgr(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        if (Cache.TryGetValue(name, out var v)) return v;
        v = "";
        if (Specs.TryGetValue(name, out var spec)) { try { v = ToSgr(spec); } catch { v = ""; } }
        Cache[name] = v;
        return v;
    }

    static string ColorSgr(string spec, bool bg)
    {
        spec = spec.Trim();
        int b = bg ? 40 : 30;
        if (spec is "default" or "none" or "-1" or "transparent") return (b + 9).ToString();
        if (spec.StartsWith('#'))
        {
            var hex = spec.Substring(1);
            if (hex.Length == 3) hex = string.Concat(hex.Select(c => new string(c, 2)));
            if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
                return $"{b + 8};2;{(rgb >> 16) & 255};{(rgb >> 8) & 255};{rgb & 255}";
            throw new FormatException($"ble-face: bad color '{spec}'");
        }
        if (spec.Contains(':'))
        {
            var (r, g, bl) = ParseColorFunction(spec);
            return $"{b + 8};2;{r};{g};{bl}";
        }
        if (!ColorNames.TryGetValue(spec, out var n) && !int.TryParse(spec, out n))
            throw new FormatException($"ble-face: unknown color '{spec}'");
        if (n < 0 || n > 255) throw new FormatException($"ble-face: color out of range '{spec}'");
        if (n < 8) return (b + n).ToString();
        if (n < 16) return (b + 60 + n - 8).ToString();
        return $"{b + 8};5;{n}";
    }

    // ble.sh src/color.sh ble/color/.name2color: rgb:R/G/B, cmy:C/M/Y, cmyk:C/M/Y/K, hsl:H/S%/L%, hsb:/hsv:H/S%/B%
    static (int R, int G, int B) ParseColorFunction(string spec)
    {
        int colon = spec.IndexOf(':');
        var kind = spec.Substring(0, colon).ToLowerInvariant();
        var parts = spec.Substring(colon + 1).Split('/');
        int Clamp(string t, int max)
        {
            bool pct = t.EndsWith('%');
            if (!int.TryParse(pct ? t[..^1] : t, out var v) || v < 0) throw new FormatException($"ble-face: bad color '{spec}'");
            v = pct ? v * max / 100 : v;
            return Math.Min(v, max);
        }
        switch (kind)
        {
            case "rgb" when parts.Length == 3:
                return (Clamp(parts[0], 255), Clamp(parts[1], 255), Clamp(parts[2], 255));
            case "cmy" when parts.Length == 3:
            case "cmyk" when parts.Length == 4:
                {
                    int c = Clamp(parts[0], 255), m = Clamp(parts[1], 255), y = Clamp(parts[2], 255);
                    int k = 255 - (parts.Length == 4 ? Clamp(parts[3], 255) : 0);
                    return ((255 - c) * k / 255, (255 - m) * k / 255, (255 - y) * k / 255);
                }
            case "hsl" or "hsb" or "hsv" when parts.Length == 3 && parts[1].EndsWith('%') && parts[2].EndsWith('%'):
                {
                    if (!int.TryParse(parts[0], out var h)) throw new FormatException($"ble-face: bad color '{spec}'");
                    h = ((h % 360) + 360) % 360;
                    int s = Clamp(parts[1], 1000), x = Clamp(parts[2], 1000);
                    int range, min;
                    if (kind == "hsl") { range = 2 * (x <= 500 ? x : 1000 - x) * s / 1000; min = x - range / 2; }
                    else { range = x * s / 1000; min = x - range; }
                    int h1 = h % 120, h2 = 120 - h1;
                    int a = min + range * Math.Min(h2, 60) / 60, bb = min + range * Math.Min(h1, 60) / 60, z = min;
                    a = a * 255 / 1000; bb = bb * 255 / 1000; z = z * 255 / 1000;
                    return (h / 120) switch { 0 => (a, bb, z), 1 => (z, a, bb), _ => (bb, z, a) };
                }
        }
        throw new FormatException($"ble-face: bad color '{spec}'");
    }

    public static string ToSgr(string spec, int depth = 0)
    {
        if (string.IsNullOrWhiteSpace(spec) || depth > 8) return "";
        spec = spec.Trim();
        // other ble.sh spec types
        if (spec.StartsWith("gspec:", StringComparison.Ordinal)) spec = spec.Substring(6);
        else if (spec.StartsWith("sgrspec:", StringComparison.Ordinal))
            return spec.Length > 8 ? "\x1b[" + spec.Substring(8) + "m" : "";
        else if (spec.StartsWith("ansi:", StringComparison.Ordinal))
            return spec.Substring(5).Replace(@"\e", "\x1b").Replace(@"\E", "\x1b");
        else if (spec.StartsWith("g:", StringComparison.Ordinal))
            throw new FormatException("ble-face: 'g:' (internal attribute numbers) is not supported");
        var codes = new List<string>();
        foreach (var part in spec.Split(','))
        {
            var p = part.Trim();
            if (p.Length == 0) continue;
            if (p == "none") { codes.Clear(); continue; }   // ble.sh: none resets what came before
            if (p.StartsWith("ref:", StringComparison.Ordinal))
            {
                if (Specs.TryGetValue(p.Substring(4), out var r))
                {
                    var inner = ToSgr(r, depth + 1);
                    if (inner.Length > 3) codes.Add(inner.Substring(2, inner.Length - 3));
                }
                continue;
            }
            if (p.StartsWith("fg=", StringComparison.Ordinal)) { codes.Add(ColorSgr(p.Substring(3), false)); continue; }
            if (p.StartsWith("bg=", StringComparison.Ordinal)) { codes.Add(ColorSgr(p.Substring(3), true)); continue; }
            codes.Add(p switch
            {
                "bold" => "1",
                "dim" or "faint" => "2",
                "italic" => "3",
                "underline" => "4",
                "blink" => "5",
                "reverse" => "7",
                "standout" => "1;7",                  // ble.sh: bold + reverse
                "invis" => "8",
                "strike" => "9",
                "overline" => "53",
                _ => throw new FormatException($"ble-face: unknown attribute '{p}'"),
            });
        }
        if (codes.Count == 0) return "";
        return "\x1b[" + string.Join(';', codes) + "m";
    }
}
