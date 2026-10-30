ble.ps1 copies ble.sh where PowerShell allows it: the default key bindings, the faces and their
colors, the highlighting rules, `bleopt` names and syntax, and the vi mode. Most of your muscle
memory and your `.blerc` carry over.

## Importing a .blerc

```powershell
Import-BleRc                   # ~/.blerc, else ~/.config/blesh/init.sh
Import-BleRc ~/dotfiles/blerc  # a specific file
Import-BleRc -Quiet            # without warnings
```

No `.blerc` yet? `New-BleRc` writes the one that ships with the module to `~/.blerc`.

The file is read as bash, and the ble.sh configuration commands in it are applied:

- `bleopt`, `ble-face` (also the old `ble-color-setface`), `ble-sabbrev`, `ble-import`
- `ble-bind -f`, `-s`, `-m`, `--cursor`
- `set -o vi`, `bind 'set editing-mode vi'`, `source`

Quoting, `$'\e[1m'` strings, variables (`$HOME`, `${X:-default}`, assignments made earlier in the
file), `~`, functions, `{ ...; }` groups and `blehook/eval-after-load keymap_vi FUNC` work, so the
usual blerc layout loads as is.

Some things can't run in PowerShell. They are skipped, with a warning that gives the line number:

- `if`, `case` and loops, `[[ ]]` tests, `$(...)`
- `ble-bind -x` and `-c`, because their commands are bash. Bind those keys again with a
  PowerShell scriptblock.
- widgets defined as bash functions, and widgets or keymaps ble.ps1 does not have (`vi_omap`,
  `vi_cmap`)

Every ble.sh option name is accepted. Options for bash or terminal internals have no effect.

The `ble` command works too: `ble version` (or `$BLE_VERSION`), `ble attach`, `ble detach`,
`ble bind ...`, `ble opt ...`, `ble face ...`, `ble sabbrev ...`. Like in ble.sh, `ble <name>`
runs `ble-<name>`. `ble update`, `ble reload` and `ble hook` don't exist here.

## Differences

**Language.** Highlighting and completion follow PowerShell, not bash. Cmdlets use the
`command_builtin` face, native programs and scripts `command_file`. There is no history expansion
(`!!`), since `!` is an operator in PowerShell.

**Extra checks.** PowerShell tells ble.ps1 more about a command than bash tells ble.sh, so
parameters a command doesn't have (`argument_error`) and paths that don't exist
(`filename_warning`) are marked as well.

**Not available on PowerShell or Windows.** `C-z`, job names, `autocd`, `M-~`/`M-@` (user and
host completion), `M-{` (brace expansion), magic slash, bracketed paste (a pasted block of lines
is detected by timing instead) and `prompt_eol_mark` (PowerShell already starts the prompt on a
new line).

**Prompt.** `exec_errexit_mark` shows a program's exit code. A failed cmdlet shows
`exec_error_mark` instead.

**Mode line.** The vi/emacs mode line sits at the bottom of the window by default, like the status
line. `info_display` has no effect. Use `prompt_mode_line_position` instead
(see [Layout](Prompt#layout)).

**Vi.** `:w` runs the line (an addition). `C-g` also cancels the selection, menu or suggestion.

**Extensions.** `ble-import` knows `keymap.vi`, `keymap.emacs` and the fzf integration. ble.sh's
contrib collection is not included. Widgets are written in PowerShell (see [Extending](Extending)).

**Configuration.** A `.blerc` is not loaded automatically; call `Import-BleRc`.

**Additions.** The list view (`f2`), `S-RET` inserting a newline when there is no suggestion,
`C-S-v` pasting from the Windows clipboard, and `C-M-j` switching between emacs and vi.
