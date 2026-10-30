## Building and testing

```powershell
./build.ps1             # build into out/ble
./build.ps1 -Test       # + xUnit tests (tests/Ble.Tests) and Pester tests (tests/Module.Tests.ps1)
./build.ps1 -E2E        # + tests in a real terminal
./build.ps1 -Package    # + out/ble-<version>.zip (the module, install.ps1, LICENSE)
./build.ps1 -Install    # install into your module folder (runs install.ps1)
```

The builds are Release builds unless you pass `-Configuration Debug`. The version is written in
one place only, `ModuleVersion` in `module/ble.psd1`. `Ble.dll` takes it from there when it's
built, and `ble version` prints it.

### Making a release

Add a line per change to `CHANGELOG.txt` (only add: lines changed or removed since the last
release stop the release), change `ModuleVersion` in `module/ble.psd1`, then:

```powershell
./build.ps1 -Test -E2E -Package
git commit -am "Release 0.3.1"; git tag v0.3.1; git push; git push --tags
```

Pushing doesn't publish anything. To publish, run the `release` workflow: Actions → release →
Run workflow, or `gh workflow run release` (`-f tag=v0.3.1` for another tag than the newest).
It checks that the tag matches `ModuleVersion`, takes the lines added to `CHANGELOG.txt` since
the last published release as the release notes, builds and tests, and creates the GitHub release
with `ble-<version>.zip`. With a `PSGALLERY_API_KEY` secret it also publishes to the PowerShell
Gallery.

The end-to-end tests start `pwsh` in a ConPTY (pywinpty) and rebuild the screen with pyte. They
need Python and `pip install -r tests/e2e/requirements.txt`.

Two scripts help when debugging:

```powershell
python tests/e2e/probe.py "Get-Ch" "k:TAB" "k:RET"   # drive a session, print the screen after each step
python tests/e2e/probe_profile.py "echo 'abc"        # same with your real profile, prints colors per cell
```

`tests/perf/bench.ps1` times everything that runs on a keystroke.

## How it fits together

ble.ps1 replaces the `PSConsoleHostReadLine` function, which PowerShell calls to read a line.
`module/ble.psm1` installs that hook and wraps your `prompt` function (keeping `$?` intact). The
editor itself is C#:

```
src/Ble/
  Session.cs          entry point from PowerShell, status marks, history setup
  Editor.cs           line state, main loop, key dispatch, undo, drawing model
  Widgets.cs          emacs widgets, history, incremental search
  WidgetsBle.cs       widgets for ble.sh's default bindings
  Completion.cs       menu, TAB, fuzzy matching, suggestions, idle tasks, completion cache
  ListView.cs         list view of suggestions
  Vi.cs, ViExtra.cs   vi modes, operators, registers, macros, marks, ex commands
  Highlighter.cs      highlighting from PowerShell's parser
  PromptSequences.cs  \u \w ... and the status line
  Layout.cs           where the mode line, status line and extra rows go (top, above, below, bottom)
  CapsLock.cs         caps lock state from the OS
  Renderer.cs         drawing with relative cursor moves and explicit line breaks
  Keys.cs, Keymap.cs  key names and the default keymaps
  History.cs          history in PSReadLine's file format
  Options.cs          bleopt and ble-face
  Shell.cs            everything that calls PowerShell (completion, command lookup, prompt)
  BleRc.cs            .blerc reader
  Cmdlets.cs          ble-bind, bleopt, ble-face, ble-sabbrev, ble-import, Import-BleRc, ble-history
```

Some decisions behind the code:

- **Shell interface.** All PowerShell calls go through `IShell`, so the tests run the whole editor
  against a fake shell.
- **Calling PowerShell.** Calls run on the pipeline thread with
  `PowerShell.Create(RunspaceMode.CurrentRunspace)`, never from a background thread.
- **Drawing.** The editor remembers where the prompt ended and draws relative to it, so it
  survives scrolling and looks the same in conhost, Windows Terminal and xterm-like terminals.
- **Command lookup.** PowerShell's lookup takes ~20 ms for a name that doesn't exist, and every
  prefix of a command name being typed is such a name. So command names are looked up in the
  session and in indexes of PATH and module commands.

## Matching ble.sh

When changing behavior that exists in ble.sh, check ble.sh's source first: faces in
`ble/color/defface` calls, bindings in `src/edit.sh` and `lib/keymap.*.sh`, highlighting in
`lib/core-syntax.sh`.
