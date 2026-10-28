Put your settings in your `$PROFILE`, after `Import-Module ble`, or in a file you dot-source
from there. Everything is set with four commands, which work like their ble.sh versions:

| Command | Sets | List everything |
|---|---|---|
| `bleopt` | options ([Options](Options)) | `bleopt`, or `bleopt 'complete_*'` |
| `ble-face` | colors ([Colors and Highlighting](Colors-and-Highlighting)) | `ble-face` |
| `ble-bind` | keys ([Key Bindings](Key-Bindings)) | `ble-bind -P`, widgets: `ble-bind -L` |
| `ble-sabbrev` | abbreviations ([Extending](Extending)) | `ble-sabbrev` |

## Example

```powershell
Import-Module ble

bleopt default_keymap=vi
bleopt complete_auto_view=list complete_auto_delay=50
bleopt prompt_rps1='\t'
ble-face command_builtin=fg=33,bold
ble-face 'filename_*=underline'
ble-bind -m vi_imap -f 'M-k' history-search-backward
ble-bind -m vi_imap -f 'M-j' history-search-forward
ble-sabbrev gst='git status'
```

## Starting from the default .blerc

The module comes with a ready-made `.blerc` (vi mode with cursor shapes). `New-BleRc` copies it
somewhere you can edit it:

```powershell
New-BleRc                  # writes ~/.blerc
New-BleRc ~/dotfiles       # writes ~/dotfiles/.blerc
New-BleRc ~/my.blerc -Force   # replaces an existing file
```

Then load it in your `$PROFILE` after `Import-Module ble`. `Import-BleRc` with no argument reads
`~/.blerc`:

```powershell
Import-Module ble
Import-BleRc
```

## bleopt syntax

```powershell
bleopt name=value          # set
bleopt name                # show
bleopt 'complete_*'        # show all matching
bleopt name+=value         # add to a colon-separated list
bleopt name-=value         # remove from it
bleopt name:=value         # define a new option
bleopt prompt_rps1 { Get-Date -Format HH:mm }   # prompt options also take a scriptblock
```

`Set-BleOption` and `Get-BleOption` do the same in PowerShell style.

Commas need no quotes: `ble-face region=fg=231,bg=60` works even though PowerShell would normally
split the value at the comma.

## Using an existing .blerc

`Import-BleRc` applies a ble.sh configuration file. See
[Coming from ble.sh](Coming-from-ble.sh#importing-a-blerc).

```powershell
Import-BleRc                 # ~/.blerc, else ~/.config/blesh/init.sh
Import-BleRc ~/dotfiles/blerc
```

Settings made after the import override the file.
