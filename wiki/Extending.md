## Abbreviations

```powershell
ble-sabbrev gst='git status' L='| Select-Object -First 10'
ble-sabbrev today { Get-Date -Format yyyy-MM-dd }   # computed when expanded
ble-sabbrev -r gst                                   # remove
```

An abbreviation expands when you type a space or press `RET` after it (`M-'` expands without
inserting anything).

## Running a command from a key

`ble-bind -c` runs PowerShell code. The current line stays on screen, the command's output is
printed below it, and the line is drawn again afterwards.

```powershell
ble-bind -c 'M-s' { git status }
```

## Editing the line from PowerShell

`ble-bind -x` runs code that can read and change the line through three global variables, like
`bind -x` in bash:

| Variable | |
|---|---|
| `$READLINE_LINE` | The text |
| `$READLINE_POINT` | Cursor position |
| `$READLINE_MARK` | Mark position, or -1 |

```powershell
ble-bind -x 'C-t' { $global:READLINE_LINE += (Get-Date -Format s); $global:READLINE_POINT = $READLINE_LINE.Length }
```

Assign with `$global:`, otherwise the change stays local to the scriptblock.

## Widgets

A widget is a named action that keys can be bound to. Define one with `Register-BleWidget`, or
with a function named `ble/widget/NAME` (ble.sh style):

```powershell
Register-BleWidget upcase-line { $global:READLINE_LINE = $READLINE_LINE.ToUpper() }

function ble/widget/kill-and-upcase {
    Invoke-BleWidget kill-forward-line          # built-in widgets can be called inside
    $global:READLINE_LINE = $READLINE_LINE.ToUpper()
}

ble-bind -f 'C-x u' upcase-line
ble-bind -f 'C-x k' kill-and-upcase
```

`ble-bind -L` lists all widget names, including yours.

## Keyboard macros

`ble-bind -s` maps a key to other keys, as if you typed them:

```powershell
ble-bind -s 'M-g' 'C-a g i t SP C-e'
ble-bind -m vi_xmap -s 'Y' '" + y'
```

## ble-import

```powershell
ble-import keymap.vi                     # same as bleopt default_keymap=vi
ble-import integration/fzf-key-bindings  # fzf keys (needs fzf)
ble-import integration/fzf-completion    # word** TAB opens fzf
ble-import ~/my-ble-settings.ps1         # dot-sources a PowerShell script
ble-import ~/.blerc                      # other files are read as ble.sh config (Import-BleRc)
```
