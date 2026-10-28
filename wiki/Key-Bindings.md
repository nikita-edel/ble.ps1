The default bindings are ble.sh's. `ble-bind -P` prints all of them, `ble-bind -L` lists the
widget names, and `ble-bind -k` shows the name of a key you press.

## Key names

`C-x` is Ctrl+x, `M-x` is Alt+x, `S-` is Shift. Special keys: `RET`, `TAB`, `ESC`, `SP`, `DEL`
(Backspace), `delete`, `insert`, `home`, `end`, `prior`/`next` (PgUp/PgDn), `up`, `down`, `left`,
`right`, `f1`-`f24`. A sequence is written with spaces: `'C-x C-e'`. PSReadLine-style names such as
`'Ctrl+Shift+k'` also work.

Keymaps: `emacs`, `vi_imap` (insert), `vi_nmap` (normal), `vi_xmap` (visual), `vi_smap` (select).
Without `-m`, `ble-bind` uses `emacs` or `vi_imap`, depending on `default_keymap`.

## ble-bind

```powershell
ble-bind -f 'C-x C-e' edit-and-execute-command      # key -> widget
ble-bind -m vi_nmap -f 'H' beginning-of-line        # in another keymap
ble-bind -f 'C-x t' -                               # remove a binding
ble-bind -s 'M-g' 'C-a g i t SP C-e'                # key -> other keys (macro), one key per word
ble-bind -c 'M-s' { git status }                    # run a command, then redraw the line
ble-bind -x 'C-t' { $global:READLINE_LINE += 'x' }  # edit the line from PowerShell
ble-bind -m vi_nmap -cursor 2                       # cursor shape for a keymap
```

`-x` and `-c` are explained in [Extending](Extending).

## Emacs mode

| Keys | Action |
|---|---|
| `C-a` `C-e`, `home` `end` | Start / end of line (twice: of the whole input) |
| `C-f` `C-b`, `M-f` `M-b`, `C-right` `C-left`, `M-right` `M-left` | Character, word, shell word. At the end of the line these accept the suggestion |
| `C-k`, `C-u`, `C-w`, `M-d`, `M-h` | Kill to end / start of line, region or previous word, next word, previous word |
| `C-y`, `M-y`, `M-Y` | Yank, cycle through the kill ring forward / backward |
| `M-w` | Copy region or previous word |
| `C-SP`, `C-x C-x`, `S-arrows` | Set mark, swap mark and cursor, select with Shift |
| `C-_` `C-/`, `C-x U`, `M-r` | Undo, redo, revert line |
| `C-t`, `M-t`, `M-u` `M-l` `M-c` | Transpose characters / words, upper / lower / capitalize word |
| `C-r` `C-s` | Incremental history search |
| `up` `down`, `prior` `next`, `C-x p` `C-x n` | History; prefix search; substring search |
| `M-.`, `M-_`, `C-M-y` | Last argument of the previous command (repeat for older), nth argument |
| `TAB`, `S-TAB`, `M-?`, `M-*` | Complete, menu backward, show menu, insert all matches |
| `M-/` `M-$` `M-!` `M-g` `M-TAB` `C-x C-r` | Complete files, variables, commands, glob, words from history, dabbrev |
| `RET` | Run a complete single line, otherwise insert a newline |
| `C-j`, `C-RET` | Run (accepting the suggestion first) |
| `S-RET` | Accept the suggestion, or insert a newline |
| `M-RET` | Insert a newline |
| `C-o` | Run, then bring back the next history entry |
| `C-c`, `C-d`, `C-l`, `C-g` | Discard line, delete character (or exit on an empty line), clear screen, cancel |
| `C-x C-e` | Edit the line in an external editor and run it |
| `M-#`, `C-q` `C-v`, `insert`, `C-]`, `M-z` | Comment out and run, insert next key literally, overwrite mode, jump to character, zap to character |
| `C-x (` `C-x )` `C-x e` | Record and play a keyboard macro |
| `M-x`, `f1` | Run a widget by name, help for the command under the cursor |
| `M-0`..`M-9`, `M--` | Numeric argument |
| `f2` | Switch between inline suggestion and list view |
| `C-S-v` | Paste from the Windows clipboard |
| `C-M-j` | Switch to vi mode |

## Vi insert mode

The common bindings above work in insert mode too, plus `ESC` (normal mode), `C-c` (normal mode,
as in ble.sh), `C-o` (one normal-mode command), vim-style `C-w` and `DEL`, and `C-M-j` (switch to
emacs). Normal, visual and select mode are described in [Vi Mode](Vi-Mode).
