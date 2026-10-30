# Handoff: features from ble.ps1 to add to a ble.sh fork

You are working in a fork of [ble.sh](https://github.com/akinomyoga/ble.sh) (bash line editor).
ble.ps1 (<https://github.com/nikita-edel/ble.ps1>) is a ble.sh clone for PowerShell written by the
same owner. It started as a 1:1 copy of ble.sh, then gained features that ble.sh does not have.
This document lists those features so they can be added to ble.sh, in ble.sh's style.

The ble.ps1 implementation (C#) is the reference for behavior. File names below are relative to the
ble.ps1 repository. Read them when a detail here is unclear; do not port their code literally,
write idiomatic ble.sh code instead.

## Ground rules from the owner

- Follow ble.sh's conventions: `bleopt/declare` for options, `ble/color/defface` for faces,
  `ble/widget/NAME` widgets, `ble-decode/keymap:NAME/define` keymaps, the idle task framework
  (`ble/util/idle.push-background`, `ble/util/idle.wait-user-input`, `ble/util/idle.sleep-until`),
  and the existing menu/info drawing (`ble/complete/menu#construct`, `ble/edit/info/show`).
- New behavior must be optional and must not change ble.sh's defaults unless stated.
- Before guessing how ble.sh does something, read its source (`lib/core-complete.sh`,
  `lib/core-syntax.sh`, `src/edit.sh`, `src/history.sh`, `lib/keymap.*.sh`).
- Tests: ble.sh's own framework (`ble/test`, `lib/test-*.sh`, `make check`). Also try every
  feature by hand in a real terminal.
- Commits: no `Co-Authored-By` or "Generated with" lines. Ask before pushing.

## Already in ble.sh: do not re-implement

These exist in ble.ps1 only because ble.ps1 copied them from ble.sh:

- the " [candidate] " auto-complete form for words that are not a prefix (types r/m/a/A in
  `ble/complete/auto-complete/source:syntax`)
- marking the last character with `syntax_error` while a nest is open (`core-syntax.sh`,
  `_ble_syntax_attr[iN-1]=ATTR_ERR`)
- all bleopt options, `:=`/`+=`/`-=`, face specs (`copy:`, `rgb:`, `hsl:` ...), vi mode names,
  the emacs `-- MULTILINE --` indicator, `exec_elapsed_*`, `edit_bell`, `history -c`/`-d`/`-w`
- `.blerc` loading (ble.ps1's `Import-BleRc` exists only because PowerShell cannot source bash)
- pasting in vi normal/visual mode with bracketed paste (`vi-command/bracketed-paste`); the
  fallback without the markers is new, see 3d

Everything PowerShell-specific is out of scope: `TabExpansion2`, the command-name index, the
PSReadLine history format, cmdlet parameter checks, the comma-splitting fix for `ble-face`,
keeping `~` in suggested paths (PowerShell expands it, bash completion doesn't), and the
background check for commands of modules that aren't loaded yet.

---

## 1. List view of suggestions (main feature)

Reference: `src/Ble/ListView.cs`, plus the hooks in `src/Ble/Completion.cs` (`UpdateSuggestion`,
`RunIdle`, `CompleteAt`), `src/Ble/Editor.cs` (`Dispatch` → `ListViewKey`, `BuildModel` →
`AddListViewPanels`), `src/Ble/Widgets.cs` (`accept-suggestion*`), tests in
`tests/Ble.Tests/BleShCompatTests.cs` (`ListView*`).

### What it is

An alternative to the gray inline auto-complete, modeled on PSReadLine's `ListView` prediction
style. Below the command line, a list shows candidates for what is being typed: history entries
and completion candidates. The user moves through it with the arrow keys (the command line shows
the selected entry), takes an entry, or keeps typing.

```
$ git s
> git status -s                                                  [History]
  git status                                                     [History]
  git stash list                                                 [History]
(1/3, up/down: select, S-RET: take, C-g: cancel)
```

### Options (new bleopts; defaults in parentheses)

| Option | Default | Meaning |
|---|---|---|
| `complete_auto_view` | `inline` | `inline` = ble.sh's normal auto-complete; `list` = list view instead |
| `complete_auto_view_maxlines` | `10` | Number of entries listed (also capped by the terminal height) |
| `complete_auto_view_source` | `history:completion` | Colon list of sources: `history`, `completion` |
| `complete_auto_view_multiline` | `1` | In multi-line input, the current (last) line is the query |
| `complete_auto_view_completion` | `word` | Completion entries show only the completed word (`word`) or the whole command line (`line`) |
| `complete_auto_view_selected` | `line` | How the selected entry is highlighted: `line` = whole row, `entry` = only the entry's text, `color` = only the text color changes |
| `complete_auto_view_ambiguous` | `1` | In list mode, still show the inline ` [candidate] ` for a word that is not a prefix of anything |

### Faces (new)

| Face | Default | Use |
|---|---|---|
| `list_view_selected` | `ref:menu_complete_selected` (reverse) | Selected entry for `line` and `entry` |
| `list_view_selected_color` | `fg=33,bold` | Selected entry for `color` (no background) |

Reuse `menu_complete_match` for the matched part of each entry, `menu_desc_default` (or a new
face referring to it) for the `[History]`/`[File]` label, and a dim face like ble.ps1's
`menu_info` (`fg=242`) for the counter line.

### When the list is shown

Only when all of these hold: `complete_auto_view=list`, `complete_auto_complete` is on, the line is
not empty, the cursor is at the end of the line, the keymap is `emacs` or `vi_imap`, no mark/region
is active, no completion menu is shown, and no other modal state (isearch etc.) is active. If the
line contains a newline: only with `complete_auto_view_multiline`, and then the query is the text
of the last line (from the start of that line to the end of the buffer). Entries then replace only
that line.

In list mode the normal inline history suggestion is not shown. The completion-based inline
suggestion is shown only in its " [candidate] " form (if `complete_auto_view_ambiguous`). Plain
prefix completions are not shown inline, because the list already has them.

### Entries

1. **History** (immediately, on every change of the query): walk history from newest to oldest.
   Skip entries equal to the query. Entries that start with the query come first; then entries
   that contain it as a substring. Matching is smart-case: case-sensitive only if the query has
   an upper-case letter. Stop at `maxlines` prefix matches. Keep at most `maxlines` substring
   matches. Label: `History`. Within each group, entries are filtered and ordered by the history
   ranking of section 2 (if that is implemented), with up to 100 validity checks per list.
2. **Completion** (after `complete_auto_delay` ms of idle time, as an idle task that is cancelled
   by user input): generate completion candidates for the word at the end of the query, using
   ble.sh's normal candidate generation.
   - Each candidate becomes an entry: the query with the word replaced by the candidate.
   - Skip entries equal to the query or already present as a history entry.
   - Label: `File` for file/directory candidates, otherwise `Completion`.
   - If nothing matches and history gave nothing, try ambiguous matching, but substring only.
     Subsequence matches were too noisy in a list (`lv-b` matched `llvm-lib.exe`).
   - **After a space** (the query ends in whitespace), list only argument values the command's
     completion provides: option values, enum-like values. Plain file names are not listed. In
     PowerShell, `Get-ChildItem -ErrorAction ` lists `Stop, Continue, Ignore, ...`. In bash this
     is what the command's completion function returns for that argument; exclude plain
     filename/directory candidates.
   - With `complete_auto_view_completion=word`, the entry shows only the completed word
     (`this/path/sub1`), not `cp -r this/path/sub1`. The match highlight then applies to the word.
3. **Merging:** history keeps the top rows, but completion entries get at least half of the list:
   `histN = min(#history, max - min(#completion, max(1, max/2)))`, then append completion entries
   up to `max`.

### Drawing

One row per entry, below the command line (the info area or the menu area, whichever fits
ble.sh's layout best; it must coexist with the vi mode line):

```
<marker><text><padding> [<Source>]
```

- marker: `"> "` for the selected entry, `"  "` otherwise.
- text: truncated to fit; the part matching the query is highlighted with `menu_complete_match`.
- the `[Source]` labels are right-aligned in one column.
- selection highlight depends on `complete_auto_view_selected`:
  - `line`: marker, text, padding and label get `list_view_selected`.
  - `entry`: only the text gets `list_view_selected`.
  - `color`: only the text gets `list_view_selected_color`.
  - In all three modes the `> ` marker stays.
- If there are more entries than rows, or an entry is selected, add a counter line:
  `(N/M, up/down: select, S-RET: take, C-g: cancel)` or `(M items, ...)`.
- When the selection moves past the visible rows, scroll so it stays visible.

### Keys (while the list is shown)

| Key | Action |
|---|---|
| `down` | Select the next entry; past the last, return to the typed text (no selection) |
| `up` | Select the previous entry; from "no selection" go to the last entry |
| keys bound to history widgets | Move through the list too, by the widget they are bound to: `…-prev` (e.g. `history-prev`, `backward-line-or-history-prev`) and `history-search-forward`/`history-substring-search-forward` move **up**; `…-next` and `history-search-backward`/`history-substring-search-backward` move **down** (older = down, so a user's `M-j history-search-backward` behaves like vim's `j`) |
| `S-RET` (`accept-suggestion-or-newline`, `accept-suggestion`) | Take the selected entry (or the first one if none is selected). The line becomes that entry, the list is recomputed for the new text, and a second `S-RET` takes the next match. If the list is empty, fall back to the inline suggestion, then to inserting a newline |
| `C-g` | Restore the typed text and hide the list until the text changes |
| `RET` | Normal accept-line (the line already shows the selected entry) |
| typing | Continues from the displayed text; the selection is dropped and the list recomputed |
| `f2` (new widget `toggle-suggestion-view`) | Switch `complete_auto_view` between `inline` and `list` |

While an entry is selected, the list must not be recomputed. Navigating changes the line, but the
list stays the list for the typed query. This was the main bug the owner hit: history-search keys
replaced the line, the list was recomputed for the full entry, came back empty, and disappeared.

### Performance

- The history part must stay cheap on every keystroke (it is a linear scan; fine for 10k entries).
- The completion part must run as an idle task that yields to user input
  (`ble/util/idle.wait-user-input`, `ble/decode/has-input`). A keystroke must never wait for a
  completion that is running.
- Cache completion results per line for the same `(text, cursor)`: the list, the inline
  " [candidate] " and TAB ask for the same thing.

### Test cases (all are unit tests in ble.ps1; port them)

- `git s` with history `git status`, `git status -s`, `git log` lists
  `git status -s`, `git status`.
- `S-RET` takes the first entry. After `C-u`, `git s`, `down down S-RET` takes `git status`, and
  the list continues with `git status -s`; `S-RET` again gives `git status -s`.
- `down`×3 past the end returns to `git`; `up` goes to the last entry; `C-g` restores and hides.
- `M-j`/`M-k` bound to history-search-backward/forward move down/up and keep the list.
- `complete_auto_view_maxlines=2` lists 2 entries.
- `if [[ x ]]; then` + newline + `ech` lists history entries starting with `ech`; `S-RET` replaces
  only the second line.
- With completion candidates `.local/bin`, `.local/lib` and history `ls .local/share`, typing
  `ls .local/` lists the history entry first, then `[File]` entries showing only `.local/bin`.
- After a space, file candidates are not listed; argument values are.
- Selection modes: check which parts of the row carry which face.
- `ls .local/bn` in list mode shows the inline ` [.local/bin] `; `S-RET` takes it when the list is
  empty; with `complete_auto_view_ambiguous=` nothing is shown inline.
- `f2` toggles inline/list.

---

## 2. History suggestions: current directory first, skip what would fail here

Reference: `src/Ble/HistoryRanking.cs` (all the rules, in one place), `src/Ble/History.cs`
(`Add(line, dir)`, `RanIn`, `DirsOf`, the `.dirs` file), `src/Ble/Highlighter.cs` (`IsValid`,
`IsMissingPath`, `WasPathIn`), `src/Ble/Completion.cs` (`UpdateSuggestion`, `NewHistoryRanking`),
tests in `tests/Ble.Tests/HistoryTests.cs` (`Ranking*`, `WordsThatWerePaths*`, `Suggestions*`,
`ListViewLeavesOutInvalidEntries`). User docs: `wiki/Completion-and-Suggestions.md`, "Which
history entry is suggested".

### What it is

ble.sh's inline auto-complete suggests the newest history entry that starts with the typed text.
A command that worked in one directory often makes no sense in another (`cd dev` from `~`, typed
again inside `~/dev`). ble.ps1 picks the entry with these rules, in order:

1. **Drop entries that would fail here** (`complete_auto_history_validate`):
   - `command`: the command name doesn't exist (what the highlighter would show as an error).
   - `path`: an argument is a path that doesn't exist from the current directory. A word counts
     as a path if it looks like one (`./x`, `~/x`, `/x`, or `dir/x` where `dir` is an existing
     directory; the same rule as 3c `IsMissingPath`), or if it was an existing file or directory
     in one of the last 4 directories the entry ran in (`WasPathIn`). So `cd dev`, run in `~`, is
     not suggested in `~/dev`, and `vim notes.md` is not suggested where `notes.md` is missing,
     but `git checkout origin/main` stays (it was never a path).
   - Words with variables or glob characters (`$HOME/x`, `*.txt`) and URLs are not checked.
2. **Entries that ran in the current directory come first** (`complete_auto_history_cwd`).
3. **Then newest first**, as before.

If no entry passes, there is no history suggestion, and the completion-based suggestion takes
over (which only offers paths that exist).

### Options (new bleopts)

| Option | Default | Meaning |
|---|---|---|
| `complete_auto_history_cwd` | `1` | Prefer entries that ran in the current directory |
| `complete_auto_history_validate` | `command:path` | Colon list of checks: `command`, `path`; empty = off |

For the fork, default both to off (empty / `0`) so ble.sh's behavior doesn't change, and say in the
docs that the owner uses `1` and `command:path`.

### Where each entry ran

History files don't record the directory. ble.ps1 writes a second file next to the history,
`<history file>.dirs`, with one record per line: `directory TAB entry` (the entry escaped like
in the history file, newlines as an escape). It is appended when a command runs, read on
startup, picked up from other sessions at the next prompt (like the history itself), and
rewritten when the history is rewritten (deleted entries lose their directories). Per entry, the
last 16 directories are kept. Entries the history doesn't save (ignored patterns, leading space)
don't get a record either. Directories are compared case-insensitively on Windows only;
normalize trailing separators.

In ble.sh this would be a file under ble.sh's state directory (or next to `$HISTFILE`), written
from the `PRECMD`/`ble/exec` hooks with `$PWD` at the time the command started. It must
tolerate `cd` inside the command itself (record the directory where the command started).

### Performance rules

- The checks touch the file system, so cap them: at most 50 checked entries for the inline
  suggestion and 100 for the list view, per keystroke. Entries after the cap are left out.
- Cache the results of command and path lookups for the current command line; reset the caches
  at each new prompt (files and commands may have changed).
- `Best`: walk candidates newest first; the first valid entry from the current directory wins
  immediately; otherwise remember the first valid entry from elsewhere and keep looking only for
  one from here (entries from elsewhere after it aren't checked).
- `Top(max)` (list view): same order; once the list is full, only entries from the current
  directory can still get in, pushing out the last entry from elsewhere.

### Test cases (port from HistoryTests.cs)

- History `cd a` (ran in `/work/a`), `cd b` (`/work/b`), `cd c` (no directory): typing `cd ` in
  `/work/a` suggests `a`; in `/work/b/` (trailing slash) `b`; elsewhere `c`; with
  `complete_auto_history_cwd=0`, `c`.
- A validator rejecting `cd a` and `cd c` in `/work/a` gives `cd b`; rejecting everything gives
  nothing; with a cap of 2 the validator runs exactly twice.
- `cd dev` ran in `~` where `dev` existed: not suggested in `~/dev` (no `~/dev/dev`), suggested
  again in `~`. `git checkout origin/main` from `~` is suggested anywhere.
- An unknown command in history is skipped with `command`, kept with `path` only.
- The list view leaves out the same entries.
- The `.dirs` file: written on add, read by a second history object (another session), rewritten
  after deleting entries.

---

## 3. Smaller additions (optional, lower priority)

### 3a. `S-RET` inserts a newline when there is no suggestion

In ble.sh, `S-RET` is bound in the `auto_complete` keymap (insert the suggestion) and in `vi_nmap`
(`vi-command/forward-first-non-space`), but not in `emacs` or `vi_imap`. ble.ps1 binds `S-RET` in
`emacs` and `vi_imap` to `accept-suggestion-or-newline`: accept the suggestion (or list entry) if
there is one, otherwise insert a newline. This is useful for multi-line editing without remembering
`M-RET`/`C-v C-j`.

### 3b. `C-M-j` switches between emacs and vi

Bind `C-M-j` to `vi-editing-mode` in `emacs` and to `emacs-editing-mode` in `vi_imap` (these
widgets exist in `src/edit.sh`).

### 3c. Warn about path arguments that do not exist

ble.sh uses `filename_warning` only for redirections that would overwrite a file. ble.ps1 also
marks *arguments that look like paths but do not exist* with `filename_warning` (underline, red).
Rule from `src/Ble/Highlighter.cs` (`IsMissingPath`):

- A word is path-like if it contains `/`, starts with `.` or `~`, or looks like `C:`.
- Words with glob characters or `$` are skipped, and so is `..`.
- An *anchored* word (starts with `.`, `~`, `/`) is marked if the file does not exist.
- An unanchored word like `src/foo` is marked only if its first segment (`src`) is an existing
  directory, so `origin/main` or `user/repo` is not flagged.

Make it a bleopt (e.g. `highlight_filename_missing`, default off) so ble.sh's default rendering
stays the same.

### 3d. Pastes in vi normal mode without bracketed paste

ble.sh handles a paste in `vi_nmap`/`vi_xmap`/`vi_smap` through bracketed paste
(`vi-command/bracketed-paste`: insert after the cursor, or replace the selection, then back to
normal mode). When the markers don't arrive, every pasted character runs as a vi command
(`echo hi` → `e`, `c`, `h`, ... and the rest lands in insert mode). That happens when
`term_bracketed_paste_mode` is off (`set enable-bracketed-paste off` in inputrc, or eterm, where
ble.sh turns it off itself), in terminals without DECSET 2004, and with tools that "type" the
clipboard (password managers, `xdotool type`). Git Bash and WSL in Windows Terminal do get the
markers (checked), so this is a fallback, not a common case.

ble.ps1 had to solve it because Windows consoles drop the markers before `ReadKey`. Reference:
`TakeViPaste` / `ViPaste` in `src/Ble/ViExtra.cs`, the `_keyAfterIdle` flag in `src/Ble/Editor.cs`
(`NextKey`), tests `Paste*` and `TypedKeysInNormalModeStayCommands` in
`tests/Ble.Tests/ViTests.cs`, e2e `test_vi_paste_in_normal_mode`.

The rule: a paste puts all its characters in the input at once, typed keys come one at a time.
So, in a vi command mode, when the editor was idle (waiting for input) and the first key is
followed by more input that is already there, read everything available (wait ~5 ms once more
for a paste that arrives in pieces). If that is 3 or more keys and all are text (printable,
RET/C-m/C-j as newline, TAB), call the same `.proc` as `vi-command/bracketed-paste` with those
characters. Otherwise feed the keys back as normal input. Keys that pile up while the editor is
busy (not idle) never count as a paste, so fast typing during a slow redraw stays commands.

In ble.sh the pieces exist: `ble/decode/has-input` tells whether input is pending, and the
decoder already batches input that arrives together. Make it a bleopt (e.g.
`decode_paste_detect`, default off, or on only when `term_bracketed_paste_mode` is off). Known
limit: a paste of 1-2 characters can't be told apart from two keys pressed together.

---

## 4. What to hand back

- A branch with the list view (section 1), with options, faces, widget and tests.
- The history ranking (section 2) as its own commits, with options, the directory file and tests.
- Each optional item from section 3 as its own commit, so the owner can pick.
- README/doc updates in ble.sh's style (`docs/` and the blerc template `blerc.template`, which
  lists every option with a comment).
- A short list of anything that turned out impossible or different in bash, and why.
