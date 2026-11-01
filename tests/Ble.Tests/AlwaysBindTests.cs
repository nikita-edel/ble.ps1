using Ble;
using Xunit;

namespace Ble.Tests;

public class AlwaysBindTests
{
    static void Always(string keys, string widget) => Keymaps.Always.Bind(KeyNames.ParseSpec(keys), Binding.W(widget));

    static Harness Line(Harness h)
    {
        h.Type("echo abc");
        return h;
    }

    [Theory]
    [InlineData("emacs")]
    [InlineData("vi_imap")]
    public void WorksInInsertKeymaps(string keymap)
    {
        var h = new Harness();
        if (keymap == "vi_imap") h.Vi();
        Always("f9", "beginning-of-line");
        Line(h).Keys("f9");
        Assert.Equal("|echo abc", h.State);
    }

    [Theory]
    [InlineData("ESC")]            // vi_nmap
    [InlineData("ESC", "d")]       // an operator waits for its motion
    [InlineData("ESC", "f")]       // f waits for its character
    [InlineData("ESC", "\"")]      // " waits for its register
    public void WorksInEveryViState(params string[] keys)
    {
        var h = new Harness().Vi();
        Always("f9", "beginning-of-line");
        Line(h).Keys(keys).Keys("f9");
        Assert.Equal(0, h.Point);
        Assert.Equal("echo abc", h.Text);
        // the waiting command is gone: the next key is a new command
        h.Keys("x");
        Assert.Equal("cho abc", h.Text);
    }

    [Fact]
    public void WorksInVisualMode()
    {
        var h = new Harness().Vi();
        Always("f9", "beginning-of-line");
        Line(h).Keys("ESC", "v", "f9");
        Assert.Equal(0, h.Point);
        Assert.Equal("echo abc", h.Text);
    }

    [Fact]
    public void BeatsKeysViHandlesItself()
    {
        var h = new Harness().Vi();
        Always("h", "beginning-of-line");
        Line(h).Keys("ESC", "h");
        Assert.Equal(0, h.Point);
    }

    [Fact]
    public void EndsIsearch()
    {
        var h = new Harness("echo one", "ls");
        Always("f9", "end-of-line");
        h.Keys("C-r").Type("ech").Keys("f9");
        Assert.Null(h.E.Modal);
        Assert.Equal("echo one|", h.State);
        h.Type("x");
        Assert.Equal("echo onex|", h.State);
    }

    [Fact]
    public void EndsTheMenu()
    {
        var h = new Harness();
        Always("f9", "beginning-of-line");
        h.S.CompleteFrom("Get-Alias", "Get-Acl");
        h.Type("Get-A").Keys("TAB", "TAB");
        Assert.NotNull(h.E.Modal);
        h.Keys("f9");
        Assert.Null(h.E.Modal);
        Assert.Null(h.E.Menu);
        Assert.Equal(0, h.Point);
    }

    [Fact]
    public void BeatsQuotedInsert()
    {
        var h = new Harness();
        Always("f9", "beginning-of-line");
        h.Type("ab").Keys("C-v", "f9");
        Assert.Equal("|ab", h.State);
        Assert.Null(h.E.Modal);
    }

    [Fact]
    public void BeatsViExPrompt()
    {
        var h = new Harness().Vi();
        Always("f9", "beginning-of-line");
        Line(h).Keys("ESC", ":").Keys("f9");
        Assert.Null(h.E.Modal);
        Assert.Equal(0, h.Point);
    }

    [Fact]
    public void HistoryWidgetsStillMoveThroughTheListView()
    {
        var h = new Harness("git status", "git log");
        Options.Set("complete_auto_view", "list");
        Always("f9", "history-search-backward");
        h.Type("git").Keys("f9");
        Assert.Equal(0, h.E.ListViewSelected);
        Assert.Equal("git log", h.Text);
    }

    [Fact]
    public void OtherWidgetsLeaveTheListViewAlone()
    {
        var h = new Harness("git status");
        Options.Set("complete_auto_view", "list");
        Always("up", "beginning-of-line");
        h.Type("git").Keys("up");
        Assert.Equal("|git", h.State);
    }

    [Fact]
    public void SequencesThatDontFinishAreOrdinaryKeys()
    {
        var h = new Harness();
        Always("f9 x", "beginning-of-line");
        h.Keys("a");
        h.Keys("f9", "x");
        Assert.Equal("|a", h.State);

        h = new Harness();
        Always("C-x f", "beginning-of-line");
        h.Type("ab").Keys("C-x", "C-u");   // undo
        Assert.Equal("|", h.State);
    }

    [Fact]
    public void RemovingItBringsTheOldKeyBack()
    {
        var h = new Harness();
        Always("C-a", "end-of-line");
        Keymaps.Always.Bind(KeyNames.ParseSpec("C-a"), null);
        h.Type("ab").Keys("C-a");
        Assert.Equal("|ab", h.State);
    }

    [Fact]
    public void RestoreGivesTheDefaultBackInAllKeymaps()
    {
        var c = KeyNames.ParseSpec("C-r");
        Assert.True(Keymaps.Get("vi_imap").TryGet("C-r", out var original));
        foreach (var name in Keymaps.Names) Keymaps.Get(name).Bind(c, Binding.W("undo"));
        Keymaps.Always.Bind(c, Binding.W("undo"));

        Keymaps.RestoreDefault(c);

        Assert.True(Keymaps.Get("vi_imap").TryGet("C-r", out var now));
        Assert.Equal(original, now);
        Assert.True(Keymaps.Get("emacs").TryGet("C-r", out _));
        Assert.False(Keymaps.Get("vi_nmap").TryGet("C-r", out _));   // vi's redo: not a binding
        Assert.False(Keymaps.Always.TryGet("C-r", out _));
    }

    [Fact]
    public void RestoreInOneKeymapLeavesTheOthers()
    {
        var c = KeyNames.ParseSpec("C-r");
        foreach (var name in new[] { "emacs", "vi_imap" }) Keymaps.Get(name).Bind(c, Binding.W("undo"));
        Keymaps.Always.Bind(c, Binding.W("undo"));

        Keymaps.RestoreDefault(c, "vi_imap");

        Assert.True(Keymaps.Get("vi_imap").TryGet("C-r", out var imap));
        Assert.Equal("history-isearch-backward", imap.Widget);
        Assert.True(Keymaps.Get("emacs").TryGet("C-r", out var emacs));
        Assert.Equal("undo", emacs.Widget);
        Assert.True(Keymaps.Always.TryGet("C-r", out _));
        Keymaps.Reset();
    }

    [Fact]
    public void RestoreAllPutsEveryKeyBack()
    {
        var before = Keymaps.Names.ToDictionary(n => n, n => Keymaps.Get(n).Bindings.ToList());
        Keymaps.Get("vi_imap").Bind(KeyNames.ParseSpec("C-x z q"), Binding.W("undo"));
        Keymaps.Get("vi_imap").Bind(KeyNames.ParseSpec("C-r"), null);
        Keymaps.Get("vi_nmap").Bind(KeyNames.ParseSpec("K"), Binding.W("undo"));
        Keymaps.Always.Bind(KeyNames.ParseSpec("f9"), Binding.W("undo"));

        Keymaps.RestoreAllDefaults();

        foreach (var n in Keymaps.Names) Assert.Equal(before[n], Keymaps.Get(n).Bindings.ToList());
        Assert.True(Keymaps.Always.IsEmpty);
        Assert.True(Keymaps.Get("vi_imap").IsPrefix("C-x"));
        Assert.False(Keymaps.Get("vi_imap").IsPrefix("C-x z"));
    }

    [Fact]
    public void RestoreAllInOneKeymapLeavesTheRest()
    {
        Keymaps.Get("vi_imap").Bind(KeyNames.ParseSpec("C-r"), Binding.W("undo"));
        Keymaps.Get("emacs").Bind(KeyNames.ParseSpec("C-r"), Binding.W("undo"));
        Keymaps.Always.Bind(KeyNames.ParseSpec("f9"), Binding.W("undo"));

        Keymaps.RestoreAllDefaults("vi_imap");

        Assert.True(Keymaps.Get("vi_imap").TryGet("C-r", out var imap));
        Assert.Equal("history-isearch-backward", imap.Widget);
        Assert.True(Keymaps.Get("emacs").TryGet("C-r", out var emacs));
        Assert.Equal("undo", emacs.Widget);
        Assert.False(Keymaps.Always.IsEmpty);
        Keymaps.Reset();
    }

    [Fact]
    public void RestoreRemovesWhatHasNoDefault()
    {
        var h = new Harness().Vi();
        Keymaps.Get("vi_nmap").Bind(KeyNames.ParseSpec("C-r"), Binding.W("beginning-of-line"));
        Keymaps.RestoreDefault(KeyNames.ParseSpec("C-r"));
        Line(h).Keys("ESC", "x", "u", "C-r");
        Assert.Equal("echo ab", h.Text);   // vi's redo works again
    }
}
