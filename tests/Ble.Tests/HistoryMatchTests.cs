using Ble;
using Xunit;

namespace Ble.Tests;

// complete_auto_history_merge and complete_auto_history_fuzzy
public class HistoryMatchTests : IDisposable
{
    // merging paths that differ by case; on Linux they are different files (FileCaseTests)
    public HistoryMatchTests() => FileSystemPaths.CaseSensitive = false;
    public void Dispose() => FileSystemPaths.CaseSensitive = OperatingSystem.IsLinux();

    static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    static string Key(string s, bool paths = true, bool ignoreCase = true) => HistoryRanking.Key(s, paths, ignoreCase);

    [Theory]
    [InlineData("git  st", "git status", 6)]        // more space typed than in the entry
    [InlineData("git st", "git   status", 8)]       // less
    [InlineData("git\tst", "git status", 6)]
    [InlineData("git ", "git   status", 6)]         // up to where the next word starts
    [InlineData("git", "git   status", 3)]
    [InlineData("   git st", "git status", 6)]      // in front
    [InlineData("git st", "gitst", -1)]             // a space can't appear from nothing
    [InlineData("gitst", "git st", -1)]
    [InlineData("git  x", "git status", -1)]
    [InlineData("echo 'a b", "echo 'a  b'", -1)]    // in a string the space counts
    [InlineData("echo \"a  b", "echo \"a b\"", -1)]
    [InlineData("echo   'a b", "echo 'a b c'", 9)]
    [InlineData("echo 'it' x", "echo 'it'   x y", 13)]   // after the string it doesn't
    [InlineData("a.b  (c", "a.b (c)", 6)]           // the rest of the text is taken literally
    [InlineData("a.b  c", "axb c", -1)]
    public void SpaceBetweenWordsDoesNotCount(string query, string entry, int length) =>
        Assert.Equal(length, new HistoryQuery(query).PrefixLength(entry));

    [Theory]
    [InlineData("# todo", true)]
    [InlineData("   # todo", true)]
    [InlineData("#", true)]
    [InlineData("# a\n\n  # b", true)]
    [InlineData("<# a #>", true)]
    [InlineData("<# a\nb\n#>", true)]
    [InlineData("git status # todo", false)]
    [InlineData("# a\ngit status", false)]
    [InlineData("<# a #> git status", false)]
    [InlineData("<# a #> git # b #>", false)]
    [InlineData("Write-Output '#'", false)]
    [InlineData("<", false)]
    public void CommentEntries(string entry, bool comment) =>
        Assert.Equal(comment, HistoryRanking.IsComment(entry));

    [Fact]
    public void CommentsAreNotSuggested()
    {
        var h = new Harness("git status", "# git todo", "git old # keep");
        h.Type("# g");
        Assert.Null(h.E.Suggestion);
        h.Keys("C-u");
        h.Type("git o");
        Assert.Equal("ld # keep", h.E.Suggestion);  // a comment behind a command stays

        h = new Harness("# git todo", "<# git block #>", "git status");
        Options.Set("complete_auto_view", "list");
        h.Type("git");
        Assert.Equal(new[] { "git status" }, h.E.ListViewItems);

        h = new Harness("# git statux");            // similar entries too
        Options.Set("complete_auto_history_fuzzy", "1");
        h.Type("# git status");
        h.Idle();
        Assert.Null(h.E.Suggestion);

        h = new Harness("# todo", "git status");    // still history
        h.Keys("C-up", "C-up");
        Assert.Equal("# todo", h.Text);
    }

    [Theory]
    [InlineData("line")]
    [InlineData("whole")]
    public void CommentsInsideMultiLineEntries(string mode)
    {
        const string entry = "# git first\n<#\n  git block\n#>\n{\n  # git inner\n  git real\n}";
        var h = new Harness(entry);
        Options.Set("complete_auto_history_multiline", mode);
        h.Type("# g");
        if (mode == "line") Assert.Null(h.E.Suggestion);
        else Assert.Equal("it first" + entry.Substring("# git first".Length), h.E.Suggestion);   // code after them: not only comments
        h.Keys("C-u");
        h.Type("git");
        Assert.Equal(mode == "line" ? " real" : null, h.E.Suggestion);
    }

    [Theory]
    [InlineData("ls                -l", "ls -l")]
    [InlineData("  ls\t\t-l  ", "ls -l")]
    [InlineData("ls -l", "ls -l")]
    [InlineData("echo 'a   b'    c", "echo 'a   b' c")]
    [InlineData("echo \"a   b\"    c", "echo \"a   b\" c")]
    [InlineData("echo \"it's   x\"   y", "echo \"it's   x\" y")]
    [InlineData("echo a`   b   c", "echo a`  b c")]          // an escaped space is part of the word
    [InlineData("echo 'a   b", "echo 'a   b")]               // never closed
    [InlineData("if (1) {\n    a   b\n}", "if (1) {\n    a   b\n}")]
    public void EntriesAreSuggestedWithOneSpaceBetweenWords(string entry, string shown) =>
        Assert.Equal(shown, HistoryQuery.Tidy(entry));

    [Fact]
    public void SuggestionsDoNotCopyExtraSpaces()
    {
        const string spaced = "ls                -l";
        var h = new Harness(spaced);
        h.Type("ls");
        Assert.Equal(" -l", h.E.Suggestion);
        h.E.AcceptSuggestion(wordOnly: false);
        Assert.Equal("ls -l", h.Text);

        h = new Harness(spaced);                    // the entry itself stays as it is
        h.Keys("C-up");
        Assert.Equal(spaced, h.Text);

        h = new Harness(spaced, "ls -a");
        Options.Set("complete_auto_view", "list");
        h.Type("ls");
        Assert.Equal(new[] { "ls -a", "ls -l" }, h.E.ListViewItems);
        Assert.Equal(" -a", h.E.Suggestion);

        h = new Harness("ls -l", spaced);           // the same once, also without merging
        Options.Set("complete_auto_view", "list");
        Options.Set("complete_auto_history_merge", "");
        h.Type("ls");
        Assert.Equal(new[] { "ls -l" }, h.E.ListViewItems);
        h.Type(" -l");
        Assert.Null(h.E.ListViewItems);             // nothing to add to what is typed

        h = new Harness("git   status");            // similar entries too
        Options.Set("complete_auto_history_fuzzy", "1");
        h.Type("gti status");
        h.Idle();
        Assert.Equal(" [git status] ", h.E.Suggestion);
    }

    [Theory]
    [InlineData("git  st", "git status")]
    [InlineData("ls       -", "ls -l")]             // the typed text is longer than the entry
    public void AcceptingAWordOfTheListHintWithOtherSpacing(string typed, string entry)
    {
        var h = new Harness(entry);
        Options.Set("complete_auto_view", "list");
        h.Type(typed);
        Assert.True(h.E.AcceptSuggestion(wordOnly: true));
        Assert.Equal(entry, h.Text);
        Assert.Null(h.E.Info);
    }

    [Fact]
    public void AcceptingWordsOfTheListHintKeepsTheRest()
    {
        var h = new Harness("git status --short");
        Options.Set("complete_auto_view", "list");
        h.Type("git  st");
        h.E.AcceptSuggestion(wordOnly: true);
        Assert.Equal("git status", h.Text);
        Assert.Equal(" --short", h.E.Suggestion);
    }

    [Fact]
    public void QueryIsFoundInsideAnEntryWithOtherSpacing()
    {
        Assert.Equal((5, 7), new HistoryQuery("add   -p").Find("git  add  -p x") is { } m ? (m.Index, m.Length) : default);
        Assert.Null(new HistoryQuery("add   -p").Find("git add-p"));
        Assert.Equal((4, 3), new HistoryQuery("ADD", StringComparison.OrdinalIgnoreCase).Find("git add") is { } c ? (c.Index, c.Length) : default);
    }

    [Fact]
    public void SuggestionKeepsGoingWithOtherSpacing()
    {
        var h = new Harness("git status");
        h.Type("git  st");
        Assert.Equal("atus", h.E.Suggestion);
        h.Type("a");
        Assert.Equal("tus", h.E.Suggestion);
        h.E.AcceptSuggestion(wordOnly: false);
        Assert.Equal("git  status", h.Text);       // what you typed stays

        h = new Harness("git   status");
        h.Type("git ");
        Assert.Equal("status", h.E.Suggestion);

        h = new Harness("git status");
        h.Type("  git");
        Assert.Equal(" status", h.E.Suggestion);

        h = new Harness("git status");             // the same command: nothing to add
        h.Type("git  status");
        Assert.Null(h.E.Suggestion);
    }

    [Fact]
    public void ListViewKeepsGoingWithOtherSpacing()
    {
        var h = new Harness("git add -p", "git status");
        Options.Set("complete_auto_view", "list");
        h.Type("git  st");
        Assert.Equal(new[] { "git status" }, h.E.ListViewItems);
        Assert.Equal("atus", h.E.Suggestion);       // shown after the cursor
        Assert.Contains(h.E.BuildModel().Panels, p => p.Segments.Any(s => s.Text == "git st"));   // the match

        h = new Harness("git add  -p");            // inside an entry
        Options.Set("complete_auto_view", "list");
        h.Type("add   -");
        Assert.Equal(new[] { "git add -p" }, h.E.ListViewItems);
    }

    [Fact]
    public void KeyTreatsPathSpellingsAsTheSame()
    {
        var k = Key(@"cd ~\dev");
        Assert.Equal(k, Key("cd ~/dev"));
        Assert.Equal(k, Key(@"cd ~\dev\"));
        Assert.Equal(k, Key("cd   ~//dev/"));
        Assert.Equal(k, Key(Path.Combine(Home, "dev").Insert(0, "cd ")));
        Assert.Equal(k, Key("CD ~/Dev"));
        Assert.Equal(k, Key("cd '~/dev'"));
        Assert.Equal(Key("cd dev"), Key(@"cd .\dev"));
        Assert.NotEqual(k, Key("cd ~/dev2"));
        // without case: only the path spellings
        Assert.Equal(Key("cd ~/dev", ignoreCase: false), Key(@"cd ~\dev", ignoreCase: false));
        Assert.NotEqual(Key("cd ~/dev", ignoreCase: false), Key("cd ~/Dev", ignoreCase: false));
        // without paths: only the case
        Assert.NotEqual(Key("cd ~/dev", paths: false), Key(@"cd ~\dev", paths: false));
    }

    [Fact]
    public void KeyResolvesPathsFromTheCurrentFolder()
    {
        var dir = Directory.CreateTempSubdirectory("ble-key").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "sub"));
            bool Exists(string p) => Directory.Exists(p) || File.Exists(p);
            string K(string s) => HistoryRanking.Key(s, true, true, dir, Exists);
            var k = K("cd sub");                                  // a word that is a folder here
            Assert.Equal(k, K("cd " + Path.Combine(dir, "sub")));
            Assert.Equal(k, K(@"cd .\sub\"));
            Assert.Equal(k, K("cd sub/../sub"));
            Assert.Equal(K("cd .."), K("cd " + Path.GetDirectoryName(dir)));
            Assert.Equal("git status", K("git status"));          // not a file here: stays a word
            Assert.Equal("ls -force", K("ls -Force"));             // parameters stay
            Assert.NotEqual(k, K("cd sub2"));
            // ~ and the home folder from a folder under home
            Assert.Equal(HistoryRanking.Key("cd ~/x", true, true, Home, Exists), HistoryRanking.Key("cd x/", true, true, Home, _ => false));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ListViewShowsTheSameCommandOnce()
    {
        var h = new Harness("cd ~/dev", "cd ~/Desktop", @"cd ~\Dev\");
        Options.Set("complete_auto_view", "list");
        Options.Set("complete_auto_view_source", "history");
        h.Type("cd ~");
        h.Idle();
        Assert.Equal(new[] { @"cd ~\Dev\", "cd ~/Desktop" }, h.E.ListViewItems);
        // what is typed is not listed in another spelling either
        h.Keys("C-u");
        h.Type(@"cd ~\dev");
        h.Idle();
        Assert.Null(h.E.ListViewItems);
        // merging off: all of them
        Options.Set("complete_auto_history_merge", "");
        h.Keys("C-u");
        h.Type("cd ~");
        h.Idle();
        Assert.Equal(new[] { @"cd ~\Dev\", "cd ~/Desktop", "cd ~/dev" }, h.E.ListViewItems);
    }

    static int? Sim(string q, string e) =>
        HistoryRanking.Similarity(q.Split(' ', StringSplitOptions.RemoveEmptyEntries), !q.EndsWith(' '), e.Split(' '));

    [Fact]
    public void SimilarAllowsFewTypos()
    {
        Assert.NotNull(Sim("gti stauts", "git status"));
        Assert.NotNull(Sim("git sttaus", "git status"));
        Assert.NotNull(Sim("git sta", "git status"));
        Assert.NotNull(Sim("dokcer run nginx", "docker run -it nginx"));   // skips -it
        Assert.True(Sim("git stauts", "git status") < Sim("gti stauts", "git status"));
        Assert.Null(Sim("g s", "git status"));             // short words must match
        Assert.Null(Sim("hub status", "git status"));      // a different command
        Assert.Null(Sim("status", "git status"));          // the command must match
        Assert.Null(Sim("git sxyzs", "git status"));       // too many typos
        Assert.Null(Sim("git a b c d", "git status"));
    }

    [Fact]
    public void OftenRunEntriesBeatOldTypos()
    {
        var h = new Harness("git status", "git status", "git status", "git status", "git stauts");
        Assert.Equal(new[] { "git status", "git stauts" }, HistoryRanking.Similar(h.H, "gti stauts"));
        h = new Harness("git status", "git stauts");
        Assert.Equal(new[] { "git stauts", "git status" }, HistoryRanking.Similar(h.H, "gti stauts"));
    }

    [Fact]
    public void InlineSimilarSuggestion()
    {
        var h = new Harness("git status", "ls");
        h.Type("gti stat");
        h.Idle();
        Assert.Null(h.E.Suggestion);                       // off by default
        Options.Set("complete_auto_history_fuzzy", "1");
        Options.Set("complete_auto_history_fuzzy_validate", "");
        h.Keys("C-u");
        h.Type("gti stat");
        h.Idle();
        Assert.Equal(" [git status] ", h.E.Suggestion);
        Assert.True(h.E.AcceptSuggestion(false));
        Assert.Equal("git status|", h.State);
    }

    [Fact]
    public void SimilarEntriesMustStillRun()
    {
        var h = new Harness("nosuchcommand status");
        Options.Set("complete_auto_history_fuzzy", "1");
        Options.Set("complete_auto_history_fuzzy_validate", "command");
        h.Type("nosuchcomand stat");
        h.Idle();
        Assert.Null(h.E.Suggestion);
        Options.Set("complete_auto_history_fuzzy_validate", "");
        h.Keys("C-u");
        h.Type("nosuchcomand stat");
        h.Idle();
        Assert.Equal(" [nosuchcommand status] ", h.E.Suggestion);
    }

    [Fact]
    public void ListViewSimilarEntries()
    {
        var h = new Harness("git status", "git stash");
        Options.Set("complete_auto_view", "list");
        Options.Set("complete_auto_view_source", "history");
        Options.Set("complete_auto_history_fuzzy", "1");
        Options.Set("complete_auto_history_fuzzy_validate", "");
        h.Type("gti stauts");
        h.Idle();
        Assert.Equal(new[] { "git status" }, h.E.ListViewItems);
        Assert.Equal("Similar", h.E.ListViewEntries![0].Source);
    }
}
