#Requires -Version 7.4
<#
.SYNOPSIS
Installs the ble module into your user module folder, as Modules\ble\<version>\
(the same layout Install-Module uses), and removes other installed versions.

.EXAMPLE
./install.ps1                   # from an extracted release zip (installs ./ble)
./install.ps1 -Path out/ble     # what build.ps1 -Install runs
./install.ps1 -Destination D:\Modules   # another modules folder (installs to D:\Modules\ble\<version>)
#>
param(
    [string]$Path = (Join-Path $PSScriptRoot 'ble'),
    [string]$Destination
)
$ErrorActionPreference = 'Stop'

$manifest = Join-Path $Path 'ble.psd1'
if (-not (Test-Path $manifest)) { throw "no module found at $Path (ble.psd1 is missing)" }
$version = (Import-PowerShellDataFile $manifest).ModuleVersion

$modRoot = $Destination
if (-not $modRoot) { $modRoot = ($env:PSModulePath -split [IO.Path]::PathSeparator | Where-Object { $_ -like "$HOME*" } | Select-Object -First 1) }
if (-not $modRoot) { $modRoot = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'PowerShell/Modules' }
$base = Join-Path $modRoot 'ble'
$dest = Join-Path $base $version
New-Item -ItemType Directory -Force $dest | Out-Null

# other versions, and files of the old unversioned layout directly in Modules\ble.
# A version loaded by a running pwsh can't be deleted; it is removed by a later install.
$locked = $false
foreach ($item in Get-ChildItem $base) {
    if ($item.Name -eq $version) { continue }
    try { Remove-Item $item.FullName -Recurse -Force -ErrorAction Stop }
    catch { $locked = $true }
}

# reinstalling the same version: Windows allows renaming a loaded DLL, so move it aside
Get-ChildItem $dest -Filter 'Ble.dll.old-*' -ErrorAction Ignore | Remove-Item -Force -ErrorAction Ignore
$dll = Join-Path $dest 'Ble.dll'
if (Test-Path $dll) {
    try { Remove-Item $dll -Force -ErrorAction Stop }
    catch { Rename-Item $dll "Ble.dll.old-$([DateTime]::Now.Ticks)"; $locked = $true }
}
Copy-Item "$Path/*" $dest -Force
# files from a downloaded zip carry the "from the internet" mark, which RemoteSigned refuses to load
if (Get-Command Unblock-File -ErrorAction Ignore) { Get-ChildItem $dest -File | Unblock-File }

if ($locked) { Write-Host 'ble.ps1 is loaded in running PowerShell sessions; they keep the old version until restarted' }
Write-Host "installed ble $version`: $dest"
Write-Host "add to your profile:  Import-Module ble"
