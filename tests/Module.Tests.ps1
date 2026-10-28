#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0' }
# Module-level tests: run with   Invoke-Pester tests/Module.Tests.ps1   after ./build.ps1

BeforeAll {
    $manifest = Join-Path $PSScriptRoot '../out/ble/ble.psd1'
    if (-not (Test-Path $manifest)) { throw "build the module first: ./build.ps1" }
    Import-Module $manifest -ArgumentList noattach -Force
}

AfterAll {
    Remove-Module ble -ErrorAction Ignore
}

Describe 'manifest' {
    It 'exports the ble.sh style commands' {
        foreach ($n in 'ble-bind', 'bleopt', 'ble-face', 'ble-sabbrev', 'ble-attach', 'ble-detach') {
            Get-Command $n -ErrorAction Stop | Should -Not -BeNullOrEmpty
        }
    }
    It 'gives Ble.dll the version of the manifest' {
        $version = (Import-PowerShellDataFile (Join-Path $PSScriptRoot '../module/ble.psd1')).ModuleVersion
        $dll = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $PSScriptRoot '../out/ble/Ble.dll')).Version
        $dll.ToString(3) | Should -Be $version
        $BLE_VERSION | Should -Be $version
        ble version | Should -Be "ble.ps1, version $version"
    }
    It 'dispatches ble subcommands like ble.sh' {
        ble opt prompt_ps2 | Should -Be "bleopt prompt_ps2='>> '"
        ble face command_builtin | Should -BeLike 'ble-face command_builtin=*'
        ble | Should -BeOfType [bool]
        "$(ble nope 2>&1)" | Should -BeLike '*unrecognized subcommand*'
    }
}

Describe 'bleopt' {
    AfterEach { bleopt prompt_ps2='>> ' complete_menu_style=align }

    It 'sets and shows options' {
        bleopt prompt_ps2='> '
        bleopt prompt_ps2 | Should -Be "bleopt prompt_ps2='> '"
        (Get-BleOption prompt_ps2).Value | Should -Be '> '
    }
    It 'supports the two-argument form and scriptblocks' {
        bleopt prompt_rps1 { Get-Date -Format HH:mm }
        (Get-BleOption prompt_rps1).Value | Should -BeOfType [scriptblock]
        bleopt prompt_rps1=
    }
    It 'shows several options' {
        bleopt default_keymap prompt_ps2 | Should -HaveCount 2
    }
    It 'adds to and removes from colon-separated lists with += and -= (ble.sh)' {
        bleopt prompt_ps2='a'
        bleopt prompt_ps2+='b'
        (Get-BleOption prompt_ps2).Value | Should -Be 'a:b'
        bleopt prompt_ps2-='a'
        (Get-BleOption prompt_ps2).Value | Should -Be 'b'
    }
    It 'rejects unknown options' {
        { bleopt no_such_option=1 -ErrorAction Stop } | Should -Throw '*unknown option*'
        { Set-BleOption default_keymap nonsense } | Should -Throw '*emacs*'
    }
    It 'lists options with wildcards' {
        @(bleopt 'complete_*').Count | Should -BeGreaterThan 3
    }
}

Describe 'ble-bind' {
    It 'binds, prints and removes keys' {
        ble-bind -f 'C-x t' transpose-chars
        ble-bind -m emacs | Should -Contain "ble-bind -m emacs -f 'C-x t' transpose-chars"
        ble-bind -f 'C-x t' -
        ble-bind -m emacs | Should -Not -Contain "ble-bind -m emacs -f 'C-x t' transpose-chars"
    }
    It 'accepts PSReadLine style key names' {
        ble-bind -f 'Ctrl+Shift+x' undo
        ble-bind -m emacs | Should -Contain "ble-bind -m emacs -f 'C-S-x' undo"
    }
    It 'binds scriptblocks' {
        ble-bind -m vi_nmap -c 'g s' { git status }
        (ble-bind -m vi_nmap) -join "`n" | Should -BeLike "*-c 'g s' { git status }*"
        ble-bind -x 'C-x d' { $READLINE_LINE += 'x' }
        (ble-bind -m emacs) -join "`n" | Should -BeLike "*-x 'C-x d'*"
    }
    It 'binds keyboard macros' {
        ble-bind -m vi_xmap -s 'Y' '" + y'
        ble-bind -m vi_xmap | Should -Contain "ble-bind -m vi_xmap -s 'Y' '`" + y'"
        ble-bind -m vi_xmap -f 'Y' -
    }
    It 'rejects unknown widgets and keys' {
        { ble-bind -f 'C-x z' no-such-widget } | Should -Throw '*unknown widget*'
        { ble-bind -f 'C-nokey' undo } | Should -Throw '*unknown key*'
        { ble-bind -m nomap -f 'a' undo } | Should -Throw '*unknown keymap*'
    }
    It 'lists widgets' {
        $w = ble-bind -L
        $w | Should -Contain 'accept-line'
        $w | Should -Contain 'vi-normal-mode'
        $w.Count | Should -BeGreaterThan 80
    }
}

Describe 'ble-face' {
    AfterEach { ble-face 'syntax_error=bg=203,fg=231' }
    It 'sets and shows faces' {
        ble-face 'syntax_error=fg=red,bold,underline'
        ble-face syntax_error | Should -Be "ble-face syntax_error='fg=red,bold,underline'"
    }
    It 'validates specifications' {
        { ble-face 'syntax_error=fg=nocolor' -ErrorAction Stop } | Should -Throw '*unknown color*'
        { ble-face 'syntax_error=sparkly' -ErrorAction Stop } | Should -Throw '*unknown attribute*'
    }
    It 'supports wildcards' {
        ble-face 'filename_*=fg=#ff8800'
        ble-face filename_url | Should -Be "ble-face filename_url='fg=#ff8800'"
        ble-face 'filename_*=underline'
    }
}

Describe 'ble-sabbrev' {
    It 'defines, lists and removes abbreviations' {
        ble-sabbrev 'gst=git status' 'L=| less'
        ble-sabbrev | Should -Contain "ble-sabbrev gst='git status'"
        ble-sabbrev -r gst, L
        ble-sabbrev | Should -BeNullOrEmpty
    }
}

Describe 'attach / detach' {
    It 'replaces PSConsoleHostReadLine and the prompt, and restores them' {
        function global:prompt { 'TEST> ' }
        $before = (Get-Item function:PSConsoleHostReadLine -ErrorAction Ignore)?.ScriptBlock
        ble-attach
        Test-BleAttached | Should -BeTrue
        (Get-Item function:PSConsoleHostReadLine).ScriptBlock.ToString() | Should -BeLike '*Ble.Session*ReadLine*'
        (Get-Item function:prompt).ScriptBlock.ToString() | Should -BeLike '*__ble_status*'
        prompt | Should -Be 'TEST> '
        ble-detach
        Test-BleAttached | Should -BeFalse
        (Get-Item function:prompt).ScriptBlock.ToString() | Should -BeLike "*'TEST> '*"
        if ($before) { (Get-Item function:PSConsoleHostReadLine).ScriptBlock.ToString() | Should -Be $before.ToString() }
    }
    It 'preserves $? for the wrapped prompt' {
        function global:prompt { "status=$?" }
        ble-attach
        try {
            # the host calls prompt as a separate statement right after the command
            Get-Item C:\no\such\path -ErrorAction SilentlyContinue; $r = prompt
            $r | Should -Be 'status=False'
            1 | Out-Null; $r = prompt
            $r | Should -Be 'status=True'
        } finally { ble-detach }
    }
}

Describe 'ble.sh configuration' {
    AfterEach { bleopt complete_auto_delay=300 keymap_vi_nmap_cursor=2; ble-face -r region }
    It 'imports a .blerc and reports skipped bash code' {
        $rc = Join-Path $TestDrive 'blerc'
        Set-Content $rc -Value @(
            'bleopt complete_auto_delay=100'
            'ble-face -s region fg=231,bg=60'
            'ble-bind -m vi_nmap --cursor 3'
            'echo hello'
        )
        $w = Import-BleRc $rc 3>&1
        (Get-BleOption complete_auto_delay).Value | Should -Be '100'
        (Get-BleOption keymap_vi_nmap_cursor).Value | Should -Be '3'
        ble-face region | Should -Be "ble-face region='fg=231,bg=60'"
        $w | Should -HaveCount 1
        "$w" | Should -BeLike "*'echo'*"
    }
    It 'ble-import reads non-PowerShell files as .blerc' {
        $rc = Join-Path $TestDrive 'init.sh'
        Set-Content $rc -Value 'bleopt complete_auto_delay=50'
        ble-import $rc
        (Get-BleOption complete_auto_delay).Value | Should -Be '50'
    }
    It 'accepts unquoted comma specs (PowerShell splits them into arrays)' {
        ble-face region=fg=33,bold
        ble-face region | Should -Be "ble-face region='fg=33,bold'"
        ble-face -s region fg=231,bg=60
        ble-face region | Should -Be "ble-face region='fg=231,bg=60'"
        bleopt complete_auto_delay=1,2
        (Get-BleOption complete_auto_delay).Value | Should -Be '1,2'
    }
    It 'New-BleRc writes the shipped default .blerc and does not overwrite without -Force' {
        $default = Join-Path $PSScriptRoot '../out/ble/default.blerc'
        $f = New-BleRc (Join-Path $TestDrive 'a/my.blerc')
        $f.Name | Should -Be 'my.blerc'
        Get-Content $f -Raw | Should -Be (Get-Content $default -Raw)
        (New-BleRc $TestDrive).FullName | Should -Be (Join-Path $TestDrive '.blerc')

        Set-Content $f 'changed'
        "$(New-BleRc $f 2>&1)" | Should -BeLike '*already exists*'
        Get-Content $f | Should -Be 'changed'
        New-BleRc $f -Force | Out-Null
        Get-Content $f -Raw | Should -Be (Get-Content $default -Raw)
    }
    It 'the default .blerc imports without warnings' {
        Import-BleRc (Join-Path $PSScriptRoot '../out/ble/default.blerc') -WarningVariable w -WarningAction SilentlyContinue
        $w | Should -BeNullOrEmpty
    }
    It 'ble-bind -cursor and ble-face -s/-r' {
        ble-bind -m vi_nmap -cursor 1
        (Get-BleOption keymap_vi_nmap_cursor).Value | Should -Be '1'
        ble-face -s region bold
        ble-face region | Should -Be "ble-face region='bold'"
        ble-face -r region
        ble-face region | Should -Not -Be "ble-face region='bold'"
    }
}

Describe 'history' {
    BeforeAll { $script:hf = (Get-BleOption history_file).Value }
    AfterAll { bleopt history_file=$script:hf }
    It 'lists, deletes and clears entries' {
        $f = Join-Path $TestDrive 'hist.txt'
        Set-Content $f -Value 'one', 'two', 'three'
        bleopt history_file=$f
        ble-history | Should -HaveCount 3
        ble-history 1 | Should -BeLike '*3  three'
        ble-history -d 2
        ble-history -w
        Get-Content $f | Should -Be @('one', 'three')
        Clear-BleHistory
        @(ble-history).Count | Should -Be 0
        (Get-Content $f -Raw) | Should -BeNullOrEmpty
    }
}
