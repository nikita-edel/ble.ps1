Set options with `bleopt name=value`. An empty value (`bleopt name=`) turns a switch off.
`bleopt` alone lists all options with their current values.

## General

| Option | Default | |
|---|---|---|
| `default_keymap` | `emacs` | `emacs` or `vi` (`auto` means emacs) |
| `edit_bell` | `none` | `abell`/`audible`, `vbell`/`visible`, or both (`abell:vbell`) |
| `edit_paste_newline` | `1` | Keep a pasted block of lines as one command |
| `edit_magic_expand` | `history:sabbrev` | Space expands abbreviations only if this contains `sabbrev` |
| `editor` | | Editor for `C-x C-e`; then `$VISUAL`, `$EDITOR`, notepad |
| `indent_offset` / `indent_tabs` | `4` / | Vi `>`/`<` width; indent with tabs |
| `tab_width` | `8` | Width of a tab in the command line |

## Highlighting

| Option | Default | |
|---|---|---|
| `highlight_syntax` | `1` | All highlighting |
| `highlight_filename` | `1` | Files and paths |
| `highlight_variable` | `1` | Variables colored by their state |
| `highlight_incomplete` | `1` | Unclosed quotes and brackets marked as errors |

## Suggestions

| Option | Default | |
|---|---|---|
| `complete_auto_complete` | `1` | Suggestions at all |
| `complete_auto_history` | `1` | Suggestions from history |
| `complete_auto_completion` | `1` | Suggestions from PowerShell's completion |
| `complete_auto_delay` | `100` | Milliseconds of idle time before completion results appear |
| `complete_auto_complete_opts` | | `history-disabled`: no history suggestions |
| `complete_auto_view` | `inline` | `list` shows suggestions as a list below the line (F2 switches) |
| `complete_auto_view_maxlines` | `10` | Entries in the list |
| `complete_auto_view_source` | `history:completion` | What the list shows |
| `complete_auto_view_completion` | `word` | Completion entries show the word only, or the whole `line` |
| `complete_auto_view_selected` | `line` | Selected entry: whole `line` highlighted, `entry` text highlighted, or text `color` only |
| `complete_auto_view_multiline` | `1` | In multi-line input, list entries for the current line |
| `complete_auto_view_ambiguous` | `1` | With the list, still show ` [correction] ` for a wrong word |

## Completion menu

| Option | Default | |
|---|---|---|
| `complete_ambiguous` | `1` | Substring and fuzzy matching when nothing matches as a prefix |
| `complete_auto_menu` | | Open the menu after this many milliseconds of idle time |
| `complete_menu_style` | `align` | `align`, `linewise` or `desc` (with descriptions) |
| `complete_menu_maxlines` | `10` | Menu height; `-1` = as much as fits |
| `complete_menu_complete` | `1` | A second TAB starts cycling through the menu |
| `complete_menu_filter` | `1` | Typing filters the open menu |
| `complete_menu_color` / `complete_menu_color_match` | `on` | Colors in the menu / highlight of the typed part |
| `menu_align_min` / `menu_align_max` | `4` / `32` | Column width limits in `align` style |
| `complete_fzf_trigger` | | Word ending that opens fzf on TAB (`**` after `ble-import integration/fzf-completion`) |

## History

| Option | Default | |
|---|---|---|
| `history_file` | PSReadLine's | History file |
| `history_size` | `10000` | Entries kept |
| `history_share` | `1` | Pick up commands from other open sessions |
| `history_ignore_space` | `1` | Don't save commands that start with a space |
| `history_ignore_pattern` | `password\|asplaintext\|token\|apikey\|secret` | Matching commands stay in memory only |

## Prompt and status

| Option | Default | |
|---|---|---|
| `prompt_ps2` | `>> ` | Continuation prompt |
| `prompt_rps1` | | Right prompt |
| `prompt_status_line` / `prompt_status_align` | / `left` | Line under the command line (`left`, `center`, `right`) |
| `prompt_xterm_title` | | Terminal title |
| `prompt_ps1_transient` / `prompt_ps1_final` | | Transient prompt: `always`, `same-dir` or `trim` / its text |
| `prompt_ruler` | | Separator line drawn before each prompt |
| `exec_error_mark` | `[ble: error]` | After a failed PowerShell command |
| `exec_errexit_mark` | `[ble: exit %d]` | After a native program exits with an error |
| `exec_elapsed_mark` / `exec_elapsed_threshold` | `[ble: elapsed %s]` / `10000` | After commands slower than the threshold (ms) |

Prompt options take [prompt sequences](Prompt#prompt-sequences) or a scriptblock.

## Vi and emacs mode lines

| Option | Default | |
|---|---|---|
| `keymap_vi_mode_show` | `1` | Show the vi mode line |
| `prompt_vi_mode_indicator` | `\q{keymap:vi/mode-indicator}` | Its text |
| `keymap_vi_mode_name_insert` etc. | `INSERT`, `REPLACE`, `VISUAL`, `SELECT`, `LINE`, `BLOCK` | Mode names |
| `keymap_vi_mode_string_nmap` | `~` (bold) | Shown in normal mode |
| `keymap_vi_imap_cursor` etc. | `5` (insert), `2` (normal, visual, select), `4` (operator pending) | Cursor shape per mode |
| `prompt_emacs_mode_indicator` | `\q{keymap:emacs/mode-indicator}` | Emacs mode line (shows `-- MULTILINE --` for multi-line input) |

## ble.sh options without an effect

All other ble.sh options (`term_*`, `decode_*`, `char_width_*`, `vbell_*`, ...) are accepted, so a
`.blerc` loads without errors, but they do nothing in PowerShell. Old names such as `rps1` map to
their current names.
