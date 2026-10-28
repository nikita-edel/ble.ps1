using Ble;

namespace Ble.Tests;

/// <summary>Drives an Editor directly with keys, without the terminal loop.</summary>
public sealed class Harness
{
    public readonly FakeTerminal T = new();
    public readonly FakeShell S = new();
    public readonly History H = new();
    public readonly Editor E;

    public Harness(params string[] history)
    {
        ResetGlobals();
        H.AddRange(history);
        E = new Editor(T, S, H);
        E.StartLine();
        E.Renderer.Begin(4);
    }

    public static void ResetGlobals()
    {
        Options.Reset();
        Faces.Reset();
        Keymaps.Reset();
        Editor.KillRing.Clear();
        Editor.Abbrevs.Clear();
        Editor.Registers.Clear();
        Options.Set("edit_paste_newline", "0");
        Options.Set("complete_auto_completion", "0");
    }

    public string Text => E.Text;
    public int Point => E.Point;

    public Harness Keys(params string[] names)
    {
        foreach (var n in names)
        {
            E.ProcessKey(KeyInput.FromName(n));
            E.UpdateSuggestion();
        }
        return this;
    }

    public Harness Type(string s)
    {
        foreach (var c in s)
        {
            var k = c == ' ' ? new KeyInput("SP", ' ') : c == '\n' ? new KeyInput("RET", '\0') : KeyInput.FromName(c.ToString());
            E.ProcessKey(k);
            E.UpdateSuggestion();
        }
        return this;
    }

    /// <summary>Text with a '|' at the cursor position.</summary>
    public string State => E.Text.Insert(E.Point, "|");

    public Harness Vi()
    {
        Options.Set("default_keymap", "vi");
        E.StartLine();
        return this;
    }
}
