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
}
