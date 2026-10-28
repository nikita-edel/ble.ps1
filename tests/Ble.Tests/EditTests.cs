using Ble;
using Xunit;

namespace Ble.Tests;

public class EditTests
{
    [Fact]
    public void TypingAndCursorMovement()
    {
        var h = new Harness().Type("hello world");
        Assert.Equal("hello world|", h.State);
        h.Keys("C-a");
        Assert.Equal("|hello world", h.State);
        h.Keys("M-f");
        Assert.Equal("hello| world", h.State);
        h.Keys("C-f", "C-f");
        Assert.Equal("hello w|orld", h.State);
        h.Keys("M-b");
        Assert.Equal("hello |world", h.State);
        h.Keys("C-e");
        Assert.Equal("hello world|", h.State);
        h.Keys("C-b", "left");
        Assert.Equal("hello wor|ld", h.State);
    }

    [Fact]
    public void KillAndYank()
    {
        var h = new Harness().Type("echo foo bar");
        h.Keys("M-b", "C-k");
        Assert.Equal("echo foo |", h.State);
        h.Keys("C-a", "C-y");
        Assert.Equal("bar|echo foo ", h.State);
        h.Keys("C-e", "C-w");
        Assert.Equal("barecho |", h.State);
        h.Keys("C-y");
        Assert.Equal("barecho foo |", h.State);
        // yank-pop cycles the kill ring
        h.Keys("C-u");
        Assert.Equal("|", h.State);
        h.Keys("C-y", "M-y");
        Assert.Equal("foo |", h.State);
    }

    [Fact]
    public void ConsecutiveKillsAppend()
    {
        var h = new Harness().Type("a b c");
        h.Keys("C-w", "C-w");
        Assert.Equal("a |", h.State);
        h.Keys("C-y");
        Assert.Equal("a b c|", h.State);
    }

    [Fact]
    public void DeleteCharAndBackspace()
    {
        var h = new Harness().Type("abc");
        h.Keys("DEL");
        Assert.Equal("ab|", h.State);
        h.Keys("C-a", "C-d");
        Assert.Equal("|b", h.State);
        h.Keys("delete");
        Assert.Equal("|", h.State);
        h.Keys("C-d");
        Assert.True(h.E.Done);
        Assert.Equal("exit", h.E.Result);
    }

    [Fact]
    public void UndoRedoGroupsInsertedWords()
    {
        var h = new Harness().Type("echo hello");
        h.Keys("C-_");
        Assert.Equal("echo |", h.State);
        h.Keys("C-_");
        Assert.Equal("echo|", h.State);
        h.Keys("C-x", "U");   // ble.sh redo (C-x C-r is dabbrev-expand)
        Assert.Equal("echo |", h.State);
        h.Keys("C-_", "C-_");
        Assert.Equal("|", h.State);
    }

    [Fact]
    public void TransposeAndCase()
    {
        var h = new Harness().Type("ab");
        h.Keys("C-t");
        Assert.Equal("ba|", h.State);
        h = new Harness().Type("foo bar");
        h.Keys("M-t");
        Assert.Equal("bar foo|", h.State);
        h.Keys("C-a", "M-u");
        Assert.Equal("BAR| foo", h.State);
        h.Keys("M-c");
        Assert.Equal("BAR Foo|", h.State);
        h.Keys("M-b", "M-l");
        Assert.Equal("BAR foo|", h.State);
    }

    [Fact]
    public void DigitArgument()
    {
        var h = new Harness().Type("abcdef");
        h.Keys("M-3", "C-b");
        Assert.Equal("abc|def", h.State);
        h.Keys("M-2", "x");
        Assert.Equal("abcxx|def", h.State);
    }

    [Fact]
    public void MarkAndRegion()
    {
        var h = new Harness().Type("one two three");
        h.Keys("M-b", "C-@", "M-b", "C-w");
        Assert.Equal("one |three", h.State);
        h.Keys("C-e", "C-y");
        Assert.Equal("one threetwo |", h.State);
    }

    [Fact]
    public void ShiftSelectionReplacesOnType()
    {
        var h = new Harness().Type("hello world");
        h.Keys("S-left", "S-left", "S-left", "S-left", "S-left");
        Assert.True(h.E.MarkActive);
        h.Type("there");
        Assert.Equal("hello there|", h.State);
        Assert.False(h.E.MarkActive);
    }

    [Fact]
    public void MultilineEditing()
    {
        var h = new Harness();
        h.Type("if ($true) {");
        h.Keys("RET");
        Assert.Equal("if ($true) {\n|", h.State);
        Assert.False(h.E.Done);
        h.Type("  1");
        h.Keys("RET");
        h.Type("}");
        h.Keys("up");
        Assert.Equal("if ($true) {\n | 1\n}", h.State);
        h.Keys("up");
        Assert.Equal("i|f ($true) {\n  1\n}", h.State);
        h.Keys("C-e", "down", "down");
        Assert.Equal("if ($true) {\n  1\n}|", h.State);
        // ble.sh accept-single-line-or-newline: RET in a multi-line buffer inserts a newline, C-j runs it
        h.Keys("RET");
        Assert.False(h.E.Done);
        h.Keys("DEL", "C-j");
        Assert.True(h.E.Done);
        Assert.Equal("if ($true) {\n  1\n}", h.E.Result);
    }

    [Fact]
    public void QuotedInsert()
    {
        var h = new Harness();
        h.Keys("C-q", "TAB");
        Assert.Equal("\t|", h.State);
    }

    [Fact]
    public void OverwriteMode()
    {
        var h = new Harness().Type("abcd");
        h.Keys("C-a", "insert");
        h.Type("XY");
        Assert.Equal("XY|cd", h.State);
    }

    [Fact]
    public void AbbreviationsExpandOnSpaceAndAccept()
    {
        var h = new Harness();
        Editor.Abbrevs["gst"] = "git status";
        h.Type("gst ");
        Assert.Equal("git status |", h.State);
        h = new Harness();
        Editor.Abbrevs["L"] = "| less";
        h.Type("ls L");
        h.Keys("RET");
        Assert.Equal("ls | less", h.E.Result);
        // not inside a word
        h = new Harness();
        Editor.Abbrevs["L"] = "| less";
        h.Type("xL ");
        Assert.Equal("xL |", h.State);
    }

    [Fact]
    public void DiscardLine()
    {
        var h = new Harness().Type("abc");
        h.Keys("C-c");
        Assert.True(h.E.Done);
        Assert.Equal("", h.E.Result);
        Assert.Equal("^C", h.E.FinalSuffix);
    }

    [Fact]
    public void CharacterSearch()
    {
        var h = new Harness().Type("a-b-c");
        h.Keys("C-a", "C-]", "-");
        Assert.Equal("a|-b-c", h.State);
        h.Keys("C-]", "-");
        Assert.Equal("a-b|-c", h.State);
    }

    [Fact]
    public void UserBindingsAndUnknownWidget()
    {
        var h = new Harness().Type("abc");
        Keymaps.Get("emacs").Bind("C-x a", "beginning-of-line");
        h.Keys("C-x", "a");
        Assert.Equal("|abc", h.State);
        Keymaps.Get("emacs").Bind("C-x z", "no-such-widget");
        h.Keys("C-x", "z");
        Assert.Contains("unknown widget", h.E.Info);
        // undefined sequence: bell, nothing inserted
        h.Keys("C-x", "q");
        Assert.Equal("|abc", h.State);
    }

    [Fact]
    public void EscPrefixActsAsMeta()
    {
        var h = new Harness().Type("foo bar");
        h.Keys("ESC", "b");
        Assert.Equal("foo |bar", h.State);
    }

    [Fact]
    public void EditBindingUsesShell()
    {
        var h = new Harness().Type("abc");
        Keymaps.Get("emacs").Bind(KeyNames.ParseSpec("C-x x"), new Binding(BindingKind.Edit, "", System.Management.Automation.ScriptBlock.Create("1")));
        h.Keys("C-x", "x");
        Assert.Equal("abc!|", h.State);
    }
}
