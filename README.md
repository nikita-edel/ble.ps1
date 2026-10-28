# ble.ps1

A powershell 7.4+ line editor, based on [ble.sh](https://github.com/akinomyoga/ble.sh) (basically cloned)

- Full support of the same ble.sh config: `Import-BleRc` (copy and paste your ble config),
`bleopt`, `ble-face`, `ble-bind`, `ble-sabbrev`
- Syntax highlighting with ble.shs colors. Unknown commands, bad parameters, unclosed quotes
  and missing paths show up while you type.
- Suggestions from history and from powershell's completion, inline or as a list below the line.
- Menu completion with fuzzy matching.
- Emacs and vi modes, including visual block mode, macros, marks and `:` commands.


## Install

You need powershelll 7.4 or later.

[Powershell Gallery Link](https://www.powershellgallery.com/packages/ble)

`Install-Module ble`

Or download the zip from [Releases](https://github.com/nikita-edel/ble.ps1/releases), extract it and run:

```powershell
pwsh -ExecutionPolicy Bypass -File .\install.ps1
```

Or build it yourself (needs the .NET 8 SDK):

```powershell
git clone https://github.com/nikita-edel/ble.ps1
cd ble.ps1
./build.ps1 -Install
```

## Use and Configure

Either put your configuration in a .blerc file or into your powershell config<br>
`Import-Module ble` and `Import-BleRc <path to .blerc>` must be in your $PROFILE (powershell config)

`New-BleRc <path to new .blerc>` will copy a default profile (mine) and you can use that one to try it out

Note: scince there is additions to functionality like the `bleopt complete_auto_view=list` it wont be backwards compatible to ble.sh<br>

```powershell
bleopt default_keymap=vi                # vi mode
bleopt complete_auto_view=list          # suggestions as a list (F2 switches)
ble-face command_builtin=fg=33          # colors

# go through the list
ble-bind -m vi_imap -f 'M-j' history-search-backward
ble-bind -m vi_imap -f 'M-k' history-search-forward
```

`bleopt` lists every option, `ble-face` every color, `ble-bind -P` every key binding.

`ble-detach` switches back to PSReadLine, `ble-attach` turns ble.ps1 on again.

## Documentation

See the [wiki](https://github.com/nikita-edel/ble.ps1/wiki) for options, key bindings,
completion, vi mode, history, the prompt, writing your own widgets, and how ble.ps1 differs
from ble.sh.
