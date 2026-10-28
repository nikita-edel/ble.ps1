using Ble;
using Xunit;
using Xunit.Abstractions;

namespace Ble.Tests;

/// <summary>Random key sequences must never make a widget throw or leave the editor in an invalid state.</summary>
public class FuzzTests
{
    readonly ITestOutputHelper _out;
    public FuzzTests(ITestOutputHelper output) { _out = output; }

    static readonly string[] Pool =
    [
        "a", "b", "x", "1", "0", "3", "SP", "(", ")", "\"", "'", "$", "-", "|", "{", "}", "w", "e", "d", "c", "y", "p", "P",
        "u", ".", "i", "A", "o", "O", "v", "V", "f", "t", ";", ",", "%", "r", "R", "~", "J", ">", "<", "g", "G", "D", "C",
        "x", "X", "s", "S", "h", "l", "j", "k", "b", "W", "B", "E", "^", "$", "n", "N", "/", "\"", "a",
        "RET", "S-RET", "TAB", "S-TAB", "ESC", "DEL", "delete", "C-a", "C-e", "C-f", "C-b", "C-k", "C-u", "C-w", "C-y", "M-y",
        "C-t", "M-t", "M-f", "M-b", "M-d", "M-DEL", "M-u", "M-l", "M-c", "C-_", "C-x", "C-r", "C-s", "C-g", "C-@", "M-.",
        "up", "down", "left", "right", "home", "end", "C-left", "C-right", "S-left", "S-right", "S-home", "S-end",
        "M-p", "M-n", "M-<", "M->", "prior", "next", "insert", "C-]", "M-3", "M--", "C-q", "M-?", "M-*", "C-a", "C-x",
        "C-v", "q", "@", "m", "`", "'", ":", "/", "C-g", "S-up", "S-down", "O", "I", "A", "c", "%", "s", "S-RET", "C-j",
    ];

    [Theory]
    [InlineData("emacs", 1)]
    [InlineData("emacs", 2)]
    [InlineData("vi", 3)]
    [InlineData("vi", 4)]
    [InlineData("vi", 5)]
    public void RandomKeys(string keymap, int seed)
    {
        var rng = new Random(seed);
        for (int round = 0; round < 60; round++)
        {
            var h = new Harness("echo hello", "Get-ChildItem -Path C:\\ | Where-Object { $_.Name -like 'a*' }", "if (1) {\n  2\n}");
            if (keymap == "vi") h.Vi();
            // edit-and-execute-command launches an external editor
            Keymaps.Get("emacs").Bind(["C-x", "C-e"], null);
            Keymaps.Get("vi_imap").Bind(["C-x", "C-e"], null);
            h.S.CompleteFrom("alpha", "alpine", "beta", "Get-ChildItem", @".\dir\");
            var log = new List<string>();
            for (int i = 0; i < 150 && !h.E.Done && h.E.Text.Length < 2000; i++)
            {
                var k = Pool[rng.Next(Pool.Length)];
                log.Add(k);
                try
                {
                    var task = Task.Run(() =>
                    {
                        h.E.ProcessKey(KeyInput.FromName(k));
                        h.E.UpdateSuggestion();
                        _ = h.E.BuildModel();
                    });
                    if (!task.Wait(3000)) Assert.Fail($"hang after keys: {string.Join(' ', log)}");
                    task.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Assert.Fail($"exception {ex.GetType().Name}: {ex.Message} after keys: {string.Join(' ', log)}\n{ex.StackTrace}");
                }
                if (h.E.Info != null && h.E.Info.StartsWith("ble: ", StringComparison.Ordinal))
                    Assert.Fail($"widget error '{h.E.Info}' after keys: {string.Join(' ', log)}");
                Assert.InRange(h.E.Point, 0, h.E.Text.Length);
                if (h.E.Mark >= 0 && h.E.MarkActive) Assert.InRange(h.E.Mark, 0, Math.Max(0, h.E.Text.Length));
            }
        }
    }

    [Fact]
    public void RandomRenderingAtNarrowWidths()
    {
        var rng = new Random(7);
        foreach (var width in new[] { 5, 10, 13, 40 })
        {
            var h = new Harness("echo 日本語 hello", "x");
            Keymaps.Get("emacs").Bind(["C-x", "C-e"], null);
            h.T.Width = width;
            h.E.Renderer.Begin(Math.Min(3, width - 1));
            var scr = new VtScreen(width, 400);
            scr.Feed(new string('>', Math.Min(3, width - 1)));
            for (int i = 0; i < 300; i++)
            {
                var k = Pool[rng.Next(Pool.Length)];
                h.E.ProcessKey(KeyInput.FromName(k));
                if (h.E.Done || h.E.Text.Length > 2000) break;
                h.E.UpdateSuggestion();
                h.T.Output.Clear();
                h.E.SafeRender();
                scr.Feed(h.T.Output.ToString());
                Assert.Null(h.E.Info is { } info && info.StartsWith("ble: render", StringComparison.Ordinal) ? info : null);
                Assert.InRange(scr.Col, 0, width - 1);
            }
        }
    }
}
