ble.ps1 keeps your own `prompt` function (and starship, oh-my-posh, ...), including `$?`
inside it. It adds a few things around it.

## Status marks

After a command fails, a mark is printed before the next prompt:

- `[ble: exit 3]` when a program exits with an error (`exec_errexit_mark`)
- `[ble: error]` when a PowerShell command fails (`exec_error_mark`)
- `[ble: elapsed 12.345s]` when a command ran longer than `exec_elapsed_threshold` ms (10 s)

Set a mark to an empty string to turn it off: `bleopt exec_error_mark=`.

## Other prompt options

```powershell
bleopt prompt_rps1='\t'                          # right prompt
bleopt prompt_rps1 { Get-Date -Format HH:mm }    # ... or a scriptblock
bleopt prompt_status_line='\q{keymap:vi/mode-indicator}' prompt_status_align=right
bleopt prompt_xterm_title='\u@\h: \w'            # terminal title
bleopt prompt_ps1_transient=always prompt_ps1_final='> '   # shorten old prompts
bleopt prompt_ruler='-'                          # line before each prompt
bleopt prompt_ps2='>> '                          # continuation lines
```

`prompt_ps1_transient` can be `always`, `same-dir` (only while the directory stays the same) or
`trim` (keep only the last line of a multi-line prompt).

## Prompt sequences

These options understand bash prompt sequences:

| | |
|---|---|
| `\u` `\h` `\H` | User, host, full host name |
| `\w` `\W` | Current directory (with `~`), its last part |
| `\t` `\T` `\@` `\A` `\d` | Time 24h, 12h, am/pm, HH:MM, date |
| `\D{%Y-%m-%d}` | strftime format |
| `\$` | `#` when elevated, else `$` |
| `\#` `\!` | History number |
| `\v` `\V` `\s` | PowerShell version, `pwsh` |
| `\e` `\n` `\a` `\\` | Escape, newline, bell, backslash |
| `\q{keymap}` | Current keymap |
| `\q{keymap:vi/mode-indicator}` | Vi mode (`-- INSERT --`) |
| `\q{keymap:emacs/mode-indicator}` | Emacs mode line (`-- MULTILINE --`) |

`\[` and `\]` are accepted and ignored.
