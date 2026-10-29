ble.ps1 uses PSReadLine's history file, in the same format, so switching between the two keeps
one history. Set another file with `bleopt history_file=...`.

- Duplicates are removed; the newest occurrence is kept.
- Commands from other open PowerShell windows are picked up at the next prompt
  (`history_share`).
- Commands starting with a space are not saved (`history_ignore_space`).
- Commands that look like secrets (`password`, `token`, `apikey`, ...) stay in memory and are not
  written to the file (`history_ignore_pattern`).
- The folder each command ran in goes to a second file, `<history file>.dirs`, which suggestions
  use to prefer commands from the current folder (see
  [Completion and Suggestions](Completion-and-Suggestions)). PSReadLine ignores that file.

## Searching

| Key | |
|---|---|
| `up` `down` | Previous / next entry. Changes to recalled lines are kept while you move |
| `C-r` `C-s` | Incremental search backward / forward (case-sensitive only if you type a capital) |
| `prior` `next`, `C-x C-p` `C-x C-n` | Entries starting with what you typed |
| `C-x p` `C-x n` | Entries containing what you typed |
| `M-<` `M->` | First / last entry |

In vi normal mode, `/`, `?`, `n` and `N` search the history.

## Viewing and clearing

```powershell
ble-history            # all entries, numbered
ble-history 20         # the last 20
ble-history -d 5       # delete entry 5 (negative numbers count from the end)
ble-history -d 3-7     # delete a range
ble-history -w         # write the history in memory to the file
ble-history -c -w      # clear it, in memory and in the file

Clear-BleHistory                 # same as -c -w
Clear-BleHistory -InMemoryOnly   # keep the file
```

PSReadLine uses the same file, so clearing it clears PSReadLine's history too. PowerShell's own
session list (`Get-History`) is separate; `Clear-History` clears that one.
