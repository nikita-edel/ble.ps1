using Ble;
using Xunit;

namespace Ble.Tests;

// complete_auto_history_merge and complete_auto_history_fuzzy
public class HistoryMatchTests
{
    static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    static string Key(string s, bool paths = true, bool ignoreCase = true) => HistoryRanking.Key(s, paths, ignoreCase);

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
