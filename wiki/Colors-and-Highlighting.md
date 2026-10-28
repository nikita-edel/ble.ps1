The command line is colored as you type, using PowerShell's own parser and ble.sh's color
scheme. Each kind of text has a named *face*; `ble-face` lists them all and changes them.

## What gets colored

| Text | Face | Default |
|---|---|---|
| Cmdlets (`Get-ChildItem`) | `command_builtin` | red, like bash builtins in ble.sh |
| Aliases | `command_alias` | teal |
| Functions | `command_function` | purple |
| Programs and scripts (`git`, `.\build.ps1`) | `command_file` | green |
| Keywords (`if`, `foreach`) | `command_keyword` | blue |
| `.` (dot-source) | `command_builtin_dot` | red, bold |
| Unknown commands, syntax errors | `syntax_error` | white on red |
| Parameters (`-Path`) | `argument_option` | teal |
| Parameters the command doesn't have | `argument_error` | black on pink |
| Strings / quote characters | `syntax_quoted` / `syntax_quotation` | green / green, bold |
| Variables | `syntax_varname`, `varname_*` | by state, see below |
| Existing files and directories | `filename_*` | underlined |
| Paths that don't exist | `filename_warning` | underlined red |
| Comments | `syntax_comment` | gray |
| Globs `*` `?`, `~` | `syntax_glob`, `syntax_tilde` | |

An unclosed quote or bracket is marked at the opening character and at the last character, as
in ble.sh. `bleopt highlight_incomplete=` turns that off.

Variables are colored by their current value, like ble.sh: `varname_export` (environment),
`varname_array`, `varname_hash`, `varname_readonly`, `varname_number`, `varname_empty`,
`varname_unset`, and `varname_new` for a variable being assigned for the first time.

Other faces: `auto_complete` (gray suggestion), `region` (selection), `menu_complete_selected`,
`menu_complete_match`, `list_view_selected`, `list_view_selected_color`, `prompt_status_line`.

## Changing a color

```powershell
ble-face command_builtin=fg=33,bold
ble-face 'filename_*=underline'          # patterns change several faces
ble-face -s region fg=231,bg=60          # ble.sh's -s form
ble-face -r region                       # back to the default
ble-face command_builtin                 # show one face
```

## Face specs

A spec is a comma-separated list:

| Part | Example |
|---|---|
| Foreground / background | `fg=red`, `bg=236` |
| Color names | `black brown green olive navy purple teal silver gray red lime yellow blue magenta cyan white orange` |
| 256-color index | `fg=33` |
| RGB | `fg=#ffaf00`, `fg=#fa0`, `fg=rgb:255/175/0` |
| Other color forms | `hsl:30/100%/50%`, `hsb:30/100%/100%`, `cmyk:0/30/100/0` |
| Attributes | `bold underline italic reverse strike blink invis standout` |
| Reset | `none` (clears what came before it) |
| Another face | `ref:syntax_quoted` (follows it), `copy:syntax_quoted` (copies it now) |
| Raw SGR | `sgrspec:1;31`, `ansi:\e[1;31m` |

Hex colors need 3 or 6 digits. `#ffff` is an error.

## Turning highlighting off

```powershell
bleopt highlight_syntax=        # everything
bleopt highlight_filename=      # file underlines and missing-path warnings
bleopt highlight_variable=      # variable states (all variables use syntax_varname)
```
