using Ble;
using Xunit;

namespace Ble.Tests;

public class KeyTests
{
    [Theory]
    [InlineData("a")] [InlineData("A")] [InlineData("1")] [InlineData("SP")] [InlineData("RET")] [InlineData("TAB")]
    [InlineData("ESC")] [InlineData("DEL")] [InlineData("delete")] [InlineData("up")] [InlineData("C-left")] [InlineData("S-TAB")]
    [InlineData("C-a")] [InlineData("C-x")] [InlineData("M-f")] [InlineData("M-F")] [InlineData("C-M-j")] [InlineData("f5")]
    [InlineData("S-RET")] [InlineData("C-RET")] [InlineData("C-S-left")] [InlineData("C-@")] [InlineData("C-_")] [InlineData("C-]")]
    [InlineData("M-DEL")] [InlineData("home")] [InlineData("prior")] [InlineData("M-.")] [InlineData("C-/")]
    public void ConsoleKeyRoundTrip(string name)
    {
        var k = KeyNames.FromConsoleKey(KeyFactory.ToConsoleKey(name));
        Assert.NotNull(k);
        Assert.Equal(name, k!.Value.Name);
    }

    [Theory]
    [InlineData("C-x C-e", "C-x C-e")]
    [InlineData("M-DEL", "M-DEL")]
    [InlineData("Ctrl+a", "C-a")]
    [InlineData("Ctrl+Shift+Left", "C-S-left")]
    [InlineData("Alt+f", "M-f")]
    [InlineData("C-m", "RET")]
    [InlineData("C-i", "TAB")]
    [InlineData("C-[", "ESC")]
    [InlineData("C-SP", "C-@")]
    [InlineData("BS", "C-h")]
    [InlineData("M-C-h", "C-M-h")]
    [InlineData("C-A", "C-a")]
    [InlineData("S-a", "A")]
    [InlineData("M-S-f", "M-F")]
    [InlineData("enter", "RET")]
    [InlineData("pgup", "prior")]
    [InlineData("F12", "f12")]
    [InlineData("space", "SP")]
    public void ParseSpec(string spec, string expected)
    {
        Assert.Equal(expected, string.Join(' ', KeyNames.ParseSpec(spec)));
    }

    [Fact]
    public void ParseSpecRejectsUnknown()
    {
        Assert.Throws<FormatException>(() => KeyNames.ParseSpec("C-nosuchkey"));
        Assert.Throws<FormatException>(() => KeyNames.ParseSpec("   "));
    }

    [Fact]
    public void AltGrProducesCharacter()
    {
        // German keyboard: AltGr+Q = '@' arrives as Ctrl+Alt+Q with KeyChar '@'
        var k = KeyNames.FromConsoleKey(new ConsoleKeyInfo('@', ConsoleKey.Q, false, true, true));
        Assert.Equal("@", k!.Value.Name);
        Assert.True(k.Value.IsPrintable);
    }

    [Fact]
    public void CtrlBackspaceIsCDel()
    {
        var k = KeyNames.FromConsoleKey(new ConsoleKeyInfo('\x7f', ConsoleKey.Backspace, false, false, true));
        Assert.Equal("C-DEL", k!.Value.Name);
    }

    [Fact]
    public void LineFeedIsCtrlJ()
    {
        var k = KeyNames.FromConsoleKey(new ConsoleKeyInfo('\n', ConsoleKey.Enter, false, false, false));
        Assert.Equal("C-j", k!.Value.Name);
    }

    [Fact]
    public void KeymapPrefixes()
    {
        var km = new Keymap("t");
        km.Bind("C-x C-e", "undo");
        Assert.True(km.IsPrefix("C-x"));
        Assert.True(km.TryGet("C-x C-e", out _));
        km.Bind(KeyNames.ParseSpec("C-x C-e"), null);
        Assert.False(km.IsPrefix("C-x"));
    }

    // what .NET reports on Linux/macOS for the bytes a terminal sends
    [Theory]
    [InlineData('\0', ConsoleKey.D7, "C-_")]
    [InlineData('\0', ConsoleKey.D5, "C-]")]
    [InlineData('\0', ConsoleKey.D4, "C-\\")]
    [InlineData('\0', ConsoleKey.D6, "C-^")]
    [InlineData('\0', ConsoleKey.D2, "C-@")]
    [InlineData('\x1f', ConsoleKey.D7, "C-_")]
    [InlineData('\n', ConsoleKey.Enter, "C-j")]
    public void UnixControlKeys(char ch, ConsoleKey key, string name)
    {
        if (OperatingSystem.IsWindows()) return;
        var k = KeyNames.FromConsoleKey(new ConsoleKeyInfo(ch, key, false, false, true));
        Assert.Equal(name, k!.Value.Name);
    }

    [Fact]
    public void UnixAltBackspaceWithControlIsCMh()
    {
        if (OperatingSystem.IsWindows()) return;
        var k = KeyNames.FromConsoleKey(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, true, true));
        Assert.Equal("C-M-h", k!.Value.Name);
    }

    [Theory]
    [InlineData("13;2u", "S-RET")]
    [InlineData("13;5u", "C-RET")]
    [InlineData("27;2;13~", "S-RET")]
    [InlineData("9;2u", "S-TAB")]
    [InlineData("97;5u", "C-a")]
    [InlineData("97u", "a")]
    [InlineData("97;2u", "A")]
    [InlineData("32;5u", "C-@")]
    public void CsiKeys(string seq, string name) => Assert.Equal(name, KeyNames.CsiKey(seq)!.Value.Name);

    [Theory]
    [InlineData("200~")] [InlineData("I")] [InlineData("999999999u")] [InlineData("55296u")]
    public void CsiKeysThatAreNotKeys(string seq) => Assert.Null(KeyNames.CsiKey(seq));

    static string ReadLine(Harness h)
    {
        h.E.Renderer.Begin(0);
        h.E.ReadLine();
        return h.E.History.Count > 0 ? h.E.History[h.E.History.Count - 1] : "";
    }

    [Theory]
    [InlineData("\x1b[200~")]
    [InlineData("\x1b[99;9;9~")]
    public void EscapeSequencesDontReachViAsCommands(string seq)
    {
        var h = new Harness().Vi();
        h.T.Type("abc");
        h.T.Pause();
        h.T.Type(seq);
        h.T.Pause();
        h.T.Type("d\r");
        Assert.Equal("abcd", ReadLine(h));
    }

    [Fact]
    public void SRetFromTheTerminalIsSRet()
    {
        var h = new Harness("git status").Vi();
        Options.Set("complete_auto_view", "list");
        h.T.Type("git s");
        h.T.Pause();
        h.T.Type("\x1b[13;2u\r");
        Assert.Equal("git status", ReadLine(h));
    }

    [Fact]
    public void EscapeAloneAndEscapeThenKeysStillWork()
    {
        var h = new Harness().Vi();
        h.T.Type("abc");
        h.T.Pause();
        h.T.Keys("ESC");
        h.T.Pause();
        h.T.Type("x\r");
        Assert.Equal("ab", ReadLine(h));

        h = new Harness().Vi();
        h.T.Type("abc");
        h.T.Pause();
        h.T.Type("\x1bx\r");   // a burst that is no sequence
        Assert.Equal("ab", ReadLine(h));
    }
}

public class LinuxPathTests
{
    [Fact]
    public void ExecutablesGoByFileMode()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("ble-exe-").FullName;
        try
        {
            string F(string name, UnixFileMode mode)
            {
                var p = Path.Combine(dir, name);
                File.WriteAllText(p, "");
                File.SetUnixFileMode(p, mode);
                return name;
            }
            const UnixFileMode rw = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            Assert.Equal(PathKind.Executable, FileSystemPaths.Resolve(F("run", rw | UnixFileMode.UserExecute), dir));
            Assert.Equal(PathKind.File, FileSystemPaths.Resolve(F("data.sh", rw), dir));
            Assert.Equal(PathKind.File, FileSystemPaths.Resolve(F("note.txt", rw), dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ProgramNamesAreCaseSensitive()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("ble-path-").FullName;
        var path = Environment.GetEnvironmentVariable("PATH");
        try
        {
            File.WriteAllText(Path.Combine(dir, "bletool"), "");
            Environment.SetEnvironmentVariable("PATH", dir);
            var index = new CommandIndex();
            Assert.Equal(CommandKind.Application, index.Path("bletool"));
            Assert.Equal(CommandKind.NotFound, index.Path("BLETOOL"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", path);
            Directory.Delete(dir, true);
        }
    }
}
