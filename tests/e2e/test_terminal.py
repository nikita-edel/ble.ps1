"""Real-terminal tests: pwsh + ble.ps1 in a ConPTY, screen reconstructed with pyte."""
import time


def fg_at(t, y, x):
    with t.lock:
        return t.screen.buffer[y][x].fg


def test_type_and_execute(term):
    term.type("Write-Output ('ab' * 3)")
    term.settle()
    term.keys("RET")
    term.wait_text("ababab")
    term.wait_prompt()


def test_syntax_highlighting(term):
    term.type("Get-ChildItem -Force nosuchcommand")
    term.settle(0.6)
    y, _ = term.cursor()
    assert term.lines()[y].startswith("PS> Get-ChildItem -Force")
    assert fg_at(term, y, 4) != "default"          # command colored
    assert fg_at(term, y, 18) != fg_at(term, y, 4)  # parameter has another color
    term.keys("C-u")
    term.type("nosuchcommand123")
    term.settle(0.6)
    with term.lock:
        bg = term.screen.buffer[y][4].bg
    assert bg != "default"                          # unknown command marked as error
    term.keys("C-c")
    term.wait_prompt()


def test_history_suggestion_and_up_arrow(term):
    term.type("Write-Output marker-12345")
    term.keys("RET")
    term.wait_text("marker-12345\n")
    term.wait_prompt()
    term.type("Write-Output mar")
    term.settle(0.5)
    assert "PS> Write-Output marker-12345" in term.text().splitlines()[-30:][term.cursor()[0]]
    assert term.cursor()[1] == len("PS> Write-Output mar")
    term.keys("right")
    term.settle()
    assert term.cursor()[1] == len("PS> Write-Output marker-12345")
    term.keys("C-u")
    term.keys("up")
    term.settle()
    assert term.line_at_cursor() == "PS> Write-Output marker-12345"
    term.keys("C-c")
    term.wait_prompt()


def test_completion_menu(make_term):
    # complete our own functions: the installed modules differ between machines
    # (e.g. CI runners have AWS.Tools, so Get-Ch... completes to Get-CHAT...)
    t = make_term(setup="function global:Invoke-BleTestAlpha {}; function global:Invoke-BleTestBeta {}; ")
    t.type("invoke-bletest")
    t.settle(0.2)
    t.keys("TAB")
    t.settle(0.8)
    y, _ = t.cursor()
    # common prefix inserted with fixed case (a gray suggestion may follow the cursor)
    assert t.lines()[y].startswith("PS> Invoke-BleTest") and t.cursor()[1] == len("PS> Invoke-BleTest")
    below = "\n".join(t.lines()[y + 1:y + 4])
    assert "Invoke-BleTestAlpha" in below and "Invoke-BleTestBeta" in below
    t.keys("TAB")                                           # menu-complete selects the first
    t.settle(0.5)
    assert t.line_at_cursor() == "PS> Invoke-BleTestAlpha"
    t.keys("TAB")
    t.settle(0.5)
    assert t.line_at_cursor() == "PS> Invoke-BleTestBeta"
    t.keys("C-g")                                           # cancel restores the prefix
    t.settle(0.3)
    assert t.cursor()[1] == len("PS> Invoke-BleTest")
    t.keys("C-c")
    t.wait_prompt()


def test_ctrl_c_discards(term):
    term.type("echo should-not-run")
    term.keys("C-c")
    term.wait_prompt()
    assert "^C" in term.text()
    assert "should-not-run\nshould-not-run" not in term.text()


def test_multiline_input(term):
    term.type("if ($true) {")
    term.keys("RET")
    term.settle()
    assert term.line_at_cursor() == ">>"
    term.type("  'inside-block'")
    term.keys("RET")
    term.type("}")
    term.settle()
    # ble.sh: RET in a multi-line buffer inserts a newline; C-j runs it
    term.keys("C-j")
    term.wait_text("inside-block\n")
    term.wait_prompt()


def test_exit_status_mark(term):
    term.type("cmd /c exit 3")
    term.keys("RET")
    term.wait_text("[ble: exit 3]")
    term.wait_prompt()
    term.type("Get-Item nonexistent-xyz -ErrorAction SilentlyContinue")
    term.keys("RET")
    term.wait_text("[ble: error]")
    term.wait_prompt()


def test_reverse_isearch(term):
    term.type("Write-Output isearch-target")
    term.keys("RET")
    term.wait_prompt()
    term.type("Write-Output other")
    term.keys("RET")
    term.wait_prompt()
    term.keys("C-r")
    term.type("target")
    term.settle()
    assert "reverse-i-search" in term.text()
    assert "Write-Output isearch-target" in term.text()
    term.keys("RET")
    term.wait_text("isearch-target\nPS>")


def test_long_line_wraps_and_edits(make_term):
    t = make_term(cols=40, rows=20)
    t.type("Write-Output " + "x" * 50)
    t.settle()
    y, x = t.cursor()
    assert t.lines()[y - 1].startswith("PS> Write-Output xxx")
    assert x == (4 + 13 + 50) % 40
    t.keys("C-a")
    t.settle()
    assert t.cursor()[1] == 4
    t.keys("RET")
    t.wait_prompt()
    assert ("x" * 50) in t.text().replace("\n", "")


def test_vi_mode(make_term):
    t = make_term(setup="bleopt default_keymap=vi; ")
    t.type("echo hello world")
    t.settle()
    assert "-- INSERT --" in t.text()
    t.keys("ESC")
    t.settle()
    t.type("bdw")
    t.settle()
    assert t.line_at_cursor() == "PS> echo hello"
    t.type("0cwWrite-Output")
    t.keys("ESC")
    t.settle()
    t.keys("RET")
    t.wait_text("hello\n")
    t.wait_prompt()


def test_vi_paste_in_normal_mode(make_term):
    t = make_term(setup="bleopt default_keymap=vi; ")
    t.type("echo hi")
    t.keys("ESC")
    t.settle()
    t.send(" there; echo pasted")   # all at once, like a paste: inserted, not run as vi commands
    t.settle(0.6)
    assert t.line_at_cursor() == "PS> echo hi there; echo pasted"
    t.keys("RET")
    t.wait_text("pasted\n")
    t.wait_prompt()


def test_wide_characters(term):
    term.type("Write-Output '日本語テキスト'")
    term.settle()
    _, x = term.cursor()
    assert x == len("PS> Write-Output '") + 14 + 1
    term.keys("RET")
    term.wait_text("日本語テキスト")
    term.wait_prompt()


def test_clear_screen(term):
    term.type("Write-Output before-clear")
    term.keys("RET")
    term.wait_prompt()
    term.type("'abc'")   # a string: no (ambiguous) completion suggestion
    term.keys("C-l")
    term.settle(0.8)
    assert "before-clear" not in term.text()
    assert term.lines()[0] == "PS> 'abc'"
    term.keys("C-c")
    term.wait_prompt()


def test_paste_multiline_runs_as_one(term):
    # a burst of input with embedded newlines arrives at once, like a paste
    term.send("Write-Output paste-1\rWrite-Output paste-2")
    term.settle(0.8)
    assert term.line_at_cursor().startswith(">> Write-Output paste-2")
    term.keys("C-j")
    term.wait_text("paste-1\npaste-2")
    term.wait_prompt()


def test_transient_prompt(make_term):
    t = make_term(setup="bleopt prompt_ps1_transient=always prompt_ps1_final='$ '; ")
    # the setup redefines `prompt` after attaching; ble re-wraps it for the next prompt
    t.type("Write-Output first")
    t.keys("RET")
    t.wait_prompt()
    t.type("Write-Output tp-ok")
    t.keys("RET")
    t.wait_text("tp-ok\n")
    t.wait_prompt()
    assert "$ Write-Output tp-ok" in t.text()
    assert "PS> Write-Output tp-ok" not in t.text()


def test_abbrev(make_term):
    t = make_term(setup="ble-sabbrev wo='Write-Output'; ")
    t.type("wo abbrev-ok")
    t.settle()
    assert t.line_at_cursor() == "PS> Write-Output abbrev-ok"
    t.keys("RET")
    t.wait_text("abbrev-ok\n")
    t.wait_prompt()


def test_resize(term):
    term.type("Write-Output resize-test")
    term.settle()
    term.proc.setwinsize(30, 60)
    with term.lock:
        term.screen.resize(30, 60)
    term.settle(0.8)
    term.keys("RET")
    term.wait_text("resize-test\n")
    term.wait_prompt()


def test_detach_restores_psreadline(term):
    term.type("ble-detach")
    term.keys("RET")
    term.settle(1.5)
    term.type("Write-Output detached-ok\r")
    term.wait_text("detached-ok\n")


def test_elapsed_mark(make_term):
    t = make_term(setup="bleopt exec_elapsed_threshold=0; ")
    t.type("Start-Sleep -Milliseconds 50")
    t.keys("RET")
    t.wait_text("[ble: elapsed 0.")
    t.wait_prompt()


def test_right_prompt(make_term):
    t = make_term(setup="bleopt prompt_rps1='[RIGHT]'; ")
    t.keys("RET")
    t.wait_prompt()
    t.settle(0.5)
    line = t.line_at_cursor()
    assert line.startswith("PS>") and line.endswith("[RIGHT]")
    t.type("Write-Output rp")
    t.keys("RET")
    t.wait_text("rp\n")
    # the right prompt is removed from accepted lines
    assert "PS> Write-Output rp" in t.text()
    assert "Write-Output rp" + " " * 10 not in t.text().replace("[RIGHT]", "")


def test_visual_yank_to_clipboard_macro(make_term):
    t = make_term(setup="bleopt default_keymap=vi; ble-bind -m vi_xmap -s 'Y' '\" + y'; Set-Clipboard -Value none; ")
    t.type("Write-Output clip-me-123")
    t.keys("ESC")
    t.settle()
    t.type("Bvf3Y")
    t.settle(0.8)
    t.type("S")          # clear the line (C-c in normal mode only shows the :q hint, like ble.sh)
    t.keys("RET")
    t.wait_prompt()
    t.type("Get-Clipboard")
    t.keys("RET")
    t.wait_text("\nclip-me-123\n")


def test_error_marks(term):
    term.type("Get-ChildItem -NoSuchParam 'open")
    term.settle(0.8)
    y, _ = term.cursor()
    line = term.lines()[y]
    x_param = line.index("-NoSuchParam")
    x_quote = line.index("'open")
    with term.lock:
        assert term.screen.buffer[y][x_param].bg != "default"   # argument_error
        assert term.screen.buffer[y][x_quote].bg != "default"   # unclosed string
    term.keys("C-c")
    term.wait_prompt()


def test_fzf_file_widget(make_term):
    import shutil
    import pytest
    if not shutil.which("fzf"):
        pytest.skip("fzf not installed")
    t = make_term(setup="ble-import integration/fzf-key-bindings; New-Item fzf-target-file.txt | Out-Null; ")
    t.type("Get-Item ")
    t.keys("C-t", delay=1.5)
    t.type("fzf-target")
    t.settle(1.0)
    t.keys("RET", delay=1.0)
    t.wait(lambda: "fzf-target-file.txt" in t.line_at_cursor(), msg="selection inserted")
    assert t.line_at_cursor().startswith("PS> Get-Item fzf-target-file.txt")
    t.keys("RET")
    t.wait_text("fzf-target-file.txt\n")


def test_ambiguous_path_suggestion(make_term):
    # ble.sh: a word that is not a prefix of the candidate shows " [candidate] "
    t = make_term(setup="New-Item -ItemType Directory .local/bin | Out-Null; bleopt complete_auto_delay=50; ")
    t.type("Get-ChildItem .local/bn")
    t.settle(1.5)
    assert t.line_at_cursor() == "PS> Get-ChildItem .local/bn [.local/bin]"
    t.keys("C-e")   # end-of-line accepts the suggestion
    t.settle()
    assert t.line_at_cursor() == "PS> Get-ChildItem .local/bin"
    t.keys("C-c")
    t.wait_prompt()


def test_list_view(make_term):
    t = make_term(setup="bleopt complete_auto_view=list; ")
    for s in ("lv-alpha", "lv-beta"):
        t.type(f"Write-Output {s}")
        t.keys("RET")
        t.wait_text(f"{s}\n")
        t.wait_prompt()
    t.type("Write-Output lv")
    t.settle()
    text = t.text()
    assert "Write-Output lv-beta" in text and "[History]" in text
    t.keys("down")
    t.settle()
    assert "PS> Write-Output lv-beta" in t.text()
    assert "> Write-Output lv-beta" in t.text() and "[History]" in t.text()   # the list stays while navigating
    t.keys("down", "RET")
    t.wait_text("\nlv-alpha\n")
    t.wait_prompt()
