# CLAUDE.md: notes for continuing work on ble.ps1

ble.ps1 is a ble.sh clone for PowerShell 7.4+: a C# line editor (binary module) behind a thin
PowerShell module. It replaces `PSConsoleHostReadLine`. Goal set by the owner: **behave like
ble.sh 1:1** (bindings, faces/colors, highlighting rules), adapted to PowerShell where bash
concepts don't exist. README.md is deliberately short; user documentation lives in `wiki/` (the source of the GitHub
wiki, published by the owner; see NOTES.md). Document user-facing changes there, in plain
language (the owner does not want docs that read as AI-generated).
NOTES.md is the owner's personal notes (install, config, keys); keep it updated for user-facing changes.

## Build / test / install

```powershell
./build.ps1                 # Release build -> out/ble (psd1, psm1, Ble.dll)
./build.ps1 -Test           # + xUnit (tests/Ble.Tests) + Pester (tests/Module.Tests.ps1)
./build.ps1 -E2E            # + real-terminal tests (pwsh in ConPTY via pywinpty, screen via pyte)
./build.ps1 -Package        # out/ble-<version>.zip: install.ps1, LICENSE, ble/
./build.ps1 -Install        # runs install.ps1 -Path out/ble -> Documents\PowerShell\Modules\ble\<version> (removes other versions)
```

- Version: only `ModuleVersion` in module/ble.psd1. Ble.csproj reads it (regex in `<Version>`), the psm1
  reads it back from Ble.dll for `$BLE_VERSION`/`ble version` (the module's own Version is 0.0 while the psm1 loads).
- Testing install.ps1: pass `-Destination <scratch dir>`. A child `pwsh` adds the real user module path
  back to the front of `PSModulePath`, so setting `$env:PSModulePath` does not keep it away from the real install.

- .NET SDK lives in `~/.dotnet` (not on PATH by default; build.ps1 adds it). From bash:
  `export PATH="$HOME/.dotnet:$PATH"; dotnet test tests/Ble.Tests -nologo`
- Python deps for e2e: `pip install -r tests/e2e/requirements.txt` (installed already).
- fzf is installed via winget at `~/AppData/Local/Microsoft/WinGet/Links`. That directory may be
  missing from PATH in an existing shell; the fzf e2e test skips if fzf isn't found.
- Expected state: all green (200 unit, 28 Pester, 24 e2e). Always run `-Test` (and `-E2E` for
  rendering/terminal changes) before committing.
- Debugging tools: `python tests/e2e/probe.py "text" "k:TAB" "k:RET"` drives a -NoProfile session
  and prints the screen. `python tests/e2e/probe_profile.py "text"` uses the owner's REAL profile
  and prints each cell's fg/bg color. Use it to verify highlighting as the owner sees it.

## Layout

```
module/ble.psm1         attach/detach, prompt wrapper (preserves $? via Write-Error -EA Ignore), PSConsoleHostReadLine
module/ble.psd1         exports (add new cmdlets/aliases here AND [Alias] on the cmdlet)
module/default.blerc    copy of the owner's $ps_config\.blerc; New-BleRc copies it out (keep it loading with 0 warnings)
src/Ble/Session.cs      static entry: ReadLine(engine, lastStatus), exec/elapsed marks, history file
src/Ble/Editor.cs       line state, main loop (NextKey/ProcessKey/Dispatch), undo, kill ring, BuildModel, edit commands
src/Ble/Widgets.cs      emacs widgets + history + isearch; static ctor registers all widget groups
src/Ble/WidgetsBle.cs   widgets added to match ble.sh default bindings (sword, zap, macros, M-x, dabbrev, ...)
src/Ble/Completion.cs   Menu, TAB logic, ambiguous (fuzzy) completion, auto-suggestions (incl. ble.sh's " [cand] " form), idle tasks
src/Ble/ListView.cs     PSReadLine-like list view of history matches (bleopt complete_auto_view=list, f2)
src/Ble/BleRc.cs        Import-BleRc: .blerc reader (bash subset: lexer, parser, executor) + BleModules (ble-import built-ins)
src/Ble/Vi.cs           vi parser (ViKey/ViParse/ViMotion/ViOperator/ViSimple/ViVisual), registers, dot-repeat
src/Ble/ViExtra.cs      block visual, q/@ macros, marks, ex (:) commands, select mode (vi_smap)
src/Ble/Highlighter.cs  token/AST highlighting with ble.sh rules (see class doc comment); IsValid = command/path
                        checks for history suggestions (+ words that were paths where the entry ran)
src/Ble/History.cs      PSReadLine-format history + "<file>.dirs" (dir TAB entry): RanIn, DirsOf
src/Ble/HistoryRanking.cs  ALL rules for which history entries are suggested (inline Best, list view Top):
                        validate -> current dir first -> newest. Options are read in Editor.NewHistoryRanking.
                        No per-command special cases (the owner rejected a hardcoded cd list).
src/Ble/PromptSequences.cs  \u \w \q{...} expansion, status line
src/Ble/Fzf.cs          optional fzf widgets + ** completion trigger (FzfRunner hook for tests)
src/Ble/Renderer.cs     relative-cursor ANSI drawing, explicit row breaks, resize estimate
src/Ble/Keys.cs         ConsoleKeyInfo -> canonical names ("C-M-x"), keyspec parsing
src/Ble/Keymap.cs       keymaps; Reset() mirrors ble.sh: BindCommon/History/Complete/Arg, BindEmacs, BindViInsert(+Meta)
src/Ble/Options.cs      bleopt defaults (ble.ps1's, then every ble.sh option; most of those are accepted no-ops),
                        ble-face defaults, color names; Options.Apply / Faces.Apply parse ble.sh's syntax
src/Ble/Shell.cs        IShell (PowerShell interop) + PsShell; tests use FakeShell
src/Ble/Cmdlets.cs      ble-bind, bleopt, ble-face, ble-sabbrev, ble-import, Import-BleRc, New-BleRc, ble-history,
                        Clear-BleHistory, Register-/Invoke-BleWidget
tests/Ble.Tests/        Harness.cs (drive Editor by key names), Fakes.cs (FakeTerminal/FakeShell/VtScreen), *Tests.cs, FuzzTests.cs
```

## Conventions and gotchas (learned the hard way)

- **Backslashes get mangled in bash heredocs and inline `python - <<EOF` scripts in this tool
  environment** (`\\` collapses, `\n` becomes a real newline, a `'` inside can break the heredoc).
  For code containing backslashes or quotes, write the file or script with the Write tool (use
  raw strings `r'''...'''` in Python), or use the Edit tool. After a scripted edit, grep the
  result for `\\`, `\n` and `'\'`. The same happens to `sed -i` replacements and quoted
  `python - <<'EOF'` heredocs that contain `\\`.
- PowerShell's host already moves the prompt to a new line after `-NoNewline` output, so
  ble.sh's `prompt_eol_mark` cannot be detected (checked in ConPTY); the option is accepted only.
- `.blerc` support (`BleRc.cs`) expands words when a command runs, not while lexing, so `$VAR`
  sees earlier assignments. Anything bash it cannot evaluate must become a warning, never an error.
- The repo uses LF (`.gitattributes`); `*.ps1/psm1/psd1` use CRLF. The CRLF warnings from git are harmless.
- Key names are canonical: modifier order `C-` `M-` `S-`, so `M-C-y` is spelled `C-M-y`.
  `C-m`=RET, `C-i`=TAB, `C-[`=ESC, `C-SP`=`C-@`, Backspace=`DEL`, Delete=`delete`,
  `M-S-f` = `M-F`. In tests, `Harness.Keys()` takes canonical names verbatim.
- Performance: never call `GetCommand` per keystroke for names that may not exist (~20 ms each;
  PsShell.ResolveCommand uses session lookups + `CommandIndex`). TabExpansion2 must run on the
  pipeline thread, so it can't be made async; `Editor.CompleteAt` caches per line and passes a
  cancel callback for idle work (PowerShell.BeginStop from a timer; script completers stop, the
  filesystem completer does not). Measure with `tests/perf/bench.ps1` before and after changes.
- The harness turns `complete_auto_history_validate` off (FakeShell knows few commands); tests of it set it
  and add commands to `S.Commands` (e.g. `cd`).
- Static global state (Options, Faces, Keymaps, KillRing, Abbrevs, Registers, Macros,
  UserWidgets) is reset by `Harness.ResetGlobals()`. Test parallelization is disabled
  (assembly attribute in Fakes.cs). The harness also turns off `edit_paste_newline` and
  `complete_auto_completion`.
- Undo: `AfterWidget` pushes a snapshot when the text changed (self-insert runs are merged). Vi
  manages its own: a whole insert session is one step (`_viInsertUndo`), `_viReplaying`
  suppresses nested pushes during `.`/`@`/`:normal`. Add new self-managing widgets to `NoUndoWidgets`.
- Highlighting: ble.sh faces only. Find ble.sh behavior in its source rather than guessing
  (download: `curl -sL https://github.com/akinomyoga/ble.sh/archive/refs/heads/master.tar.gz | tar xz`;
  faces: `grep -r "ble/color/defface"`; bindings: `src/edit.sh` bind-common, `lib/keymap.emacs.sh`,
  `lib/keymap.vi.sh`; highlighting: `lib/core-syntax.sh`).
- PowerShell interop must run on the pipeline thread via `PowerShell.Create(RunspaceMode.CurrentRunspace)`
  (see PsShell). Don't call PowerShell from background threads.
- The fuzz tests must never hit widgets that launch external programs (`C-x C-e` is unbound
  there, because it opened Notepad and hung the test run). Never kill processes; the environment denies it.
- Windows locks a loaded Ble.dll: reinstalling the same version renames it to `Ble.dll.old-*`; running sessions keep the old version.
  Other versions' folders that are still loaded stay until a later install.
- Commits: no Co-Authored-By / attribution lines (the owner asked; global Claude settings
  disable them too). Work on `main` (the owner's choice).

## Owner's environment

- Profile: `Documents\PowerShell\Microsoft.PowerShell_profile.ps1` dot-sources
  `$ps_config\*.ps1` (= `Documents\PowerShell\Config`): variables, setup (PSReadLine settings and
  starship), alias, functions, **ble.ps1** (the ble config: vi mode, cursor shapes, Alt+J/K
  history search, `ble-bind -m vi_xmap -s 'Y' '" + y'` for clipboard, keymap_vi_mode_show disabled).
- The owner uses vi mode, Windows Terminal, and starship. German Windows (error messages in German).
- GitHub remote: `git@github.com:nikita-edel/ble.ps1.git`.

## Open items

- The cleaned history (no Co-Authored-By) was force-pushed by the owner. Pushing is the owner's
  job: this shell has no SSH access to GitHub (`git fetch` fails), so ask before any push and
  let the owner run it.
- CI (`.github/workflows/ci.yml`) runs on GitHub's Windows/Linux/macOS runners. The first run
  failed only because an e2e test depended on installed modules (fixed: tests must not depend on
  the machine's modules or files; define what they need in `make_term(setup=...)`).
- Ideas not done yet: ble.sh contrib modules beyond fzf; more context completions (`M-~` user,
  `M-@` host are n/a on Windows); a `build.ps1 -Uninstall` switch; Linux/macOS testing (CI exists
  in `.github/workflows/ci.yml` but was never run); publishing to the PowerShell Gallery (see NOTES.md).
