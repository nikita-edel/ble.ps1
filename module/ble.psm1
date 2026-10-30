# ble.ps1 - a ble.sh-like line editor for PowerShell.
#
#   Import-Module ble             # load and attach
#   Import-Module ble -ArgumentList noattach; ble-attach
#
param([string]$AttachMode = 'attach')

# ---- default history file (shared with PSReadLine) --------------------------
$__bleHistory = $null
try {
    # only ask PSReadLine if it is already loaded: importing it just for this path costs ~90 ms
    if (Get-Module PSReadLine) {
        $__bleHistory = (Get-PSReadLineOption).HistorySavePath
    }
} catch { }
if (-not $__bleHistory) {
    $__bleHost = if ($Host -and $Host.Name) { $Host.Name } else { 'ConsoleHost' }
    if ($IsWindows -or $env:OS -eq 'Windows_NT') {
        $__bleHistory = Join-Path $env:APPDATA "Microsoft\Windows\PowerShell\PSReadLine\${__bleHost}_history.txt"
    } else {
        $__bleData = if ($env:XDG_DATA_HOME) { $env:XDG_DATA_HOME } else { Join-Path $HOME '.local/share' }
        $__bleHistory = Join-Path $__bleData "powershell/PSReadLine/${__bleHost}_history.txt"
    }
}
[Ble.Session]::DefaultHistoryFile = $__bleHistory
Remove-Variable __bleHistory, __bleHost, __bleData -ErrorAction Ignore

# ---- hooks -----------------------------------------------------------------
$script:BleAttached = $false
$script:BleOrigReadLine = $null
$script:BleOrigPrompt = $null

# Wraps the user's prompt: prints ble status marks first and records the prompt text.
# $? is captured first and re-established before calling the original prompt.
$script:BlePromptWrapper = {
    $__ble_status = $?
    [Ble.Session]::BeforePrompt($__ble_status, $global:LASTEXITCODE)
    if (-not $__ble_status) { Write-Error -Message '' -ErrorAction Ignore }
    $__ble_out = & $script:BleOrigPrompt
    [Ble.Session]::AfterPrompt($__ble_out)
}

$script:BleReadLine = {
    $__ble_lastRunStatus = $?
    if ($script:BleAttached) {
        $__ble_p = Get-Item function:prompt -ErrorAction Ignore
        if ($__ble_p -and -not $__ble_p.ScriptBlock.ToString().Contains('__ble_status')) {
            # the user (or a prompt framework) replaced the prompt after attaching
            $script:BleOrigPrompt = $__ble_p.ScriptBlock
            Set-Item function:global:prompt -Value $script:BlePromptWrapper
        }
    }
    [Ble.Session]::ReadLine($ExecutionContext, $__ble_lastRunStatus)
}

function Connect-Ble {
    <#
    .SYNOPSIS
    Attaches ble.ps1 to the console host (ble-attach).
    #>
    [CmdletBinding()] param()
    if ($script:BleAttached) { return }
    $rl = Get-Item function:PSConsoleHostReadLine -ErrorAction Ignore
    $script:BleOrigReadLine = if ($rl) { $rl.ScriptBlock } else { $null }
    $p = Get-Item function:prompt -ErrorAction Ignore
    $script:BleOrigPrompt = if ($p) { $p.ScriptBlock } else { { "PS $($executionContext.SessionState.Path.CurrentLocation)$('>' * ($nestedPromptLevel + 1)) " } }
    Set-Item function:global:prompt -Value $script:BlePromptWrapper
    Set-Item function:global:PSConsoleHostReadLine -Value $script:BleReadLine
    $script:BleAttached = $true
}

function Disconnect-Ble {
    <#
    .SYNOPSIS
    Detaches ble.ps1 and restores the previous line editor (ble-detach).
    #>
    [CmdletBinding()] param()
    if (-not $script:BleAttached) { return }
    if ($script:BleOrigReadLine) { Set-Item function:global:PSConsoleHostReadLine -Value $script:BleOrigReadLine }
    else { Remove-Item function:global:PSConsoleHostReadLine -ErrorAction Ignore }
    if ($script:BleOrigPrompt) { Set-Item function:global:prompt -Value $script:BleOrigPrompt }
    $script:BleAttached = $false
}

function Test-BleAttached { $script:BleAttached }

# like ble.sh's $BLE_VERSION. Ble.dll is built with ModuleVersion from ble.psd1 (the module's
# own Version is still 0.0 while this file loads)
$global:BLE_VERSION = [Ble.Session].Assembly.GetName().Version.ToString(3)

function Invoke-Ble {
    <#
    .SYNOPSIS
    ble.sh's `ble` command: ble version, ble attach, ble bind ..., ble opt ..., ble face ...
    Without arguments it returns whether ble.ps1 is attached.
    #>
    if (-not $args) { return $script:BleAttached }
    $cmd = $args[0]
    $rest = @($args | Select-Object -Skip 1)
    switch -Exact ($cmd) {
        { $_ -in 'version', '--version' } { "ble.ps1, version $global:BLE_VERSION"; return }
        { $_ -in 'help', '--help' } {
            'usage: ble [SUBCOMMAND [ARGS...]]'
            'subcommands: attach detach bind opt face sabbrev import rc history version help'
            return
        }
        'opt' { bleopt @rest; return }
    }
    # like ble.sh: any other word runs ble-<word> if it exists
    if ($cmd -match '^[-a-zA-Z0-9]+$' -and (Get-Command "ble-$cmd" -ErrorAction Ignore)) { & "ble-$cmd" @rest; return }
    Write-Error "ble (ble.ps1): unrecognized subcommand '$cmd'."
}

Set-Alias -Name ble-attach -Value Connect-Ble
Set-Alias -Name ble-detach -Value Disconnect-Ble
Set-Alias -Name ble -Value Invoke-Ble

$ExecutionContext.SessionState.Module.OnRemove = { Disconnect-Ble }

Export-ModuleMember -Function Connect-Ble, Disconnect-Ble, Test-BleAttached, Invoke-Ble -Alias * -Cmdlet *

if ($AttachMode -ne 'noattach' -and $env:BLE_ATTACH -ne 'none') {
    if ([Environment]::UserInteractive -and -not [Console]::IsInputRedirected) { Connect-Ble }
}
