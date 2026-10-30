## Suggestions

While you type, ble.ps1 suggests how the line could continue, shown in gray after the cursor.
The suggestion comes from history first. If history has nothing, PowerShell's completion is asked
after a short pause (`complete_auto_delay`, 100 ms).

| Key | |
|---|---|
| `S-RET`, or `right` / `C-f` / `end` / `C-e` at the end of the line | Accept it |
| `M-f`, `C-right` | Accept one word |
| `M-right` | Accept up to the next space |
| `C-j`, `C-RET` | Accept and run |
| `C-g` | Hide it until the line changes |

When the word you typed is not the start of anything but something similar exists, the
suggestion shows the replacement in brackets, as in ble.sh:

```
PS> ls .local/bn [.local/bin]
```

Accepting it replaces the word.

Paths you start with `~` or a variable stay that way: PowerShell completes `~/de` to the full
path (`C:\Users\you\dev`), but the suggestion and the list show `~/dev`. The same goes for
`$ps_config\a` (shown as `$ps_config\alias.ps1`), `${name}\...` and `$env:APPDATA\...`. When the
path needs quotes, double quotes are used, because a variable in single quotes isn't expanded.
TAB still inserts what PowerShell returns.

### Which history entry is suggested

ble.sh suggests the newest entry that starts with what you typed. ble.ps1 picks more carefully,
because a command that worked in one folder often makes no sense in another:

- **Commands you ran in the current folder come first.** If you typed `cd ` in `~\dev`, you get
  the newest `cd ...` you ran in `~\dev`, even when you ran another `cd` somewhere else later.
  Turn it off with `bleopt complete_auto_history_cwd=0`.
- **Entries that wouldn't work here are skipped:** a command that doesn't exist (`command`) or a
  path that doesn't exist from the current folder (`path`). A word counts as a path when it
  looks like one (`.\x`, `~\x`, `C:\x`), or when it was a file or folder in the folder where you
  ran the command. So `cd dev`, run in `~`, is not suggested in `~\dev`, and neither is
  `code notes.md` in a folder without `notes.md`, while `git checkout origin/main` stays.
  If no entry fits, you get PowerShell's completion instead, which only offers real paths.
  Arguments with variables or wildcards (`$HOME\x`, `*.txt`) are not checked.
  `bleopt complete_auto_history_validate=path` checks only paths,
  `bleopt complete_auto_history_validate=` turns it off.

Both work in the list view too.

**The same command is listed once.** `cd ~/dev`, `cd ~\dev\`, `cd C:\Users\you\dev` and
`CD ~/Dev` all go to the same place, so the list view shows only the best of them. This doesn't
apply to inline suggestions, which only ever show one entry. `bleopt complete_auto_history_merge`
says what counts as the same:
`path` (paths that point to the same place from the current folder: in `~`, `dev\ble.ps1` is
`~/dev/ble.ps1`; also `/` and `\`, `~` and your home folder, `.\`, `..\`, `\` at the end, quotes)
and `case` (upper and lower case). A word without `/`, `\`, `~` or a leading `.` only counts as
a path when it exists in the current folder, so `cd dev` is `cd ~/dev` in `~`, but `git status`
stays a word. The default is `path:case`; `''` lists every entry.

**Similar commands** (off by default): `bleopt complete_auto_history_fuzzy=1`. When nothing else
is suggested, history entries with small typos are looked for. `gti stauts` finds `git status`,
inline as ` [git status] ` and in the list view as `[Similar]` entries. To keep it from guessing
wildly, the command word has to match, the words have to come in the same order, and only a few
typos per word count (none in words of 1-2 letters, 1 in 3-5 letters, 2 in longer ones).
Commands you run often come first, so an old typo in your history doesn't beat the command you
meant: each doubling of how often you ran an entry outweighs one typo. These
entries are checked like the others, with their own option:
`complete_auto_history_fuzzy_validate` (default `command:path`, same values as
`complete_auto_history_validate`).

The folders come from your history since the feature exists: ble.ps1 writes them to a second
file next to the history file (`ConsoleHost_history.txt.dirs`), because PSReadLine's format
has no room for them. Older entries have no folder: they count as "from somewhere else", and a
plain word in them (like `dev`) is only checked when it looks like a path.

## List view

`bleopt complete_auto_view=list`, or `f2` to switch, shows suggestions as a list below the line
instead of inline, similar to PSReadLine's list view:

```
PS> git s
> git status -s                                        [History]
  git status                                           [History]
  git stash list                                       [History]
```

- History entries come first: those that start with what you typed, then those that contain it.
- After the pause, PowerShell's completion adds entries: files (`[File]`) and other completions
  (`[Completion]`). After a space, only argument values are listed. For example,
  `Get-ChildItem -ErrorAction ` lists `Stop`, `Continue`, ...
- `up` and `down` move through the list and the line shows the selected entry. Keys bound to
  history search (for example `M-j`/`M-k`) move through it too.
- `S-RET` takes the selected or first entry, and the list continues for the new text.
- `C-g` goes back to what you typed.
- In multi-line input, the list is for the line you are on.

Related options: `complete_auto_view_maxlines`, `complete_auto_view_source`,
`complete_auto_view_completion`, `complete_auto_view_selected` and the faces `list_view_selected`
and `list_view_selected_color` ([Options](Options)).

## Menu completion

`TAB` completes as far as the matches agree and shows the matches. A second `TAB` cycles through
them: `TAB`/`S-TAB`/arrows/`C-n`/`C-p`/`prior`/`next` select, `RET` accepts, `C-g` or `ESC`
cancels, and any other key accepts and continues. Typing while the menu is open filters it.

If nothing matches as a prefix, substring and then fuzzy matches are tried, so `childit` finds
`Get-ChildItem` (`bleopt complete_ambiguous=` turns this off).

Menu styles: `bleopt complete_menu_style=align` (columns), `linewise`, or `desc` (with the
parameter type or description).

Specific completions: `M-/` files, `M-$` variables, `M-!` commands, `M-g` glob, `M-TAB` words
from history, `C-x C-r` words from the current line and history.

## fzf

With [fzf](https://github.com/junegunn/fzf) installed:

```powershell
ble-import integration/fzf-key-bindings   # C-t insert files, M-c cd into a directory, C-r history
ble-import integration/fzf-completion     # word** then TAB opens fzf
```

## Speed

Completion comes from PowerShell itself (`TabExpansion2`). Usually it takes about a millisecond;
completing command names takes 10-30 ms, and very large folders take longer. ble.ps1 only asks
after you pause, caches the answer for the current line, and stops slow script-based completers
(git, winget, ...) when you press a key. See [Troubleshooting](Troubleshooting#performance).
