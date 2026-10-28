#Requires -Version 7.4
<#
.SYNOPSIS
Builds ble.ps1 into out/ble, optionally runs tests, packages and installs the module.

.EXAMPLE
./build.ps1                 # build Release into out/ble
./build.ps1 -Test           # build and run unit + module tests
./build.ps1 -E2E            # also run the terminal end-to-end tests (needs python, pywinpty, pyte)
./build.ps1 -Package        # also make out/ble-<version>.zip (module + install.ps1)
./build.ps1 -Install        # install out/ble into the user module path (install.ps1)

The version is ModuleVersion in module/ble.psd1; Ble.dll takes it from there.
#>
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$Test,
    [switch]$E2E,
    [switch]$Package,
    [switch]$Install
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$moduleVersion = (Import-PowerShellDataFile module/ble.psd1).ModuleVersion

$dotnet = Get-Command dotnet -ErrorAction Ignore
if (-not $dotnet -and (Test-Path "$HOME/.dotnet/dotnet*")) { $env:PATH = "$HOME/.dotnet$([IO.Path]::PathSeparator)$env:PATH" }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

dotnet build src/Ble/Ble.csproj -c $Configuration -nologo -v q
if ($LASTEXITCODE) { throw 'build failed' }

$out = Join-Path $PSScriptRoot 'out/ble'
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item module/* $out -Force
Copy-Item "src/Ble/bin/$Configuration/net8.0/Ble.dll" $out -Force
Write-Host "built ble $moduleVersion`: $out"

if ($Test) {
    dotnet test tests/Ble.Tests/Ble.Tests.csproj -c $Configuration -nologo -v q
    if ($LASTEXITCODE) { throw 'unit tests failed' }
    pwsh -NoProfile -Command "Invoke-Pester -Path '$PSScriptRoot/tests/Module.Tests.ps1' -CI -Output Detailed"
    if ($LASTEXITCODE) { throw 'module tests failed' }
}
if ($E2E) {
    python -m pytest tests/e2e -q
    if ($LASTEXITCODE) { throw 'e2e tests failed' }
}
if ($Package) {
    # zip layout: install.ps1, LICENSE, ble/ (the module); extract and run install.ps1
    $stage = Join-Path $PSScriptRoot 'out/package'
    Remove-Item $stage -Recurse -Force -ErrorAction Ignore
    New-Item -ItemType Directory -Force $stage | Out-Null
    Copy-Item $out $stage -Recurse
    Copy-Item install.ps1, LICENSE $stage
    $zip = Join-Path $PSScriptRoot "out/ble-$moduleVersion.zip"
    Compress-Archive -Path "$stage/*" -DestinationPath $zip -Force
    Remove-Item $stage -Recurse -Force
    Write-Host "packaged: $zip"
}
if ($Install) {
    & (Join-Path $PSScriptRoot 'install.ps1') -Path $out
}
