## Requirements

- PowerShell 7.4 or later. Windows PowerShell 5.1 is not supported.
- To build from source: the .NET 8 SDK or later. Not needed for the release zip.
- Optional: [fzf](https://github.com/junegunn/fzf) for the fzf keys (`winget install junegunn.fzf`).

## Install from a release

Download `ble-<version>.zip` from the
[releases page](https://github.com/nikita-edel/ble.ps1/releases), extract it, and run the
installer in the extracted folder:

```powershell
pwsh -ExecutionPolicy Bypass -File .\install.ps1
```

`-ExecutionPolicy Bypass` is needed because Windows marks downloaded files, and the default
policy won't run a downloaded script that isn't signed. The installer removes that mark from the
module files, so loading the module later works without it.

## Install from source

```powershell
git clone https://github.com/nikita-edel/ble.ps1
cd ble.ps1
./build.ps1 -Install
```

## Load it in your profile

Both ways copy the module to `Documents\PowerShell\Modules\ble\<version>` and remove older
versions. Add this to your
`$PROFILE`, after anything that sets up the prompt (starship, oh-my-posh):

```powershell
Import-Module ble
```

ble.ps1 wraps your `prompt` function, so it has to come after it. If a prompt framework replaces
`prompt` later, ble.ps1 notices and wraps the new one from the next prompt on.

To keep PowerShell usable on machines without the module, guard the import:

```powershell
try { Import-Module ble -ErrorAction Stop }
catch { Write-Warning "ble.ps1 not loaded: $($_.Exception.Message)" }
```

## Turning it on and off

| Command | Effect |
|---|---|
| `ble-detach` | Go back to PSReadLine for this session |
| `ble-attach` | Turn ble.ps1 on again |
| `ble version` | Show the installed version (also in `$BLE_VERSION`) |
| `Import-Module ble -ArgumentList noattach` | Load without attaching (also `$env:BLE_ATTACH = 'none'`) |

## Update

Run `install.ps1` from a newer release zip, or pull and run `./build.ps1 -Install` again.
Running PowerShell windows keep the old version until you close them, because Windows locks the
loaded DLL. The installer leaves a locked old version in place and removes it on a later install.

## Uninstall

Remove `Import-Module ble` from your profile, close all PowerShell windows and delete
`Documents\PowerShell\Modules\ble`.
