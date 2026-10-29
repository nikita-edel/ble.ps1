@{
    RootModule           = 'ble.psm1'
    NestedModules        = @('Ble.dll')
    ModuleVersion        = '0.2.0'
    GUID                 = 'b1e05c1d-4a6e-4f0b-9a8b-2f3c5d7e9a10'
    Author               = 'Nikita Edel'
    Copyright            = '(c) Nikita Edel. MIT License.'
    Description          = 'A ble.sh-like line editor for PowerShell: syntax highlighting, auto-suggestions, menu completion, vi/emacs modes. Source and docs: https://github.com/nikita-edel/ble.ps1 . To use it, add "Import-Module ble" to your $PROFILE. To start from a default config, run "New-BleRc ~/.blerc" once and add "Import-BleRc ~/.blerc" to your $PROFILE after the Import-Module line.'
    PowerShellVersion    = '7.4'
    CompatiblePSEditions = @('Core')
    FunctionsToExport    = @('Connect-Ble', 'Disconnect-Ble', 'Test-BleAttached', 'Invoke-Ble')
    CmdletsToExport      = @('Invoke-BleBind', 'Invoke-BleOpt', 'Set-BleOption', 'Get-BleOption', 'Invoke-BleFace', 'Invoke-BleSabbrev',
                             'Register-BleWidget', 'Invoke-BleWidget', 'Import-BleModule', 'Import-BleRc', 'New-BleRc', 'Invoke-BleHistory', 'Clear-BleHistory')
    AliasesToExport      = @('ble', 'ble-attach', 'ble-detach', 'ble-bind', 'bleopt', 'ble-face', 'ble-sabbrev', 'ble-import', 'ble-rc', 'ble-history')
    VariablesToExport    = @()
    PrivateData          = @{
        PSData = @{
            Tags       = @('ReadLine', 'LineEditor', 'ble.sh', 'SyntaxHighlighting', 'Autosuggestions', 'vi')
            LicenseUri = 'https://opensource.org/licenses/MIT'
            ProjectUri = 'https://github.com/nikita-edel/ble.ps1'
        }
    }
}
