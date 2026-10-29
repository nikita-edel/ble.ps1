# Notes

## How `Install-Module` works

**Where modules come from.** PowerShell has a built-in package manager: PowerShellGet
(`Install-Module`) and its newer successor PSResourceGet (`Install-PSResource`). By default it
downloads from the **PowerShell Gallery**, <https://www.powershellgallery.com>, which is
Microsoft's public NuGet-style repository.

```powershell
Get-PSRepository                 # configured repositories (PSGallery + its URL)
Find-Module posh-git             # search the gallery
Register-PSRepository ...        # add another (e.g. a private company) repository
```

**What `Install-Module Name -Scope CurrentUser` does.** It downloads the package (a `.nupkg`,
which is a zip file) and extracts it into a versioned folder in your user module path:

```
Documents\PowerShell\Modules\Name\1.2.3\
```

`Import-Module Name` then finds it by name. No admin rights are needed with `-Scope CurrentUser`.
Related commands: `Update-Module`, `Uninstall-Module`, `Get-InstalledModule`.

## How ble.ps1 is installed today

`./build.ps1 -Install` and the release zip's `install.ps1` do the same final step by hand: they
copy the module to `Documents\PowerShell\Modules\ble\<version>\`, the same folder
`Install-Module` would use, and delete other installed versions.

- A version that a running pwsh has loaded can't be deleted; it is removed by a later install.
  Reinstalling the same version renames the loaded DLL to `Ble.dll.old-*`. Running sessions keep
  the old version until they are restarted.
- `ble version` (or `$BLE_VERSION`) shows the version that is loaded.
- **Unload:** `ble-detach` (turns it off; `ble-attach` turns it back on) or `Remove-Module ble`.
- **Uninstall:** remove the `Import-Module ble` line from your config, close all pwsh windows,
  then delete `Documents\PowerShell\Modules\ble`.

## Versions and release zips

- The version is `ModuleVersion` in `module/ble.psd1`, and nowhere else. `Ble.csproj` reads it
  from there, so `Ble.dll` always has the same version. To change it, edit that line.
- `./build.ps1 -Package` makes `out/ble-<version>.zip` with `install.ps1`, `LICENSE` and the
  `ble/` module folder. Users extract it and run `pwsh -ExecutionPolicy Bypass -File .\install.ps1`.
  (Bypass is needed because downloaded scripts are blocked; install.ps1 unblocks the module files.)
- `./build.ps1 -Install` runs the same `install.ps1`, on `out/ble`.
- A release: change `ModuleVersion`, `./build.ps1 -Test -E2E -Package`, commit, `git tag vX.Y.Z`, push
  with `--tags`, then create a GitHub release for the tag and attach the zip.

## How you could publish ble.ps1 to the PowerShell Gallery

The gallery doesn't take the zip. You give it the module folder (`out/ble`) and it builds its own
package (a `.nupkg`) from it, with the same three files. Users then install with `Install-Module`,
and install.ps1 isn't involved.

1. Create an account on powershellgallery.com and generate an API key.
2. Build and test: `./build.ps1 -Test`.
3. Publish: `Publish-Module -Path ./out/ble -NuGetApiKey <key>` (or, with the newer PSResourceGet,
   `Publish-PSResource -Path ./out/ble -ApiKey <key>`). The name, version, description
   and tags come from `module/ble.psd1`. The gallery refuses a version it already has, so bump
   `ModuleVersion` for each release.
4. After that, anyone can run `Install-Module ble -Scope CurrentUser` and `Update-Module ble`.
   The name "ble" was still free on the gallery (checked 2026-10-28); whoever publishes first
   gets it.
5. Both ways use the same folder layout (`Modules\ble\<version>\`), so they don't get in each
   other's way. `Import-Module ble` loads the highest version, and install.ps1 removes the others.

## Configuration

Your config lives in `$ps_config\ble.ps1` (`Documents\PowerShell\Config\ble.ps1`), which is
dot-sourced from `$PROFILE` after starship. Useful commands:

```powershell
bleopt                 # list all options (bleopt 'complete_*' to filter)
ble-face               # list all faces (colors)
ble-bind -P            # list key bindings;  ble-bind -L  lists widget names
ble-bind -k            # press a key to see its name
```

**The default .blerc.** `module/default.blerc` is a copy of `$ps_config\.blerc`. It ships with the
module, and `New-BleRc <path>` copies it out (`~/.blerc` without a path, `<dir>\.blerc` for a
folder; `-Force` replaces an existing file). It's a snapshot: when you change your own `.blerc`
and want the default to follow, copy it over `module/default.blerc` again.

## Suggestions and completion keys (vi insert mode)

| Key | Effect |
|---|---|
| `Shift+Enter`, `→`, `Ctrl+F`, `End`, `Ctrl+E` | accept the gray suggestion |
| `Alt+F`, `Ctrl+→` / `Alt+→` | accept one word / one whitespace-delimited word |
| `Ctrl+J`, `Ctrl+Enter` | accept the suggestion and run |
| `Ctrl+G` | dismiss the suggestion |
| `Tab`, then `Tab`/`Shift+Tab`/arrows | completion menu, then cycle through it; `Enter` takes it, `Esc` cancels |
| `PgUp`/`PgDn`, `Alt+K`/`Alt+J` (your config) | older/newer history entry with the typed prefix |
| `Ctrl+R` / `Ctrl+S` | incremental history search (press again for the next match) |
| `"+y`, `"+p`, visual `Y` (your config) | copy to / paste from the system clipboard |

**Which history entry is suggested** (both on by default):

- `bleopt complete_auto_history_cwd=1`: commands you ran in the current folder win over newer
  ones from elsewhere. The folders are stored in `ConsoleHost_history.txt.dirs` next to the
  history file, starting from now (older entries have no folder).
- `bleopt complete_auto_history_validate=command:path`: entries with a command or a path that
  doesn't exist here are skipped. A word is a path if it looks like one, or if it was a file or
  folder where the command ran (`cd dev` from `~` isn't suggested in `~\dev`). `path` alone
  checks only paths; empty turns it off. The rules are listed in `src/Ble/HistoryRanking.cs`.

## Behavior that changed to match ble.sh (Oct 2026)

- **Enter in a multi-line buffer** inserts a newline (ble.sh `accept-single-line-or-newline`);
  run it with `Ctrl+J` / `Ctrl+Enter`. A single complete line still runs with Enter.
- **Vi normal mode:** `Ctrl+C` no longer discards the line; it shows "Type :q and press <Enter>
  to exit". `:q` exits PowerShell, as in ble.sh. To clear the line use `S` or `cc` (then Esc),
  or `0D`.
- **Vi insert mode:** `Ctrl+C` returns to normal mode (ble.sh), it does not discard.
- **Redo** is `C-x U` (emacs); `C-x C-r` is `dabbrev-expand`. In vi normal mode redo stays `Ctrl+R`.
- **Colors** are ble.sh's defaults (`ble-face` lists them). Variables: `$` is purple-ish (133); the
  name shows the variable's state: orange = set, pink-bold = environment, gray = unset, green = new.

## Optional: fzf

fzf is **not required**. It is only used if you enable the integration:

```powershell
winget install junegunn.fzf                 # once
ble-import integration/fzf-key-bindings     # C-t files, M-c cd, C-r history through fzf
ble-import integration/fzf-completion       # word**<Tab> opens fzf
```

Without fzf installed those keys just show "fzf not found". `$env:BLE_FZF` can point to a
specific fzf executable. fzf's own `FZF_DEFAULT_OPTS`, `FZF_CTRL_T_OPTS`, `FZF_ALT_C_OPTS` and
`FZF_CTRL_R_OPTS` are honored.

## New things to try

- Vi: `C-v` block visual (`I`/`A` edit all lines), `qa…q` + `@a` macros, `ma` + `` `a `` marks,
  `:%s/old/new/g`, `:'<,'>normal A;`, `gh` select mode.
- Completion: typos and fragments match (`gci`, `childit` -> `Get-ChildItem`).
- Status line, ble.sh style: `bleopt keymap_vi_mode_show= prompt_status_line='\q{keymap:vi/mode-indicator}'`
  (this is the commented-out alternative from your old .blerc, and it works now).
- Own widgets: `Register-BleWidget name { ... $READLINE_LINE ... }` or `function ble/widget/name { }`.

## New in this round (Oct 2026)

- **Unclosed quotes/brackets:** besides the opener, the last character is now red too (ble.sh).
- **Typo / fuzzy suggestions:** `ls .local/bn` shows `[.local/bin]` after the cursor (ble.sh's
  ambiguous auto-complete). Accept it like any suggestion (`Shift+Enter`, `→`, `End`); `Alt+F` takes
  one word of it. It comes from `TabExpansion2`, so it appears after `complete_auto_delay` (100 ms).
- **List view (PSReadLine style):** `F2` toggles between the gray inline suggestion and a list of
  matching history entries below the line. `↑`/`↓` pick one, `Enter` runs it, `Ctrl+G` goes back.
  Make it the default with `bleopt complete_auto_view=list`.
- **Your old .blerc:** `Import-BleRc ~/.blerc` (or just `Import-BleRc`) applies its bleopt /
  ble-face / ble-bind / ble-sabbrev lines; bash-only parts are listed as warnings. Put it in
  `Config\ble.ps1` after `Import-Module ble`; settings after it override the .blerc.
- **History:** `ble-history` (list, `-d 5`, `-d 3-7`, `-c`, `-w`) and `Clear-BleHistory` (wipes
  memory and the history file, which PSReadLine shares; `-InMemoryOnly` keeps the file;
  `-WhatIf` shows what it would do). `Clear-History` is PowerShell's separate `Get-History` list.
- **Colors:** cmdlets are red because ble.sh colors builtins red (`command_builtin`). Change with
  e.g. `ble-face command_builtin=fg=33`.
- `bleopt X+=Y` now adds to a colon-separated list (ble.sh); it no longer appends text.
- **List view details:** `Shift+Enter` takes the highlighted (or first) entry and keeps listing for
  the new text, e.g. take `ls .local/` and the files inside show up as `[File]` entries.
  `Alt+J`/`Alt+K` (your history-search keys) move down/up in the list like the arrows.
  Options: `complete_auto_view_maxlines=10` (rows), `complete_auto_view_source=history:completion`
  (drop `completion` to list history only), `complete_auto_view_multiline=1` (in multi-line input,
  the current line is the query; `=` turns that off).
- **List view, part 2:** file/argument entries show only the completed word
  (`complete_auto_view_completion=word`; `line` shows the whole command line). After a space the
  list shows argument values PowerShell knows: `-ErrorAction ` lists Stop, Continue, ... (enums,
  ValidateSet, argument completers).
- **List selection color:** `bleopt complete_auto_view_selected=line` (whole row) or `text` (only
  the entry's text; the `>` stays), color via `ble-face list_view_selected=fg=black,bg=153` etc.
- **List selection modes** (renamed): `line` (whole row), `entry` (was `text`: the entry's text gets
  `list_view_selected`), `color` (only the text color changes: `ble-face list_view_selected_color=fg=green`).
  In list view the ` [correction] ` for a wrong word is shown inline again (`complete_auto_view_ambiguous`);
  `Shift+Enter`/`End`/`→` take it when the list has nothing.
- `ble-face name=fg=33,bold` works unquoted now (PowerShell splits at the comma; ble.ps1 joins it
  back). Hex colors need 3 or 6 digits: `#fff` / `#ffffff`.
- **Performance (Oct 2026):** typing a command name no longer lags (unknown-name lookups went from
  ~20 ms to 0.03 ms per key; echo latency 40-65 ms -> ~21 ms in a test terminal), suggestions and list
  entries from completion come after 100 ms instead of 300 ms, a key press stops a slow script
  completer, and idle CPU is halved. Slowness that remains is PowerShell's own completion in huge
  folders (e.g. ~90 ms for C:\Windows\WinSxS), which cannot be interrupted.

## Publishing the wiki

The wiki pages live in `wiki/` in this repo (GitHub wiki format: `Home.md`, `_Sidebar.md`, links
by page name). GitHub keeps a wiki in its own git repository:

1. Once: on GitHub, open the repo's **Wiki** tab and create any first page (this creates the wiki
   repository; it will be overwritten).
2. Then, and after every change to `wiki/`:

```powershell
git clone git@github.com:nikita-edel/ble.ps1.wiki.git ../ble.ps1.wiki
Copy-Item wiki/*.md ../ble.ps1.wiki/ -Force
cd ../ble.ps1.wiki; git add -A; git commit -m "Update wiki"; git push
```
