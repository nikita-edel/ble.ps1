using Ble;
using Xunit;

namespace Ble.Tests;

public class HistoryTests
{
    [Fact]
    public void ParsesPSReadLineFormat()
    {
        var entries = History.Parse("ls\r\nif ($x) {`\r\n  1`\r\n}\r\n\r\nGet-Date\r\n");
        Assert.Equal(new[] { "ls", "if ($x) {\n  1\n}", "Get-Date" }, entries);
        Assert.Equal("a`\nb", History.Serialize("a\r\nb"));
    }

    [Fact]
    public void DeduplicatesKeepingNewest()
    {
        var h = new History();
        h.AddRange(["a", "b", "a", "c"]);
        Assert.Equal(new[] { "b", "a", "c" }, h.Items);
    }

    [Fact]
    public void FileRoundTripAndShare()
    {
        Harness.ResetGlobals();
        var path = Path.Combine(Path.GetTempPath(), "ble-test-" + Guid.NewGuid() + ".txt");
        try
        {
            var h1 = new History(); h1.Load(path);
            h1.Add("echo 1");
            h1.Add("multi\nline");
            h1.Add(" secret-space");          // ignored (leading space)
            h1.Add("my password is x");       // memory only
            Assert.Equal(3, h1.Count);
            var h2 = new History(); h2.Load(path);
            Assert.Equal(new[] { "echo 1", "multi\nline" }, h2.Items);
            h1.Add("from h1");
            h2.Sync();
            Assert.Equal("from h1", h2.Items[^1]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FindPrefixReturnsMostRecent()
    {
        var h = new History();
        h.AddRange(["git status", "git commit -m x", "ls", "git"]);
        Assert.Equal("git commit -m x", h.FindPrefix("git"));
        Assert.Equal("git status", h.FindPrefix("git s"));
        Assert.Null(h.FindPrefix("nope"));
        Assert.Null(h.FindPrefix("ls"));
    }

    [Fact]
    public void UpDownNavigatesAndKeepsEdits()
    {
        var h = new Harness("one", "two", "three");
        h.Type("new");
        h.Keys("up");
        Assert.Equal("three|", h.State);
        h.Keys("up");
        h.Type("!");
        Assert.Equal("two!|", h.State);
        h.Keys("up", "down");
        Assert.Equal("two!|", h.State);
        h.Keys("down", "down");
        Assert.Equal("new|", h.State);
        h.Keys("M-<");
        Assert.Equal("one|", h.State);
        h.Keys("M->");
        Assert.Equal("new|", h.State);
    }

    [Fact]
    public void PrefixSearch()
    {
        var h = new Harness("git status", "ls", "git log", "dir");
        h.Type("git");
        h.Keys("prior");
        Assert.Equal("git| log", h.State);
        h.Keys("prior");
        Assert.Equal("git| status", h.State);
        h.Keys("next");
        Assert.Equal("git| log", h.State);
    }

    [Fact]
    public void IncrementalSearch()
    {
        var h = new Harness("Get-ChildItem", "echo hello", "Get-Content foo", "dir");
        h.Keys("C-r");
        h.Type("get");
        Assert.Equal("|Get-Content foo", h.State);
        Assert.Contains("reverse-i-search", h.E.Info);
        h.Keys("C-r");
        Assert.Equal("|Get-ChildItem", h.State);
        h.Keys("DEL");
        Assert.Equal("|Get-Content foo", h.State);
        h.Keys("C-g");
        Assert.Equal("|", h.State);
        h.Keys("C-r");
        h.Type("hel");
        h.Keys("C-e");
        Assert.Equal("echo hello|", h.State);
        Assert.Null(h.E.Modal);
        h.Keys("C-r");
        h.Type("zzz");
        Assert.Contains("failed", h.E.Info);
        h.Keys("RET");
        Assert.True(h.E.Done);
    }

    [Fact]
    public void YankLastArg()
    {
        var h = new Harness("cp a.txt b.txt", "echo first");
        h.Type("cat ");
        h.Keys("M-.");
        Assert.Equal("cat first|", h.State);
        h.Keys("M-.");
        Assert.Equal("cat b.txt|", h.State);
    }

    [Fact]
    public void HistorySuggestionAcceptance()
    {
        var h = new Harness("git commit -m 'msg'", "ls");
        h.Type("git c");
        Assert.Equal("ommit -m 'msg'", h.E.Suggestion);
        h.Type("om");
        Assert.Equal("mit -m 'msg'", h.E.Suggestion);
        h.Keys("M-f");
        Assert.Equal("git commit|", h.State);
        Assert.Equal(" -m 'msg'", h.E.Suggestion);
        h.Keys("right");
        Assert.Equal("git commit -m 'msg'|", h.State);
        Assert.Null(h.E.Suggestion);
    }

    [Fact]
    public void BleShDefaultSuggestionKeys()
    {
        var h = new Harness("git commit -m 'msg'");
        h.Type("git c");
        h.Keys("S-RET");
        Assert.Equal("git commit -m 'msg'|", h.State);
        Assert.False(h.E.Done);
        h.Keys("S-RET");                       // no suggestion now: newline
        Assert.Equal("git commit -m 'msg'\n|", h.State);

        h = new Harness("echo one two");
        h.Type("echo o");
        h.Keys("C-g");                         // dismiss
        Assert.Null(h.E.Suggestion);
        h.Type("n");
        Assert.Equal("e two", h.E.Suggestion);  // comes back once the text changes
        h.Keys("C-j");                          // accept with suggestion and run
        Assert.True(h.E.Done);
        Assert.Equal("echo one two", h.E.Result);

        h = new Harness("ls C:/some/dir x");
        h.Type("ls");
        h.Keys("M-right");                      // whole unix word
        Assert.Equal("ls C:/some/dir|", h.State);
    }

    [Fact]
    public void SuggestionHiddenWhenCursorNotAtEnd()
    {
        var h = new Harness("echo hello");
        h.Type("echo");
        Assert.NotNull(h.E.Suggestion);
        h.Keys("left");
        Assert.Null(h.E.Suggestion);
    }
}
