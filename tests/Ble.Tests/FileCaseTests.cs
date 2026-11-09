using System.Management.Automation;
using Ble;
using Xunit;

namespace Ble.Tests;

// file names on Linux: Foo and foo are different files, like in bash without completion-ignore-case
public class FileCaseTests : IDisposable
{
    public FileCaseTests() => FileSystemPaths.CaseSensitive = true;
    public void Dispose() => FileSystemPaths.CaseSensitive = OperatingSystem.IsLinux();

    static CompletionItem File(string name) => new("./" + name, name, "/tmp/x/" + name, CompletionResultType.ProviderItem);

    // what PowerShell answers on Linux: file names match ignoring case
    static Harness With(params string[] names)
    {
        var h = new Harness();
        h.S.Completer = (text, cursor) =>
        {
            int s = cursor;
            while (s > 0 && !char.IsWhiteSpace(text[s - 1])) s--;
            var pattern = new WildcardPattern(text.Substring(s, cursor - s) + "*", WildcardOptions.IgnoreCase);
            return new CompletionSet(s, cursor - s, names.Where(pattern.IsMatch).Select(File).ToList());
        };
        return h;
    }

    static readonly string[] Files = ["Foo.txt", "foo2.txt", "FOOBAR.log"];

    [Theory]
    [InlineData("ls fo", "ls ./foo2.txt |")]
    [InlineData("ls Fo", "ls ./Foo.txt |")]
    [InlineData("ls FO", "ls ./FOOBAR.log |")]
    public void TabOnlyOffersFilesInTheCaseTyped(string typed, string expected)
    {
        var h = With(Files);
        h.Type(typed);
        h.Keys("TAB");
        Assert.Equal(expected, h.State);
    }

    [Fact]
    public void WrongCaseFindsNothing()
    {
        var h = With("Foo.txt");
        h.Type("ls fOO");
        h.Keys("TAB");
        Assert.Equal("ls fOO|", h.State);
        Assert.Null(h.E.Menu);
    }

    [Fact]
    public void WildcardsKeepCase()
    {
        var h = With(Files);
        h.Type("ls F*");
        h.Keys("TAB");
        Assert.Equal(["Foo.txt", "FOOBAR.log"], h.E.Menu!.Items.Select(i => i.ListItemText));
    }

    [Fact]
    public void AmbiguousMatchesKeepCase()
    {
        var h = With(Files);
        h.Type("ls oo");
        h.Keys("TAB");
        Assert.Equal(["Foo.txt", "foo2.txt"], h.E.Menu!.Items.Select(i => i.ListItemText));
    }

    [Fact]
    public void CommonPrefixStopsAtADifferentCase()
    {
        var h = With("fooA", "fooa");
        h.Type("ls f");
        h.Keys("TAB");
        Assert.Equal("ls ./foo|", h.State);
    }

    [Fact]
    public void MenuFilterKeepsCase()
    {
        var h = With("ab1", "aB2");
        h.Type("ls a");
        h.Keys("TAB");
        Assert.Equal(2, h.E.Menu!.Items.Count);
        h.Type("B");
        Assert.Equal(["aB2"], h.E.Menu!.Items.Select(i => i.ListItemText));
    }

    [Fact]
    public void OtherCompletionsStillIgnoreCase()
    {
        var h = new Harness();
        h.S.CompleteFrom("Get-ChildItem");
        h.Type("get-chi");
        h.Keys("TAB");
        Assert.Equal("Get-ChildItem |", h.State);

        var env = new CompletionItem("Env:/BleVar", "BleVar", "Env:/BleVar", CompletionResultType.ProviderItem);
        var set = new CompletionSet(3, 7, [env]);
        Assert.Same(set, FileSystemPaths.KeepCase(set, "ls env:ble", 10));
    }

    [Theory]
    [InlineData("ls 'fo", 3, 3)]        // quoted
    [InlineData("ls sub/fo", 3, 6)]     // only the last part is the name
    [InlineData(@"ls sub\fo", 3, 6)]
    [InlineData("ls fo", 3, 2)]
    public void OnlyTheFileNameIsCompared(string text, int index, int length)
    {
        var set = new CompletionSet(index, length, [File("Foo"), File("foo")]);
        Assert.Equal(["foo"], FileSystemPaths.KeepCase(set, text, text.Length)!.Items.Select(i => i.ListItemText));
    }

    [Fact]
    public void TextAfterTheCursorDoesNotCount()
    {
        var set = new CompletionSet(3, 5, [File("Foo"), File("foobar")]);
        Assert.Equal(["foobar"], FileSystemPaths.KeepCase(set, "ls fobar", 5)!.Items.Select(i => i.ListItemText));
    }

    [Fact]
    public void ItemsThatDoNotMatchTheWordAreLeftAlone()
    {
        // "~" completes to the home folder, whose name has nothing to do with the word
        var set = new CompletionSet(3, 1, [new CompletionItem("/home/me", "me", "/home/me", CompletionResultType.ProviderContainer)]);
        Assert.Same(set, FileSystemPaths.KeepCase(set, "ls ~", 4));
    }

    [Fact]
    public void HomeFolderKeepsCase()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal("~/x", Editor.TildePath(home + "/x"));
        Assert.Equal(home.ToUpperInvariant() + "/x", Editor.TildePath(home.ToUpperInvariant() + "/x"));
    }

    [Fact]
    public void HistoryMergeKeepsTheCaseOfPaths()
    {
        static string K(string s) => HistoryRanking.Key(s, paths: true, ignoreCase: true);
        Assert.NotEqual(K("cat ./Foo"), K("cat ./foo"));
        Assert.Equal(K("CAT ./foo"), K("cat ./foo"));
        Assert.Equal(K("git STATUS"), K("git status"));
    }

    [Fact]
    public void WithoutCaseSensitiveFilesEverythingIsOffered()
    {
        FileSystemPaths.CaseSensitive = false;
        var h = With(Files);
        h.Type("ls fo");
        h.Keys("TAB");
        Assert.Equal(3, h.E.Menu!.Items.Count);
        Assert.Equal(HistoryRanking.Key("cat ./Foo", true, true), HistoryRanking.Key("cat ./foo", true, true));
    }
}
