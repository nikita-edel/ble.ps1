using Ble;
using Xunit;

namespace Ble.Tests;

public class CompletionTests
{
    [Fact]
    public void SingleCandidateInsertsWithSpace()
    {
        var h = new Harness();
        h.S.CompleteFrom("Get-ChildItem", "Set-Location");
        h.Type("Get-Ch");
        h.Keys("TAB");
        Assert.Equal("Get-ChildItem |", h.State);
        Assert.Null(h.E.Menu);
    }

    [Fact]
    public void DirectoryCandidateGetsNoSpace()
    {
        var h = new Harness();
        h.S.CompleteFrom(@".\src\");
        h.Type("cd .\\s");
        h.Keys("TAB");
        Assert.Equal(@"cd .\src\|", h.State);
    }

    [Fact]
    public void MultipleCandidatesInsertCommonPrefixAndShowMenu()
    {
        var h = new Harness();
        h.S.CompleteFrom("Get-Content", "Get-Command", "Get-ChildItem");
        h.Type("get-co");
        h.Keys("TAB");
        Assert.Equal("Get-Co|", h.State);
        Assert.NotNull(h.E.Menu);
        Assert.False(h.E.Menu!.Selecting);
        Assert.Equal(2, h.E.Menu.Items.Count);
        // second TAB enters menu-complete
        h.Keys("TAB");
        Assert.True(h.E.Menu!.Selecting);
        Assert.Equal("Get-Content|", h.State);
        h.Keys("TAB");
        Assert.Equal("Get-Command|", h.State);
        h.Keys("TAB");
        Assert.Equal("Get-Content|", h.State);
        h.Keys("S-TAB");
        Assert.Equal("Get-Command|", h.State);
        // RET accepts the candidate without executing
        h.Keys("RET");
        Assert.False(h.E.Done);
        Assert.Null(h.E.Menu);
        Assert.Equal("Get-Command|", h.State);
        // and undo restores the pre-selection text
        h.Keys("C-_");
        Assert.Equal("Get-Co|", h.State);
    }

    [Fact]
    public void EscapeCancelsMenuComplete()
    {
        var h = new Harness();
        h.S.CompleteFrom("alpha", "alpine");
        h.Type("al");
        h.Keys("TAB", "TAB");
        Assert.Equal("alpha|", h.State);
        h.Keys("C-g");
        Assert.Equal("alp|", h.State);
        Assert.Null(h.E.Menu);
    }

    [Fact]
    public void TypingDuringMenuCompleteAcceptsAndInserts()
    {
        var h = new Harness();
        h.S.CompleteFrom("alpha", "alpine");
        h.Type("al");
        h.Keys("TAB", "TAB");
        h.Type(" x");
        Assert.Equal("alpha x|", h.State);
        Assert.Null(h.E.Menu);
    }

    [Fact]
    public void MenuFiltersWhileTyping()
    {
        var h = new Harness();
        h.S.CompleteFrom("abc1", "abd2", "abd3");
        h.Type("a");
        h.Keys("TAB");
        Assert.Equal("ab|", h.State);
        Assert.Equal(3, h.E.Menu!.Items.Count);
        h.Type("d");
        Assert.Equal(2, h.E.Menu!.Items.Count);
        h.Keys("DEL");
        Assert.Equal(3, h.E.Menu!.Items.Count);
        h.Type("z");
        Assert.Null(h.E.Menu);
        h.Keys("DEL", "TAB");
        Assert.NotNull(h.E.Menu);
        h.Keys("C-a");
        Assert.Null(h.E.Menu);
    }

    [Fact]
    public void NoCandidates()
    {
        var h = new Harness();
        h.S.CompleteFrom("x");
        h.Type("zz");
        h.Keys("TAB");
        Assert.Equal("zz|", h.State);
        Assert.Contains("no completions", h.E.Info);
    }

    [Fact]
    public void InsertCompletions()
    {
        var h = new Harness();
        h.S.CompleteFrom("a1", "a2");
        h.Type("a");
        h.Keys("M-*");
        Assert.Equal("a1 a2 |", h.State);
    }

    [Fact]
    public void MenuGridNavigation()
    {
        var h = new Harness();
        var names = Enumerable.Range(0, 12).Select(i => "item" + i.ToString("00")).ToArray();
        h.S.CompleteFrom(names);
        h.T.Width = 40;
        h.E.Renderer.Begin(4);
        h.Type("i");
        h.Keys("TAB");
        var lines = h.E.Menu!.Layout(40, 10);
        Assert.True(h.E.Menu.Columns >= 2);
        Assert.Contains("item00", lines[0].PlainText);
        h.Keys("TAB");              // select item00
        h.Keys("down");             // next row
        Assert.Equal("item" + h.E.Menu!.Columns.ToString("00") + "|", h.State);
        h.Keys("right");
        Assert.Equal("item" + (h.E.Menu!.Columns + 1).ToString("00") + "|", h.State);
    }

    [Fact]
    public void CompletionBasedSuggestionWhenIdle()
    {
        var h = new Harness();
        Options.Set("complete_auto_completion", "1");
        Options.Set("complete_auto_delay", "50");
        h.S.CompleteFrom("Get-ChildItem");
        h.T.Type("get-chi").Pause().Pause().Pause().Pause().Pause().Pause().Pause().Pause();
        h.T.Keys("right", "RET");
        h.E.Renderer.Begin(0);
        var result = h.E.ReadLine();
        Assert.Equal("Get-ChildItem", result);
    }
}
