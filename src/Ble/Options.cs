using System.Management.Automation;
using System.Text;

namespace Ble;

/// <summary>bleopt: global editor options. Values are strings (or ScriptBlocks for prompt options).</summary>
public static class Options
{
    static readonly string E = "\x1b";

    public static readonly IReadOnlyDictionary<string, object> Defaults = new Dictionary<string, object>
    {
        ["default_keymap"] = "emacs",            // emacs | vi
        ["highlight_syntax"] = "1",
        ["highlight_filename"] = "1",
        ["highlight_variable"] = "1",
        ["highlight_incomplete"] = "1",          // mark unclosed strings and brackets as errors (ble.ps1)
        ["complete_auto_complete"] = "1",        // inline suggestions
        ["complete_auto_history"] = "1",         // suggestions from history
        ["complete_auto_history_cwd"] = "1",     // ble.ps1: prefer history entries that ran in the current directory
        ["complete_auto_history_validate"] = "command:path",   // ble.ps1: skip history entries with unknown commands / missing paths ('' = off)
        ["complete_auto_history_merge"] = "path:case",   // ble.ps1: list the same command once: path (/ \ ~ .\ trailing \), case ('' = off)
        ["complete_auto_history_fuzzy"] = "",    // ble.ps1: similar history entries (typos) when nothing else is suggested
        ["complete_auto_history_fuzzy_validate"] = "command:path",   // ble.ps1: complete_auto_history_validate for those
        ["complete_auto_completion"] = "1",      // suggestions from TabExpansion2 when idle
        ["complete_auto_delay"] = "100",         // ms idle before completion-based suggestions (ble.sh: 1; a key press stops a running completion)
        ["complete_auto_menu"] = "",             // ms idle before showing the menu ('' = off)
        ["complete_menu_style"] = "align",       // align | linewise | desc
        ["complete_ambiguous"] = "1",            // fall back to substring / fuzzy matching
        ["complete_fzf_trigger"] = "",           // e.g. '**': word ending with it opens fzf (ble-import integration/fzf-completion)
        ["complete_menu_maxlines"] = "10",
        ["menu_align_max"] = "32",
        ["edit_bell"] = "none",                  // none | audible | visible
        ["edit_paste_newline"] = "1",            // RET followed by queued input inserts a newline
        ["history_file"] = "",                   // '' = PSReadLine history file
        ["history_size"] = "10000",
        ["history_share"] = "1",
        ["history_ignore_space"] = "1",
        ["history_ignore_pattern"] = "password|asplaintext|token|apikey|secret",
        ["prompt_ps2"] = ">> ",
        ["prompt_rps1"] = "",
        ["prompt_ps1_final"] = "",
        ["prompt_ps1_transient"] = "",           // '' | always | same-dir | trim
        ["prompt_ruler"] = "",
        ["prompt_status_line"] = "",             // line drawn below the command line (prompt sequences allowed)
        ["prompt_status_align"] = "left",        // left | center | right
        ["prompt_xterm_title"] = "",             // terminal title set at each prompt
        ["prompt_vi_mode_indicator"] = @"\q{keymap:vi/mode-indicator}",   // text of the vi mode line
        // ble.ps1: where the mode line and prompt_status_line go: bottom | top (edges of the window),
        // below (under the input, above menus and lists) | above (row between the prompt and the input)
        ["prompt_mode_line_position"] = "bottom",
        ["prompt_status_line_position"] = "bottom",   // ble.sh docks the status line at the bottom
        ["prompt_input_left"] = "",              // ble.ps1: text in front of the input, after the prompt
        ["prompt_input_after"] = "",             // ble.ps1: text right after the input and its suggestion
        ["prompt_input_after_position"] = "after",   // ble.ps1: after = behind the suggestion, before = in front of it
        ["prompt_top_left"] = "", ["prompt_top_center"] = "", ["prompt_top_right"] = "",          // ble.ps1: rows like
        ["prompt_above_left"] = "", ["prompt_above_center"] = "", ["prompt_above_right"] = "",    // the status line, with
        ["prompt_below_left"] = "", ["prompt_below_center"] = "", ["prompt_below_right"] = "",    // left/center/right parts,
        ["prompt_bottom_left"] = "", ["prompt_bottom_center"] = "", ["prompt_bottom_right"] = "", // at the four positions
        ["prompt_capslock_indicator"] = $"{E}[1;31mCAPS{E}[m",   // ble.ps1: what \q{capslock} shows while caps lock is on
        ["capslock_refresh"] = "100",            // ble.ps1: ms between caps lock checks while idle ('' = off)
        ["capslock_reserve"] = "",               // ble.ps1: \q{capslock} keeps its width (spaces) while caps lock is off
        ["capslock_hook_on"] = "",               // ble.ps1: scriptblock (or code) run when caps lock turns on; its output goes to the terminal
        ["capslock_hook_off"] = "",              // ble.ps1: ... when it turns off
        ["exec_errexit_mark"] = $"{E}[91m[ble: exit %d]{E}[m",
        ["exec_error_mark"] = $"{E}[91m[ble: error]{E}[m",
        ["exec_elapsed_mark"] = $"{E}[94m[ble: elapsed %s]{E}[m",
        ["exec_elapsed_threshold"] = "10000",    // ms; '' disables
        ["keymap_vi_mode_show"] = "1",
        ["keymap_vi_imap_cursor"] = "5",         // DECSCUSR shape ('' = leave alone)
        ["keymap_vi_nmap_cursor"] = "2",
        ["keymap_vi_xmap_cursor"] = "2",
        ["keymap_vi_omap_cursor"] = "4",
        ["keymap_vi_smap_cursor"] = "2",
        ["complete_auto_view"] = "inline",       // inline | list (ble.ps1: PSReadLine-like list of history matches, f2 toggles)
        ["complete_auto_view_maxlines"] = "10",  // entries shown in the list view
        ["complete_auto_view_source"] = "history:completion",   // list view sources (completion: TabExpansion2, after complete_auto_delay)
        ["complete_auto_view_multiline"] = "1",  // list view in multi-line input: the current line is the query
        ["complete_auto_view_selected"] = "line",     // selected list entry: line = whole row, entry = its text (face list_view_selected), color = only its text color (face list_view_selected_color)
        ["complete_auto_view_ambiguous"] = "1",     // list view: still show " [correction] " inline for a wrong word
        ["complete_auto_view_completion"] = "word",   // word: completion entries show only the completed word; line: the whole line

        // ---- ble.sh options (defaults from ble.sh). Those with an effect in ble.ps1: ----
        ["complete_auto_complete_opts"] = "",    // history-disabled
        ["complete_menu_complete"] = "1",        // TAB again enters menu-complete
        ["complete_menu_filter"] = "1",          // typing filters the menu
        ["complete_menu_color"] = "on",
        ["complete_menu_color_match"] = "on",
        ["menu_align_min"] = "4",
        ["editor"] = "",                         // edit-and-execute-command (then $VISUAL, $EDITOR)
        ["edit_magic_expand"] = "history:sabbrev",   // SPACE expands sabbrevs only if this contains 'sabbrev'
        ["indent_offset"] = "4",
        ["indent_tabs"] = "",                    // ble.sh default is 1; PowerShell code is mostly indented with spaces
        ["tab_width"] = "",                      // '' = 8
        ["exec_elapsed_enabled"] = "usr+sys>=10000",  // ble.ps1 measures wall time; the number is the threshold
        ["exec_exit_mark"] = $"{E}[94m[ble: exit]{E}[m",
        ["prompt_eol_mark"] = $"{E}[94m[ble: EOF]{E}[m",   // no effect: the PowerShell host already starts the prompt on a new line
        ["prompt_emacs_mode_indicator"] = @"\q{keymap:emacs/mode-indicator}",
        ["keymap_emacs_mode_string_multiline"] = $"{E}[1m-- MULTILINE --{E}[m",
        ["keymap_vi_mode_string_nmap"] = $"{E}[1m~{E}[m",
        ["keymap_vi_mode_name_insert"] = "INSERT",
        ["keymap_vi_mode_name_replace"] = "REPLACE",
        ["keymap_vi_mode_name_vreplace"] = "VREPLACE",
        ["keymap_vi_mode_name_visual"] = "VISUAL",
        ["keymap_vi_mode_name_select"] = "SELECT",
        ["keymap_vi_mode_name_linewise"] = "LINE",
        ["keymap_vi_mode_name_blockwise"] = "BLOCK",
        ["keymap_vi_cmap_cursor"] = "",

        // ---- ble.sh options accepted for .blerc compatibility; no effect in ble.ps1 (bash or terminal specific) ----
        ["accept_line_threshold"] = "5", ["allow_exit_with_jobs"] = "", ["canvas_winch_action"] = "redraw-here",
        ["char_width_mode"] = "auto", ["char_width_version"] = "auto", ["color_scheme"] = "default",
        ["complete_allow_reduction"] = "", ["complete_auto_wordbreaks"] = " \t\n",
        ["complete_contract_function_names"] = "1", ["complete_limit"] = "", ["complete_limit_auto"] = "2000",
        ["complete_limit_auto_menu"] = "100", ["complete_menu_complete_opts"] = "insert-selection",
        ["complete_polling_cycle"] = "50", ["complete_requote_threshold"] = "0", ["complete_skip_matched"] = "on",
        ["complete_source_command_opts"] = "", ["complete_source_sabbrev_ignore"] = "", ["complete_source_sabbrev_opts"] = "",
        ["complete_timeout_auto"] = "5000", ["complete_timeout_compvar"] = "200", ["connect_tty"] = "1",
        ["debug_idle"] = "", ["debug_profiler_opts"] = "line:func", ["debug_profiler_tree_threshold"] = "5.0",
        ["debug_xtrace"] = "", ["debug_xtrace_ps4"] = "+ ", ["decode_abort_char"] = "28",
        ["decode_error_char_abell"] = "", ["decode_error_char_discard"] = "", ["decode_error_char_vbell"] = "1",
        ["decode_error_cseq_abell"] = "", ["decode_error_cseq_discard"] = "1", ["decode_error_cseq_vbell"] = "",
        ["decode_error_kseq_abell"] = "1", ["decode_error_kseq_discard"] = "1", ["decode_error_kseq_vbell"] = "1",
        ["decode_isolated_esc"] = "auto", ["decode_macro_limit"] = "1024", ["delete_selection_mode"] = "1",
        ["edit_abell"] = "[obsolute: use edit_bell=abell]", ["edit_vbell"] = "[obsolute: use edit_bell=vbell]",
        ["edit_forced_textmap"] = "1", ["edit_line_type"] = "logical", ["edit_magic_accept"] = "verify-syntax",
        ["edit_magic_opts"] = "", ["edit_marker"] = $"{E}[94m[ble: %s]{E}[m", ["edit_marker_error"] = $"{E}[91m[ble: %s]{E}[m",
        ["emoji_opts"] = "ri", ["emoji_version"] = "16.0", ["emoji_width"] = "2", ["exec_restore_pipestatus"] = "",
        ["filename_ls_colors"] = "", ["grapheme_cluster"] = "extended", ["highlight_eval_word_limit"] = "200",
        ["highlight_timeout_async"] = "5000", ["highlight_timeout_sync"] = "50", ["history_default_point"] = "auto",
        ["history_erasedups_limit"] = "0", ["history_lazyload"] = "1", ["history_limit_length"] = "10000",
        ["idle_interval"] = "", ["import_path"] = "", ["info_display"] = "top", ["input_encoding"] = "UTF-8",
        ["internal_exec_int_trace"] = "", ["internal_exec_type"] = "gexec",
        ["internal_ignoreeof_trap"] = "Use \"exit\" to leave the shell.", ["internal_stackdump_enabled"] = "0",
        ["internal_suppress_bash_output"] = "1", ["keymap_vi_imap_undo"] = "", ["keymap_vi_keymodel"] = "",
        ["keymap_vi_macro_depth"] = "64", ["keymap_vi_mode_update_prompt"] = "", ["keymap_vi_operatorfunc"] = "",
        ["keymap_vi_search_match_current"] = "", ["line_limit_length"] = "10000", ["line_limit_type"] = "none",
        ["menu_align_prefix"] = "", ["menu_dense_prefix"] = "", ["menu_desc_multicolumn_width"] = "65",
        ["menu_desc_prefix"] = "", ["menu_linewise_prefix"] = "", ["menu_prefix"] = "", ["openat_base"] = "30",
        ["pager"] = "", ["prompt_command_changes_layout"] = "", ["prompt_rps1_final"] = "", ["prompt_rps1_transient"] = "",
        ["prompt_screen_title"] = "", ["prompt_term_status"] = "", ["syntax_debug"] = "",
        ["syntax_eval_polling_interval"] = "50", ["term_bracketed_paste_mode"] = "on", ["term_cursor_external"] = "0",
        ["term_index_colors"] = "auto", ["term_modifyOtherKeys_external"] = "auto", ["term_modifyOtherKeys_internal"] = "auto",
        ["term_modifyOtherKeys_passthrough_kitty_protocol"] = "", ["term_stty_restore"] = "",
        ["term_synchronized_update_mode"] = "auto", ["term_true_colors"] = "semicolon", ["term_vi_cmap"] = "",
        ["term_vi_imap"] = "", ["term_vi_nmap"] = "", ["term_vi_omap"] = "", ["term_vi_smap"] = "", ["term_vi_xmap"] = "",
        ["undo_point"] = "auto", ["vbell_align"] = "right", ["vbell_default_message"] = " Wuff, -- Wuff!! ",
        ["vbell_duration"] = "2000",
    };

    // ble.sh obsolete names (bleopt/declare -o) -> current names
    static readonly Dictionary<string, string> Obsolete = new()
    {
        ["rps1"] = "prompt_rps1", ["rps1_transient"] = "prompt_rps1_transient",
        ["complete_menu_align"] = "menu_align_max", ["complete_stdin_frequency"] = "complete_polling_cycle",
        ["history_preserve_point"] = "history_default_point", ["keymap_vi_nmap_name"] = "keymap_vi_mode_string_nmap",
    };

    static readonly Dictionary<string, object> Values = new(Defaults);
    static readonly List<string> Order = Defaults.Keys.ToList();

    /// <summary>Strips ble.sh's bleopt_ variable prefix and maps obsolete names.</summary>
    public static string Canonical(string name)
    {
        if (name.StartsWith("bleopt_", StringComparison.Ordinal)) name = name.Substring(7);
        return Obsolete.TryGetValue(name, out var n) ? n : name;
    }

    public static IEnumerable<string> Names => Order;
    public static bool Exists(string name) => Values.ContainsKey(Canonical(name));

    public static event Action<string>? Changed;

    public static object? GetRaw(string name) => Values.TryGetValue(Canonical(name), out var v) ? v : null;

    public static string Get(string name) => Values.TryGetValue(Canonical(name), out var v) ? v as string ?? v?.ToString() ?? "" : "";

    public static bool GetBool(string name)
    {
        var v = GetRaw(name);
        if (v is ScriptBlock) return true;
        var s = v as string;
        return !string.IsNullOrEmpty(s) && s != "0";
    }

    public static int GetInt(string name, int def = 0) => int.TryParse(Get(name), out var n) ? n : def;

    public static void Set(string name, object? value)
    {
        name = Canonical(name);
        if (!Values.ContainsKey(name)) throw new ArgumentException($"bleopt: unknown option '{name}'");
        if (value is PSObject pso) value = pso.BaseObject;
        object v = value is ScriptBlock sb ? sb : (value?.ToString() ?? "");
        if (name == "default_keymap" && v is string s)
        {
            // ble.sh also has auto (follow readline's editing-mode) and safe; both mean emacs here
            if (s is "auto" or "safe") v = "emacs";
            else if (s != "emacs" && s != "vi") throw new ArgumentException("bleopt: default_keymap must be 'emacs' or 'vi' (or 'auto')");
        }
        Values[name] = v;
        Changed?.Invoke(name);
    }

    /// <summary>bleopt name:=value: sets an option, creating it if it does not exist (ble.sh).</summary>
    public static void Define(string name, object? value)
    {
        name = Canonical(name);
        if (!Values.ContainsKey(name)) { Values[name] = ""; Order.Add(name); }
        Set(name, value);
    }

    /// <summary>
    /// Applies one ble.sh bleopt assignment: NAME=VALUE (NAME may contain wildcards), NAME:=VALUE
    /// (define), NAME+=VALUE / NAME-=VALUE (add to / remove from a colon-separated list).
    /// Returns false if the argument is not an assignment.
    /// </summary>
    public static bool Apply(string arg)
    {
        var m = System.Text.RegularExpressions.Regex.Match(arg, @"^([_a-zA-Z0-9@*?]+)([:+-]?=)(.*)$", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (!m.Success) return false;
        string name = m.Groups[1].Value, op = m.Groups[2].Value, value = m.Groups[3].Value;
        if (op == ":=") { Define(name, value); return true; }
        foreach (var n in Match(name))
        {
            switch (op)
            {
                case "+=":
                    {
                        var cur = Get(n);
                        var items = cur.Split(':').Where(x => x.Length > 0).ToList();
                        if (!items.Contains(value)) Set(n, cur.Length == 0 ? value : cur + ":" + value);
                        break;
                    }
                case "-=":
                    Set(n, string.Join(':', Get(n).Split(':').Where(x => x.Length > 0 && x != value)));
                    break;
                default:
                    Set(n, value);
                    break;
            }
        }
        return true;
    }

    /// <summary>Option names matching a name or a ble.sh pattern (*, ? and @ as wildcards). Throws if none match.</summary>
    public static List<string> Match(string pattern)
    {
        var name = Canonical(pattern);
        if (pattern.IndexOfAny(['*', '?', '@']) < 0)
        {
            if (!Values.ContainsKey(name)) throw new ArgumentException($"bleopt: unknown option '{pattern}'");
            return [name];
        }
        var wp = new WildcardPattern(name.Replace('@', '*'), WildcardOptions.IgnoreCase);
        var list = Order.Where(n => wp.IsMatch(n)).ToList();
        if (list.Count == 0) throw new ArgumentException($"bleopt: unknown option '{pattern}'");
        return list;
    }

    public static void Reset()
    {
        foreach (var name in Order.Where(n => !Defaults.ContainsKey(n)).ToList()) { Values.Remove(name); Order.Remove(name); }
        foreach (var kv in Defaults) Values[kv.Key] = kv.Value;
    }

    public static string Quote(object? v)
    {
        if (v is ScriptBlock sb) return "{" + sb + "}";
        return "'" + (v?.ToString() ?? "").Replace("'", "''").Replace("\x1b", "`e") + "'";
    }
}

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
