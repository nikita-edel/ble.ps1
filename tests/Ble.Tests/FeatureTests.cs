using System.Management.Automation;
using Ble;
using Xunit;

namespace Ble.Tests;

public class ViBlockTests
{
    static Harness Normal(string text, int point)
    {
        var h = new Harness().Vi();
        h.E.SetText(text, point);
        h.Keys("ESC");
        h.E.Point = point;
        return h;
    }

    [Fact]
    public void BlockDelete()
    {
        var h = Normal("abc\ndef\nghi", 0);
        h.Keys("C-v");
        Assert.Equal("vi_xmap", h.E.Keymap);
        Assert.Contains(h.E.BuildModel().Bottom, p => p.PlainText == "-- VISUAL BLOCK --");
        h.Type("jld");
        Assert.Equal("|c\nf\nghi", h.State);
        Assert.Equal("vi_nmap", h.E.Keymap);
        h.Type("u");
        Assert.Equal("abc\ndef\nghi", h.Text);
    }

    [Fact]
    public void BlockInsertAndAppend()
    {
        var h = Normal("ab\ncd\nef", 0);
        h.Keys("C-v");
        h.Type("jjIX");
        h.Keys("ESC");
        Assert.Equal("Xab\nXcd\nXef", h.Text);
        h = Normal("a\nbcd", 0);
        h.Keys("C-v");
        h.Type("j$A!");
        h.Keys("ESC");
        Assert.Equal("a!\nbcd!", h.Text);
        // A pads short lines
        h = Normal("abc\nx", 1);
        h.Keys("C-v");
        h.Type("jA|");
        h.Keys("ESC");
        Assert.Equal("ab|c\nx |", h.Text);
    }

    [Fact]
    public void BlockChangeYankPaste()
    {
        var h = Normal("abc\ndef", 1);
        h.Keys("C-v");
        h.Type("jcZ");
        h.Keys("ESC");
        Assert.Equal("aZc\ndZf", h.Text);
        h = Normal("ab\ncd", 0);
        h.Keys("C-v");
        h.Type("jy");
        Assert.True(Editor.Registers['"'].Blockwise);
        h.Type("$p");
        Assert.Equal("aba\ncdc", h.Text);
    }

    [Fact]
    public void BlockReplaceAndCase()
    {
        var h = Normal("abc\ndef", 0);
        h.Keys("C-v");
        h.Type("jlrx");
        Assert.Equal("xxc\nxxf", h.Text);
        h = Normal("abc\ndef", 1);
        h.Keys("C-v");
        h.Type("jU");
        Assert.Equal("aBc\ndEf", h.Text);
    }
}

public class ViMacroMarkExTests
{
    static Harness Normal(string text, int point, params string[] history)
    {
        var h = new Harness(history).Vi();
        h.E.SetText(text, point);
        h.Keys("ESC");
        h.E.Point = point;
        return h;
    }

    [Fact]
    public void RecordAndPlayMacros()
    {
        var h = Normal("abcdef", 0);
        h.Type("qaxq");
        Assert.Equal("bcdef", h.Text);
        Assert.DoesNotContain(h.E.BuildModel().Bottom, p => p.PlainText.Contains("recording"));
        h.Type("2@a");
        Assert.Equal("def", h.Text);
        h.Type("@@");
        Assert.Equal("ef", h.Text);
    }

    [Fact]
    public void RecordingShowsInModeLine()
    {
        var h = Normal("abc", 0);
        h.Type("qb");
        Assert.Contains(h.E.BuildModel().Bottom, p => p.PlainText.Contains("recording @b"));
        h.Type("q");
        Assert.DoesNotContain(h.E.BuildModel().Bottom, p => p.PlainText.Contains("recording"));
    }

    [Fact]
    public void MacroWithInsert()
    {
        var h = Normal("x", 0);
        h.Type("qcA!");
        h.Keys("ESC");
        h.Type("q");
        Assert.Equal("x!", h.Text);
        h.Type("@c");
        Assert.Equal("x!!", h.Text);
    }

    [Fact]
    public void Marks()
    {
        var h = Normal("one two three", 4);
        h.Type("ma$");
        Assert.Equal(12, h.Point);
        h.Type("`a");
        Assert.Equal(4, h.Point);
        h.Type("$d`a");
        Assert.Equal("one e", h.Text);
        h = Normal("l1\nl2\nl3", 4);
        h.Type("mbgg'b");
        Assert.Equal("l1\n|l2\nl3", h.State);
        h.Type("G''");
        Assert.Equal("l1\n|l2\nl3", h.State);
    }

    [Fact]
    public void ExSubstitute()
    {
        var h = Normal("foo boo", 0);
        h.Type(":s/o/0/g");
        h.Keys("RET");
        Assert.Equal("f00 b00", h.Text);
        h.Type("u");
        Assert.Equal("foo boo", h.Text);
        h = Normal("a1\na2\na3", 0);
        h.Type(":%s/a\\(\\d\\)/[\\1]/");
        h.Keys("RET");
        Assert.Equal("[1]\n[2]\n[3]", h.Text);
        h = Normal("x y", 0);
        h.Type(":s/y/&&/");
        h.Keys("RET");
        Assert.Equal("x yy", h.Text);
    }

    [Fact]
    public void ExRangesAndCommands()
    {
        var h = Normal("l1\nl2\nl3\nl4", 0);
        h.Type(":2,3d");
        h.Keys("RET");
        Assert.Equal("l1\nl4", h.Text);
        h.Type(":%normal A!");
        h.Keys("RET");
        Assert.Equal("l1!\nl4!", h.Text);
        h.Type(":2");
        h.Keys("RET");
        Assert.Equal(1, h.E.LineNumber(h.Point));
        h.Type(":nosuchcmd");
        h.Keys("RET");
        Assert.Contains("E492", h.E.Info);
        h.Type(":set complete_menu_style?");
        h.Keys("RET");
        Assert.Equal("complete_menu_style=align", h.E.Info);
        h.Type(":set complete_menu_style=desc");
        h.Keys("RET");
        Assert.Equal("desc", Options.Get("complete_menu_style"));
    }

    [Fact]
    public void ExFromVisualUsesSelectedLines()
    {
        var h = Normal("a\nb\nc", 0);
        h.Type("Vj:");
        Assert.Equal(":'<,'>", h.E.Info);
        h.Type("s/$/;/");
        h.Keys("RET");
        Assert.Equal("a;\nb;\nc", h.Text);
    }

    [Fact]
    public void ExWriteAndQuit()
    {
        var h = Normal("echo hi", 0);
        h.Type(":w");
        h.Keys("RET");
        Assert.True(h.E.Done);
        Assert.Equal("echo hi", h.E.Result);
        h = Normal("echo hi", 0);
        h.Type(":q");
        h.Keys("RET");
        Assert.True(h.E.Done);
        Assert.Equal("exit", h.E.Result);   // ble.sh: :q exits the shell
    }

    [Fact]
    public void RepeatLastExCommand()
    {
        var h = Normal("aaa", 0);
        h.Type(":s/a/b/");
        h.Keys("RET");
        h.Type("@:");
        Assert.Equal("bba", h.Text);
    }

    [Fact]
    public void VimRegexConversion()
    {
        Assert.Equal(@"(a|b)+", Editor.VimRegexToNet(@"\(a\|b\)\+"));
        Assert.Equal(@"\(x\)", Editor.VimRegexToNet(@"(x)"));
        Assert.Equal(@"\bword\b", Editor.VimRegexToNet(@"\<word\>"));
        Assert.Equal("${1}-$0", Editor.VimReplacementToNet(@"\1-&"));
    }
}

public class SelectModeTests
{
    static Harness Normal(string text, int point)
    {
        var h = new Harness().Vi();
        h.E.SetText(text, point);
        h.Keys("ESC");
        h.E.Point = point;
        return h;
    }

    [Fact]
    public void TypingReplacesSelection()
    {
        var h = Normal("hello world", 0);
        h.Type("gh");
        Assert.Equal("vi_smap", h.E.Keymap);
        Assert.Contains(h.E.BuildModel().Bottom, p => p.PlainText == "-- SELECT --");
        h.Keys("right", "right");
        h.Type("X");
        Assert.Equal("X|lo world", h.State);
        Assert.Equal("vi_imap", h.E.Keymap);
    }

    [Fact]
    public void ShiftArrowFromNormalStartsSelect()
    {
        var h = Normal("hello world", 0);
        h.Keys("S-right");
        Assert.Equal("vi_smap", h.E.Keymap);
        h.Type("Z");
        Assert.Equal("Z|llo world", h.State);
    }

    [Fact]
    public void BackspaceDeletesAndCtrlGSwitchesToVisual()
    {
        var h = Normal("abcdef", 1);
        h.Type("gh");
        h.Keys("right", "DEL");
        Assert.Equal("a|def", h.State);
        h = Normal("abcdef", 0);
        h.Type("gh");
        h.Keys("C-g");
        Assert.Equal("vi_xmap", h.E.Keymap);
        h.Keys("C-g");
        Assert.Equal("vi_smap", h.E.Keymap);
        h.Keys("ESC");
        Assert.Equal("vi_nmap", h.E.Keymap);
    }
}

public class AmbiguousAndFzfTests
{
    static void WildcardCompleter(FakeShell sh, params string[] candidates)
    {
        sh.Completer = (text, cursor) =>
        {
            int s = cursor;
            while (s > 0 && !char.IsWhiteSpace(text[s - 1])) s--;
            var w = text.Substring(s, cursor - s);
            var pat = new WildcardPattern(w.Contains('*') ? w : w + "*", WildcardOptions.IgnoreCase);
            var items = candidates.Where(c => pat.IsMatch(c))
                .Select(c => new CompletionItem(c, c, c, CompletionResultType.Command)).ToList();
            return new CompletionSet(s, cursor - s, items);
        };
    }

    [Fact]
    public void SubstringAndSubsequenceFallback()
    {
        var h = new Harness();
        WildcardCompleter(h.S, "Get-ChildItem", "Set-Location", "Get-Content");
        h.Type("childit");
        h.Keys("TAB");
        Assert.Equal("Get-ChildItem |", h.State);
        h = new Harness();
        WildcardCompleter(h.S, "Get-ChildItem", "Set-Location");
        h.Type("gci");
        h.Keys("TAB");
        Assert.Equal("Get-ChildItem |", h.State);
        h = new Harness();
        WildcardCompleter(h.S, "Get-ChildItem", "Get-Content");
        h.Type("tent");
        h.Keys("TAB");
        Assert.Equal("Get-Content |", h.State);
    }

    [Fact]
    public void AmbiguousCanBeDisabled()
    {
        var h = new Harness();
        Options.Set("complete_ambiguous", "");
        WildcardCompleter(h.S, "Get-ChildItem");
        h.Type("childit");
        h.Keys("TAB");
        Assert.Equal("childit|", h.State);
    }

    [Fact]
    public void FzfWidgets()
    {
        var h = new Harness("old cmd", "newest cmd");
        string? lastArgs = null;
        h.E.FzfRunner = (args, input) =>
        {
            lastArgs = args;
            return input != null ? new List<string> { input[0] } : new List<string> { "my file.txt", "b.txt" };
        };
        h.Type("cat");
        h.E.InvokeWidget("fzf-file-widget", default);
        Assert.Equal("cat 'my file.txt' b.txt |", h.State);
        Assert.Contains("--multi", lastArgs);
        h.Keys("C-u");
        h.E.InvokeWidget("fzf-history-widget", default);
        Assert.Equal("newest cmd|", h.State);
        Assert.Contains("--scheme=history", lastArgs);
    }

    [Fact]
    public void FzfCompletionTrigger()
    {
        var h = new Harness();
        Options.Set("complete_fzf_trigger", "**");
        string? lastArgs = null;
        h.E.FzfRunner = (args, input) => { lastArgs = args; return ["src/a.cs"]; };
        h.Type("cat src**");
        h.Keys("TAB");
        Assert.Equal("cat src/a.cs |", h.State);
        Assert.Contains("--query=\"src\"", lastArgs);
    }

    [Fact]
    public void FzfCancelledLeavesLine()
    {
        var h = new Harness();
        h.E.FzfRunner = (_, _) => null;
        h.Type("echo");
        h.E.InvokeWidget("fzf-file-widget", default);
        Assert.Equal("echo|", h.State);
    }
}

public class PromptAndWidgetTests
{
    [Fact]
    public void PromptSequences()
    {
        var h = new Harness();
        Assert.Equal(Environment.UserName + "@" + Environment.MachineName.Split('.')[0], h.E.ExpandPrompt(@"\u@\h"));
        Assert.Equal(DateTime.Now.Year.ToString(), h.E.ExpandPrompt(@"\D{%Y}"));
        Assert.Equal("\x1b[1mX\x1b[m", h.E.ExpandPrompt(@"\e[1mX\e[m"));
        Assert.Equal(@"a\b", h.E.ExpandPrompt(@"a\\b"));
        Assert.Equal("plain", h.E.ExpandPrompt("plain"));
        h.Vi();
        Assert.Equal("-- INSERT --", h.E.ExpandPrompt(@"\q{keymap:vi/mode-indicator}"));
        Assert.Equal("insert", h.E.ExpandPrompt(@"\q{keymap:vi/mode-name}"));
    }

    [Fact]
    public void StatusLineAndModeIndicator()
    {
        var h = new Harness();
        Options.Set("prompt_status_line", @"[\q{keymap}]");
        Options.Set("prompt_status_align", "right");
        h.T.Width = 30;
        h.E.Renderer.Begin(4);
        var last = h.E.BuildModel().Bottom[^1].PlainText;
        Assert.Equal("[emacs]".PadLeft(29), last);
        // ble.sh style: hide the mode line and show the mode in the status line instead
        h = new Harness().Vi();
        Options.Set("keymap_vi_mode_show", "");
        Options.Set("prompt_status_line", @"\q{keymap:vi/mode-indicator}");
        var panels = h.E.BuildModel().Bottom;
        Assert.Single(panels);
        Assert.StartsWith("-- INSERT --", panels[0].PlainText);
    }

    [Fact]
    public void CapsLockIndicator()
    {
        var h = new Harness().Vi();
        bool? caps = false;
        h.E.CapsLockProbe = () => caps;
        Options.Set("prompt_vi_mode_indicator", @"\q{keymap:vi/mode-indicator} \q{capslock}");
        Options.Set("prompt_capslock_indicator", @"CAPS \q{capslock}");   // must not recurse
        h.E.UpdateCapsLock();
        Assert.Equal("-- INSERT --", h.E.BuildModel().Bottom[0].PlainText.TrimEnd());
        caps = true;
        Assert.True(h.E.UpdateCapsLock());
        Assert.False(h.E.UpdateCapsLock());
        Assert.Equal("-- INSERT -- CAPS", h.E.BuildModel().Bottom[0].PlainText.TrimEnd());
        // capslock_reserve: same width while off
        Options.Set("capslock_reserve", "1");
        Options.Set("prompt_capslock_indicator", "\x1b[31m^^\x1b[m");
        Assert.Equal("\x1b[31m^^\x1b[m", h.E.ExpandPrompt(@"\q{capslock}"));
        caps = false;
        h.E.UpdateCapsLock();
        Assert.Equal("  ", h.E.ExpandPrompt(@"\q{capslock}"));
        Options.Set("capslock_refresh", "");
        h.E.UpdateCapsLock();
        Assert.Equal("", h.E.ExpandPrompt(@"\q{capslock}"));
    }

    [Fact]
    public void UserWidgetsUseEditProtocol()
    {
        var h = new Harness();
        Editor.UserWidgets["shout"] = ScriptBlock.Create("1");
        h.S.EditHandler = (line, point, mark) => (line.ToUpperInvariant(), point, mark);
        Keymaps.Get("emacs").Bind(KeyNames.ParseSpec("C-x s"), new Binding(BindingKind.UserWidget, "shout"));
        h.Type("hello");
        h.Keys("C-x", "s");
        Assert.Equal("HELLO|", h.State);
        h.Keys("C-_");
        Assert.Equal("hello|", h.State);
        Editor.UserWidgets.Clear();
    }

    [Fact]
    public void EditCommandCanSetMark()
    {
        var h = new Harness();
        h.S.EditHandler = (line, point, mark) => (line, 0, line.Length);
        Keymaps.Get("emacs").Bind(KeyNames.ParseSpec("C-x m"), new Binding(BindingKind.Edit, "", ScriptBlock.Create("1")));
        h.Type("select me");
        h.Keys("C-x", "m");
        Assert.True(h.E.MarkActive);
        Assert.Equal(9, h.E.Mark);
        Assert.Equal(0, h.Point);
    }
}

public class BleShParityTests
{
    static Harness Normal(string text, int point)
    {
        var h = new Harness().Vi();
        h.E.SetText(text, point);
        h.Keys("ESC");
        h.E.Point = point;
        return h;
    }

    [Fact]
    public void ViNormalCtrlCDoesNotDiscard()
    {
        var h = Normal("echo hi", 2);
        h.Keys("C-c");
        Assert.False(h.E.Done);
        Assert.Contains(":q", h.E.Info);
        Assert.Equal("echo hi", h.Text);
    }

    [Fact]
    public void ViInsertCtrlCGoesToNormalMode()
    {
        var h = new Harness().Vi();
        h.Type("abc");
        h.Keys("C-c");
        Assert.Equal("vi_nmap", h.E.Keymap);
        Assert.False(h.E.Done);
    }

    [Fact]
    public void ViNormalReturnMovesDownInMultiline()
    {
        var h = Normal("a\n  b", 0);
        h.Keys("RET");
        Assert.False(h.E.Done);
        Assert.Equal("a\n  |b", h.State);
        h = Normal("echo ok", 0);
        h.Keys("RET");
        Assert.True(h.E.Done);
    }

    [Fact]
    public void RegionIsDeletedByDelAndCtrlD()
    {
        var h = new Harness().Type("hello world");
        h.Keys("C-@", "M-b", "DEL");
        Assert.Equal("hello |", h.State);
        h = new Harness().Type("abc");
        h.Keys("C-@", "C-a", "C-d");
        Assert.Equal("|", h.State);
        Assert.False(h.E.Done);
    }

    [Fact]
    public void EmacsArgumentDigitsAndUniversalArg()
    {
        var h = new Harness().Type("x");
        h.Keys("M-1", "2", "y");
        Assert.Equal("x" + new string('y', 12) + "|", h.State);
        h = new Harness();
        h.Keys("C-M-u", "z");
        Assert.Equal("zzzz|", h.State);
        h = new Harness().Type("a1-2");
        Assert.Equal("a1-2|", h.State);
    }

    [Fact]
    public void KeyboardMacros()
    {
        var h = new Harness();
        h.Keys("C-x", "(");
        h.Type("ab");
        h.Keys("C-x", ")");
        Assert.Equal("ab|", h.State);
        h.Keys("C-x", "e");
        Assert.Equal("abab|", h.State);
    }

    [Fact]
    public void ExecuteNamedCommand()
    {
        var h = new Harness().Type("hello");
        h.Keys("M-x");
        h.Type("beginning-of-line");
        h.Keys("RET");
        Assert.Equal("|hello", h.State);
    }

    [Fact]
    public void ShellWordsAndSelection()
    {
        var h = new Harness().Type("echo 'a b' c");
        h.Keys("C-a", "M-right");
        Assert.Equal("echo| 'a b' c", h.State);
        h.Keys("M-right");
        Assert.Equal("echo 'a b'| c", h.State);
        h.Keys("M-F");
        Assert.True(h.E.MarkActive);
    }

    [Fact]
    public void SubstringHistorySearchAndNthArgument()
    {
        var h = new Harness("git commit -m x", "ls -la", "echo last one");
        h.Type("comm");
        h.Keys("C-x", "p");
        Assert.Equal("git commit -m x|", h.State);
        h = new Harness("cmd first second");
        h.Keys("C-M-y");
        Assert.Equal("first|", h.State);
    }

    [Fact]
    public void AcceptAndNext()
    {
        var h = new Harness("one", "two", "three");
        h.Keys("up", "up", "up");
        Assert.Equal("one|", h.State);
        h.Keys("C-o");
        Assert.True(h.E.Done);
        Assert.Equal("two", h.E.TakeNextLineInitial());
    }

    [Fact]
    public void DabbrevAndTildeExpand()
    {
        var h = new Harness("cd somedirectory", "echo something");
        h.Type("ls som");
        h.Keys("C-x", "C-r");
        Assert.Equal("ls something|", h.State);
        h.Keys("C-x", "C-r");
        Assert.Equal("ls somedirectory|", h.State);
        h = new Harness().Type("cd ~/x");
        h.Keys("M-&");
        Assert.StartsWith("cd " + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), h.Text);
    }

    [Fact]
    public void ZapToCharAndYankPopBackward()
    {
        var h = new Harness().Type("abc:def");
        h.Keys("C-a", "M-z", ":");
        Assert.Equal("|def", h.State);
    }
}
