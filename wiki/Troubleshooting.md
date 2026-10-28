## Something is broken, I need a working prompt now

`ble-detach` switches back to PSReadLine for the current window. To start a window without
ble.ps1, set `$env:BLE_ATTACH = 'none'` before your profile imports it, or start
`pwsh -NoProfile`.

If ble.ps1 hits an internal error, it prints `[ble: internal error: ...]` and falls back to plain
input for that line. The exception is kept in `[Ble.Session]::LastError`; please include it in a
bug report.

## Settings print errors at startup

Run your config file directly to see which line fails:

```powershell
. $PROFILE
```

Common causes: a typo in an option name (`bleopt: unknown option`), an invalid color
(`#ffff`, it needs 3 or 6 hex digits), a face name that doesn't exist (`ble-face name:=spec`
defines a new one).

## Why are cmdlets red?

ble.sh colors bash builtins red, and cmdlets are PowerShell's builtins. Change it with
`ble-face command_builtin=fg=33` (any color).

## A key does nothing or the wrong thing

`ble-bind -k`, then press the key: it prints the key's name as ble.ps1 sees it. Then
`ble-bind -P` shows what that key is bound to. Some terminals send nothing for certain
combinations (for example some `C-S-` keys).

## Performance

ble.ps1's own work per key (highlighting, history, drawing) takes well under a millisecond. When
something feels slow, it is usually PowerShell itself:

- **Completion** (`TabExpansion2`) takes about 1 ms for most input, 10-30 ms for command names, and
  longer in folders with thousands of files. ble.ps1 only asks after you pause
  (`complete_auto_delay`), caches the result for the current line, and stops script-based
  completers when you press a key. File listings can't be stopped once started.
- **Argument completers** registered by modules (git, winget, dotnet, ...) run PowerShell code or
  start programs and can take hundreds of milliseconds. They are stopped when you type.
- **The first prompt** of a new window prepares the command index and the first completion during
  the first pause. Typing a command name within the first half second can take ~40 ms once.

To measure on your machine:

```powershell
pwsh -NoProfile -File tests/perf/bench.ps1            # in the ble.ps1 folder, after ./build.ps1
pwsh -NoProfile -File tests/perf/bench.ps1 -Profile   # with your profile's modules loaded
```

## Does it work in Windows PowerShell 5.1?

No. It needs PowerShell 7.4 or later (`winget install Microsoft.PowerShell`); both can be
installed side by side.

## Does it work on Linux or macOS?

It is built and tested on Windows. The code avoids Windows-only APIs where possible, but other
systems are not tested yet.
