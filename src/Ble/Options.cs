using System.Management.Automation;
using System.Text.RegularExpressions;

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
        ["complete_auto_history_multiline"] = "none",    // ble.ps1: multi-line history entries: none = not suggested, line = each line on its own, whole = the whole entry
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
        ["complete_auto_view_inline"] = "1",        // list view: show what S-RET would take after the cursor
        ["complete_auto_view_tips"] = "1",          // list view: the row with the position and the keys ('' or 0 = off)
        ["complete_auto_view_min_width"] = "0",     // list view: narrower windows get inline suggestions (0 = always the list)
        ["complete_auto_view_min_height"] = "0",    // list view: lower windows get inline suggestions (0 = always the list)
        ["complete_auto_hide_command_paths"] = "",  // suggestions: leave out /usr/bin/ls when typing ls (TAB still lists it)

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

    /// <summary>Whether an option that is a colon-separated list has the item.</summary>
    public static bool ListHas(string name, string item) => Get(name).Split(':').Contains(item);

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
        var m = Regex.Match(arg, @"^([_a-zA-Z0-9@*?]+)([:+-]?=)(.*)$", RegexOptions.Singleline);
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
