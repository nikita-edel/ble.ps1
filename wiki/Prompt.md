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
bleopt prompt_status_line='\q{keymap:vi/mode-indicator}' prompt_status_align=right   # see Layout
bleopt prompt_xterm_title='\u@\h: \w'            # terminal title
bleopt prompt_ps1_transient=always prompt_ps1_final='> '   # shorten old prompts
bleopt prompt_ruler='-'                          # line before each prompt
bleopt prompt_ps2='>> '                          # continuation lines
```

`prompt_ps1_transient` can be `always`, `same-dir` (only while the directory stays the same) or
`trim` (keep only the last line of a multi-line prompt).

## Layout

Besides the right prompt, there are four places for extra rows:

```
ble.ps1 on main                      <- starship's first line
[above]                              <- rows between the prompt and the input
$ git st                [rps1]       <- prompt_input_left goes in front of the input
[below]                              <- right under the input, above the menu / list
  git status   [History]
  ...
[bottom]                             <- last rows of the window
```

`top` is the same as `bottom`, but at the first rows of the window.

The vi/emacs mode line and `prompt_status_line` go to the bottom by default, like ble.sh's
status line. Move them with `prompt_mode_line_position` and `prompt_status_line_position`
(`bottom`, `top`, `below`, `above`):

```powershell
# under the input, but the suggestion list opens below it, so it never moves
bleopt prompt_mode_line_position=below
```

Each position also has a row with a left, a center and a right part, like a status bar:

```powershell
bleopt keymap_vi_mode_show=                     # no separate mode line
bleopt prompt_bottom_left='\q{keymap:vi/mode-indicator}'
bleopt prompt_bottom_center='\w'
bleopt prompt_bottom_right='\q{capslock} \t'
bleopt prompt_above_right='\q{capslock}'
bleopt prompt_input_left='\q{capslock}'
```

Each row has a face (`prompt_top`, `prompt_above`, `prompt_below`, `prompt_bottom`) that colors the
whole row, like a bar. Colors inside the text are set with escapes; `\e[m` goes back to the row's
face, not to plain text:

```powershell
ble-face prompt_bottom='fg=231,bg=236'
bleopt prompt_bottom_left='\e[30;42m \q{keymap:vi/mode-name} \e[m \w'
```

`prompt_input_after` is drawn right after the input and its suggestion, when it fits on the row.
With `bleopt prompt_input_after_position=before` it goes between the input and the suggestion instead.

They're named `prompt_{top,above,below,bottom}_{left,center,right}`. When a row gets too narrow, the
center is dropped first, then the left part gets cut. If one position has several rows, the mode line comes first, then the
status line, then the left/center/right row.

`prompt_status_align` also takes ble.sh's `justify`: the fields of the status line (split by `\r`,
or by `X` with `justify=X`) are spread over the whole line.

A few things to know:

- `above` works by adding empty rows to your prompt before its last line (the `$ ` part of a
  multi-line starship prompt, or the whole `PS> `). The rows are emptied once you press Enter.
- `top` covers the first rows of the window. When the screen is full, the output that was there
  is drawn over while you type and gone afterwards.
- `bottom` keeps those rows free under the input, so when you're at the end of the window it
  scrolls up by that many rows first.

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
| `\q{capslock}` | `prompt_capslock_indicator` (red `CAPS`) while caps lock is on (ble.ps1 only) |

`\[` and `\]` are accepted and ignored.

## Caps lock

The terminal can't tell a shell about caps lock, so ble.ps1 asks the OS (like
[capsdetect.nvim](https://github.com/nikita-edel/capsdetect.nvim)): `GetKeyState` on Windows, the
keyboard LEDs in `/sys/class/leds` on Linux, CoreGraphics on macOS. Over ssh there's no local
keyboard, so `\q{capslock}` stays empty there. Put it in any prompt option, for example next to the vi mode:

```powershell
bleopt prompt_vi_mode_indicator='\q{keymap:vi/mode-indicator} \q{capslock}'
```

While waiting for keys, ble.ps1 checks every `capslock_refresh` ms (default `100`, `''` turns it off).

Change the text with `prompt_capslock_indicator`, for example `bleopt prompt_capslock_indicator='\e[33m^\e[m'`.
With `bleopt capslock_reserve=1`, `\q{capslock}` prints spaces of the same width while caps lock is
off, so the text next to it doesn't move when you toggle it.

To run something when caps lock turns on or off, give `capslock_hook_on` / `capslock_hook_off` a
scriptblock (or PowerShell code as a string, e.g. a function name, in a `.blerc`). What it returns
is written to the terminal, so escape sequences work directly. For example a red cursor while caps
lock is on, which is hard to miss in vi normal mode, where `j` turns into `J`:

```powershell
bleopt capslock_hook_on { "`e]12;red`a" }    # cursor color (if the terminal supports OSC 12)
bleopt capslock_hook_off { "`e]112`a" }      # back to the default color
```

The hooks run when ble.ps1 notices the change: while you type, or at the next prompt if caps lock
changed while a command was running. Return only escape sequences; visible text would end up
wherever the cursor is.
