using Ble;
using Xunit;

namespace Ble.Tests;

public class ViTests
{
    static Harness Normal(string text, int point, params string[] history)
    {
        var h = new Harness(history).Vi();
        h.Type(text);
        h.Keys("ESC");
        h.E.Point = point;
        return h;
    }

    [Fact]
    public void EscEntersNormalModeAndMovesLeft()
    {
        var h = new Harness().Vi();
        Assert.Equal("vi_imap", h.E.Keymap);
        h.Type("abc");
        h.Keys("ESC");
        Assert.Equal("vi_nmap", h.E.Keymap);
        Assert.Equal("ab|c", h.State);
    }

    [Fact]
    public void BasicMotions()
    {
        var h = Normal("echo foo.bar baz", 0);
        h.Type("w");
        Assert.Equal("echo |foo.bar baz", h.State);
        h.Type("w");
        Assert.Equal("echo foo|.bar baz", h.State);
        h.Type("W");
        Assert.Equal("echo foo.bar |baz", h.State);
        h.Type("b");
        Assert.Equal("echo foo.|bar baz", h.State);
        h.Type("B");
        Assert.Equal("echo |foo.bar baz", h.State);
        h.Type("e");
        Assert.Equal("echo fo|o.bar baz", h.State);
        h.Type("$");
        Assert.Equal("echo foo.bar ba|z", h.State);
        h.Type("0");
        Assert.Equal("|echo foo.bar baz", h.State);
        h.Type("3l");
        Assert.Equal("ech|o foo.bar baz", h.State);
        h.Type("fb");
        Assert.Equal("echo foo.|bar baz", h.State);
        h.Type(";");
        Assert.Equal("echo foo.bar |baz", h.State);
        h.Type(",");
        Assert.Equal("echo foo.|bar baz", h.State);
        h.Type("tz");
        Assert.Equal("echo foo.bar b|az", h.State);
        h.Type("Fo");
        Assert.Equal("echo fo|o.bar baz", h.State);
        h.Type("^");
        Assert.Equal("|echo foo.bar baz", h.State);
    }

    [Fact]
    public void DeleteChangeYankOperators()
    {
        var h = Normal("one two three", 0);
        h.Type("dw");
        Assert.Equal("|two three", h.State);
        h.Type("cwTWO");
        h.Keys("ESC");
        Assert.Equal("TW|O three", h.State);
        h.Type("w");
        h.Type("yw");
        h.Type("0P");
        Assert.Equal("thre|eTWO three", h.State);
        h.Type("u");
        Assert.Equal("TWO three", h.Text);
        h.Type("$");
        h.Type("D");
        Assert.Equal("TWO thr|e", h.State);
    }

    [Fact]
    public void DeleteToEndAndChangeLine()
    {
        var h = Normal("alpha beta gamma", 6);
        h.Type("D");
        Assert.Equal("alpha| ", h.State);
        h = Normal("alpha beta gamma", 6);
        h.Type("C");
        h.Type("X");
        h.Keys("ESC");
        Assert.Equal("alpha |X", h.State);
        h = Normal("  indented text", 5);
        h.Type("cc");
        Assert.Equal("  |", h.State);
        Assert.Equal("vi_imap", h.E.Keymap);
    }

    [Fact]
    public void CountsAndRepeat()
    {
        var h = Normal("abcdefgh", 0);
        h.Type("3x");
        Assert.Equal("|defgh", h.State);
        h.Type(".");
        Assert.Equal("|gh", h.State);
        h = Normal("a b c d e f", 0);
        h.Type("d2w");
        Assert.Equal("|c d e f", h.State);
        h.Type(".");
        Assert.Equal("|e f", h.State);
    }

    [Fact]
    public void DotRepeatsInsertSessions()
    {
        var h = Normal("x", 0);
        h.Type("aXY");
        h.Keys("ESC");
        Assert.Equal("xX|Y", h.State);
        h.Type(".");
        Assert.Equal("xXYX|Y", h.State);
        h.Type("u");
        Assert.Equal("xXY", h.Text);
    }

    [Fact]
    public void CountedInsert()
    {
        var h = Normal("", 0);
        h.Type("3ia");
        h.Keys("ESC");
        Assert.Equal("aa|a", h.State);
        h.Type("u");
        Assert.Equal("", h.Text);
    }

    [Fact]
    public void TextObjects()
    {
        var h = Normal("echo \"hello world\" (a (b c) d)", 8);
        h.Type("ci\"");
        h.Type("X");
        h.Keys("ESC");
        Assert.Equal("echo \"|X\" (a (b c) d)", h.State);
        h.E.Point = h.Text.IndexOf('b');
        h.Type("di(");
        Assert.Equal("echo \"X\" (a (|) d)", h.State);
        h.E.Point = h.Text.IndexOf('a');
        h.Type("da(");
        Assert.Equal("echo \"X\"| ", h.State);
        h = Normal("foo bar baz", 5);
        h.Type("daw");
        Assert.Equal("foo |baz", h.State);
        h = Normal("foo bar baz", 5);
        h.Type("ciwQ");
        h.Keys("ESC");
        Assert.Equal("foo |Q baz", h.State);
    }

    [Fact]
    public void PasteAndRegisters()
    {
        var h = Normal("abc def", 0);
        h.Type("\"ayw");
        h.Type("$\"ap");
        Assert.Equal("abc defabc| ", h.State);
        h = Normal("hello", 0);
        h.Type("x");
        h.Type("p");
        Assert.Equal("e|hllo", h.State);
    }

    [Fact]
    public void LinewiseYankPasteInMultilineBuffer()
    {
        var h = new Harness().Vi();
        h.Type("line1");
        h.Keys("S-RET");
        h.Type("line2");
        h.Keys("ESC");
        h.Type("k");
        Assert.Equal("line|1\nline2", h.State);
        h.Type("yyjp");
        Assert.Equal("line1\nline2\n|line1", h.State);
        h.Type("dd");
        Assert.Equal("line1\n|line2", h.State);
        h.Type("ggdG");
        Assert.Equal("|", h.State);
    }

    [Fact]
    public void ReplaceCommands()
    {
        var h = Normal("abcd", 1);
        h.Type("rX");
        Assert.Equal("a|Xcd", h.State);
        h.Type("2rY");
        Assert.Equal("aY|Yd", h.State);
        h.Type("0RZZ");
        Assert.Equal("ZZ|Yd", h.State);
        h.Keys("DEL");
        Assert.Equal("Z|YYd", h.State);
        h.Keys("ESC");
        h.Type("~");
        Assert.Equal("z|YYd", h.State);
    }

    [Fact]
    public void VisualMode()
    {
        var h = Normal("hello world", 0);
        h.Type("vlld");
        Assert.Equal("|lo world", h.State);
        Assert.Equal("vi_nmap", h.E.Keymap);
        h.Type("veU");
        Assert.Equal("|LO world", h.State);
        h.Type("wviwy");
        Assert.Equal("LO |world", h.State);
        h.Type("0vep");
        Assert.Equal("worl|d world", h.State);
    }

    [Fact]
    public void HistoryWithJKAndSearch()
    {
        var h = Normal("", 0, "first cmd", "second cmd", "third");
        h.Type("k");
        Assert.Equal("|third", h.State);
        h.Type("k");
        Assert.Equal("|second cmd", h.State);
        h.Type("j");
        Assert.Equal("|third", h.State);
        h.Type("/first");
        h.Keys("RET");
        Assert.Equal("|first cmd", h.State);
        h.Type("G");
        Assert.Equal("|", h.State);
        h.Type("/cmd");
        h.Keys("RET");
        Assert.Equal("second |cmd", h.State);
        h.Type("n");
        Assert.Equal("first |cmd", h.State);
        h.Type("N");
        Assert.Equal("second |cmd", h.State);
    }

    [Fact]
    public void IndentAndJoinAndIncrement()
    {
        var h = new Harness().Vi();
        h.Type("a");
        h.Keys("S-RET");
        h.Type("  b");
        h.Keys("ESC");
        h.Type("kJ");
        Assert.Equal("a| b", h.State);
        h.Type(">>");
        Assert.Equal("    |a b", h.State);
        h.Type("<<");
        Assert.Equal("|a b", h.State);
        h = Normal("x = 41", 0);
        h.Keys("C-a");
        Assert.Equal("x = 4|2", h.State);
        h.Type("5");
        h.Keys("C-x");
        Assert.Equal("x = 3|7", h.State);
    }

    [Fact]
    public void AcceptFromNormalMode()
    {
        var h = Normal("echo ok", 2);
        h.Keys("RET");
        Assert.True(h.E.Done);
        Assert.Equal("echo ok", h.E.Result);
    }

    [Fact]
    public void UserBindingInNormalMode()
    {
        var h = Normal("abc", 2);
        Keymaps.Get("vi_nmap").Bind("K", "beginning-of-line");
        h.Type("K");
        Assert.Equal("|abc", h.State);
    }

    [Fact]
    public void MacroBindingYanksSelectionThroughRegister()
    {
        var h = Normal("hello world", 0);
        Keymaps.Get("vi_xmap").Bind(KeyNames.ParseSpec("Y"), new Binding(BindingKind.Macro, "\" + y"));
        h.Type("veY");
        Assert.Equal("vi_nmap", h.E.Keymap);
        Assert.Equal("hello", Editor.Registers['"'].Text);
        Assert.Equal("|hello world", h.State);
    }

    [Fact]
    public void ModeLine()
    {
        var h = new Harness().Vi();
        var m = h.E.BuildModel();
        Assert.Contains(m.Panels, p => p.PlainText == "-- INSERT --");
        h.Keys("ESC");
        m = h.E.BuildModel();
        Assert.DoesNotContain(m.Panels, p => p.PlainText.Contains("INSERT"));
        h.Type("v");
        m = h.E.BuildModel();
        Assert.Contains(m.Panels, p => p.PlainText == "-- VISUAL --");
    }

    // A paste reaches the editor as keys that are all in the input buffer at once; the pauses make
    // the editor wait, as it does between typed keys.
    static string RunTerminal(Action<FakeTerminal> keys)
    {
        var h = new Harness().Vi();
        keys(h.T);
        h.E.Renderer.Begin(0);
        try { return h.E.ReadLine(); }
        catch (InputExhaustedException) { return h.E.Text; }   // the line was not accepted
    }

    [Fact]
    public void PasteInNormalModeInsertsTheText()
    {
        // after the cursor, then back to normal mode (ble.sh vi-command/bracketed-paste)
        Assert.Equal("echo hi there", RunTerminal(t => t.Type("echo hi").Keys("ESC").Pause().Type(" there").Pause().Pause().Keys("RET")));
        // at the start of a line: before the cursor
        Assert.Equal("hello world", RunTerminal(t => t.Type("world").Keys("ESC", "0").Pause().Type("hello ").Pause().Pause().Keys("RET")));
        // newlines in the paste don't run the line; one undo step removes the paste
        Assert.Equal("abc\nd", RunTerminal(t => t.Type("ab").Keys("ESC").Pause().Type("c").Keys("RET").Type("d").Pause().Pause()));
        Assert.Equal("x", RunTerminal(t => t.Type("x").Keys("ESC").Pause().Type("dd and more").Pause().Pause().Type("u").Keys("RET")));
    }

    [Fact]
    public void PasteInVisualModeReplacesTheSelection()
    {
        Assert.Equal("baz bar", RunTerminal(t => t.Type("foo bar").Keys("ESC", "0", "v", "e").Pause().Type("baz").Pause().Pause().Keys("RET")));
    }

    [Fact]
    public void TypedKeysInNormalModeStayCommands()
    {
        // one key at a time is typing, and so are short bursts (two keys pressed at once)
        Assert.Equal("ab", RunTerminal(t => t.Type("abc").Keys("ESC").Pause().Type("x").Pause().Keys("RET")));
        Assert.Equal("", RunTerminal(t => t.Type("abc").Keys("ESC").Pause().Type("dd").Pause().Pause().Keys("RET")));
    }
}
