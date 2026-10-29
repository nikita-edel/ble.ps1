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
    public void RankingPrefersTheDirectoryAndSkipsInvalid()
    {
        var h = new History();
        h.Add("cd a", @"C:\work\a");
        h.Add("cd b", @"C:\work\b");
        h.Add("cd c");
        string? Best(string? dir, Func<string, bool>? valid = null, int max = 50) =>
            new HistoryRanking(h, dir, valid, max).Best(h.PrefixMatches("cd "));
        Assert.Equal("cd a", Best(@"C:\work\a"));
        Assert.Equal("cd b", Best(@"C:\work\b" + Path.DirectorySeparatorChar));   // trailing separator
        Assert.Equal("cd c", Best(@"C:\elsewhere"));
        Assert.Equal("cd c", Best(null));
        // invalid entries are skipped, also the one from this directory
        Assert.Equal("cd b", Best(@"C:\work\a", l => l != "cd a" && l != "cd c"));
        Assert.Null(Best(@"C:\work\a", _ => false));
        // validation runs for at most maxChecks entries
        int calls = 0;
        Assert.Null(Best(null, _ => { calls++; return false; }, max: 2));
        Assert.Equal(2, calls);
        if (OperatingSystem.IsWindows()) Assert.True(h.RanIn("cd a", @"c:\WORK\A"));

        // Top: this directory first, then newest first; a full list still takes entries from here
        var top = new HistoryRanking(h, @"C:\work\a", null, 50).Top(h.PrefixMatches("cd "), 2);
        Assert.Equal(new[] { "cd a", "cd c" }, top);
    }

    [Fact]
    public void WordsThatWerePathsWhereTheEntryRanMustExistHere()
    {
        var home = Directory.CreateTempSubdirectory("ble-test-").FullName;
        try
        {
            var dev = Directory.CreateDirectory(Path.Combine(home, "dev")).FullName;
            var h = new Harness();
            h.S.Commands["cd"] = CommandKind.Alias;
            h.H.Add("cd dev", home);                       // dev was a directory where it ran
            h.H.Add("git checkout origin/main", home);     // origin/main was never a path
            Options.Set("complete_auto_history_validate", "command:path");
            h.S.Cwd = dev;
            h.Type("cd ");
            Assert.Null(h.E.Suggestion);                   // ~\dev\dev doesn't exist
            h.Keys("C-u"); h.Type("git ");
            Assert.Equal("checkout origin/main", h.E.Suggestion);
            h.S.Cwd = home;
            h.E.Highlighter.ResetCaches();
            h.Keys("C-u"); h.Type("cd ");
            Assert.Equal("dev", h.E.Suggestion);
        }
        finally { Directory.Delete(home, true); }
    }

    [Fact]
    public void DirectoriesAreSavedSharedAndRewritten()
    {
        Harness.ResetGlobals();
        var path = Path.Combine(Path.GetTempPath(), "ble-test-" + Guid.NewGuid() + ".txt");
        try
        {
            var h1 = new History(); h1.Load(path);
            h1.Add("make", @"C:\proj");
            h1.Add("multi\nline", @"C:\proj");
            h1.Add("my token is x", @"C:\proj");     // memory only, also its directory
            var h2 = new History(); h2.Load(path);
            Assert.True(h2.RanIn("make", @"C:\proj"));
            Assert.True(h2.RanIn("multi\nline", @"C:\proj"));
            Assert.DoesNotContain("token", File.ReadAllText(path + ".dirs"));
            h1.Add("make", @"C:\other");
            h2.Sync();
            Assert.True(h2.RanIn("make", @"C:\other"));
            Assert.True(h2.RanIn("make", @"C:\proj"));
            // Rewrite (ble-history -d, Clear-BleHistory) drops the directories of removed entries
            h2.RemoveRange(h2.Items.ToList().IndexOf("multi\nline"), 1);
            h2.Rewrite();
            Assert.DoesNotContain("multi", File.ReadAllText(path + ".dirs"));
            var h3 = new History(); h3.Load(path);
            Assert.True(h3.RanIn("make", @"C:\other"));
        }
        finally { File.Delete(path); File.Delete(path + ".dirs"); }
    }

    [Fact]
    public void SuggestionsSkipUnknownCommandsAndMissingPaths()
    {
        var dir = Directory.CreateTempSubdirectory("ble-test-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "real"));
            // ./real on Linux and macOS (a backslash is part of a file name there)
            string real = "." + Path.DirectorySeparatorChar + "real", gone = "." + Path.DirectorySeparatorChar + "gone";
            var h = new Harness("cd " + real, "git log", "cd " + gone, "nosuchcmd x");
            h.S.Cwd = dir;
            h.S.Commands["cd"] = CommandKind.Alias;
            Options.Set("complete_auto_history_validate", "command:path");
            h.Type("cd ");
            Assert.Equal(real, h.E.Suggestion);
            h.Keys("C-u"); h.Type("n");
            Assert.Null(h.E.Suggestion);               // nosuchcmd is not a command
            Options.Set("complete_auto_history_validate", "path");
            h.Keys("C-u"); h.Type("n");
            Assert.Equal("osuchcmd x", h.E.Suggestion);
            Options.Set("complete_auto_history_validate", "");
            h.Keys("C-u"); h.Type("cd ");
            Assert.Equal(gone, h.E.Suggestion);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void SuggestionsPreferTheCurrentDirectory()
    {
        var h = new Harness();
        h.H.Add("build here", @"C:\proj");
        h.H.Add("build elsewhere", @"C:\other");
        h.S.Cwd = @"C:\proj";
        h.Type("build ");
        Assert.Equal("here", h.E.Suggestion);
        Options.Set("complete_auto_history_cwd", "0");
        h.Keys("C-u"); h.Type("build ");
        Assert.Equal("elsewhere", h.E.Suggestion);

        Options.Set("complete_auto_history_cwd", "1");
        Options.Set("complete_auto_view", "list");
        h.H.Add("build again", @"C:\other");
        h.H.Add("rebuild", @"C:\proj");
        h.Keys("C-u"); h.Type("build");
        // this directory first in each group (prefix matches, then substring matches)
        Assert.Equal(new[] { "build here", "build again", "build elsewhere", "rebuild" }, h.E.ListViewItems);
    }

    [Fact]
    public void ListViewLeavesOutInvalidEntries()
    {
        var h = new Harness("git status", "nosuchcmd git", "git log");
        Options.Set("complete_auto_history_validate", "command:path");
        Options.Set("complete_auto_view", "list");
        h.Type("git");
        Assert.Equal(new[] { "git log", "git status" }, h.E.ListViewItems);
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
