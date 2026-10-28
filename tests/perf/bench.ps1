param([string]$Module = (Join-Path $PSScriptRoot '../../out/ble/ble.psd1'), [switch]$Profile)
# Times the work ble.ps1 does per keystroke, with the real PowerShell engine.
#   pwsh -NoProfile -File tests/perf/bench.ps1            (after ./build.ps1)
#   pwsh -NoProfile -File tests/perf/bench.ps1 -Profile   (with your profile's modules loaded)
if ($Profile) { . $PROFILE }
Import-Module $Module -ArgumentList noattach -Force

Invoke-Expression @'
class NullTerm : Ble.ITerminal {
    [int]$W = 120
    [System.Text.StringBuilder]$Out = [System.Text.StringBuilder]::new()
    [ConsoleKeyInfo] ReadKey() { return [ConsoleKeyInfo]::new([char]0, 0, $false, $false, $false) }
    [bool] get_KeyAvailable() { return $false }
    [void] Write([string]$s) { [void]$this.Out.Append($s) }
    [int] get_Width() { return $this.W }
    [int] get_Height() { return 40 }
    [int] get_CursorLeft() { return 0 }
    [int] get_CursorTop() { return 0 }
    [bool] get_TreatControlCAsInput() { return $false }
    [void] set_TreatControlCAsInput([bool]$v) { }
    [long] get_Now() { return [Environment]::TickCount64 }
    [void] Wait([int]$ms) { }
}
'@

function T([string]$name, [scriptblock]$sb, [int]$n = 20) {
    & $sb | Out-Null   # warm up
    $sw = [Diagnostics.Stopwatch]::StartNew()
    for ($i = 0; $i -lt $n; $i++) { & $sb | Out-Null }
    $sw.Stop()
    '{0,-55} {1,9:N2} ms' -f $name, ($sw.Elapsed.TotalMilliseconds / $n)
}

$shell = [Ble.PsShell]::new($ExecutionContext)
'--- TabExpansion2 (CommandCompletion.CompleteInput, CurrentRunspace) ---'
$cold = [Diagnostics.Stopwatch]::StartNew(); $null = $shell.Complete('Get-Ch', 6); $cold.Stop()
'{0,-55} {1,9:N2} ms' -f 'first call (cold)', $cold.Elapsed.TotalMilliseconds
foreach ($t in 'Get-Ch', 'Get-ChildItem -Erro', 'Get-ChildItem -ErrorAction ', 'ls ./', 'git ', 'xyz', '$Hom', 'Get-ChildItem C:\Windows\System32\dr') {
    T "complete '$t'" { $shell.Complete($t, $t.Length) } 10
}

'--- ble.ps1 internals ---'
$hist = [Ble.History]::new()
$hist.AddRange([string[]](1..10000 | ForEach-Object { "Write-Output item-$_ | Select-Object -First $($_ % 7)" }))
T 'History.FindPrefix (10k entries, hit)' { $hist.FindPrefix('Write-Output item-99') } 200
T 'History.FindPrefix (10k entries, miss)' { $hist.FindPrefix('nothing-like-this') } 200

$hl = [Ble.Highlighter]::new($shell)
$line = 'Get-ChildItem -Path ./src -Recurse | Where-Object { $_.Length -gt 1kb } | ForEach-Object { "$($_.Name): $($_.Length)" }'
T 'Highlight (cold caches, 110 chars)' { $hl.ResetCaches(); $hl.Highlight($line) } 50
T 'Highlight (warm caches, text changed by 1 char)' { $script:k++; $hl.Highlight($line + "$script:k") } 200

$term = [NullTerm]::new()
$ed = [Ble.Editor]::new($term, $shell, $hist)
$ed.StartLine($line)
$ed.Renderer.Begin(4)
T 'Editor.BuildModel' { $ed.BuildModel() } 200
T 'BuildModel + Renderer.Render' { $ed.Renderer.Render($ed.BuildModel()) } 200
[Ble.Options]::Set('complete_auto_view', 'list')
$ed.StartLine('Write-Output item-1')
T 'UpdateSuggestion (list view, 10k history)' { $ed.Text = 'Write-Output item-1' + (Get-Random); $ed.Point = $ed.Text.Length; $ed.UpdateSuggestion() } 100

'--- typing a command name char by char (each prefix is a new name) ---'
$word = 'Get-ChildItem'
$hl2 = [Ble.Highlighter]::new($shell)
$sw = [Diagnostics.Stopwatch]::StartNew()
$per = foreach ($i in 1..$word.Length) {
    $hl2.ResetCaches()
    $s = [Diagnostics.Stopwatch]::StartNew(); $null = $hl2.Highlight($word.Substring(0, $i)); $s.Stop()
    '{0}={1:N1}' -f $word.Substring(0, $i), $s.Elapsed.TotalMilliseconds
}
'{0,-55} {1,9:N2} ms total' -f "highlight each prefix of $word", $sw.Elapsed.TotalMilliseconds
'   ' + ($per -join '  ')
T 'ResolveCommand unknown name (zzqq)' { $script:u++; $shell.ResolveCommand("zzqq$script:u") } 20
T 'ResolveCommand cmdlet (Get-ChildItem)' { $shell.ResolveCommand('Get-ChildItem') } 50
T 'ResolveCommand native (git)' { $shell.ResolveCommand('git') } 20
T 'GetParameterNames (Get-ChildItem)' { $shell.GetParameterNames('Get-ChildItem') } 50
T 'ResolvePath existing dir' { $shell.ResolvePath('src') } 200
T 'GetVariableKind' { $shell.GetVariableKind('HOME') } 200
T 'InvokePrompt' { $shell.InvokePrompt() } 20
