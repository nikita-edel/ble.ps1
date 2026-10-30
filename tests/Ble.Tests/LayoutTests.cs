using Ble;
using Xunit;

namespace Ble.Tests;

// mode line, status line and prompt_{top,above,below,bottom}_* rows, drawn on a 40x10 screen
public class LayoutTests
{
    sealed class Screen
    {
        public readonly Harness H;
        public readonly VtScreen S;

        public Screen(Harness h, string prompt = "PS> ", int aboveRows = 0)
        {
            H = h;
            h.T.Width = 40;
            h.T.Height = 10;
            S = new VtScreen(40, 10);
            S.Feed(prompt.Replace("\n", "\r\n"));
            h.E.AboveRows = aboveRows;
            h.E.AbovePromptLine = prompt.Substring(prompt.LastIndexOf('\n') + 1);
            h.E.Renderer.Begin(S.Col);
            if (aboveRows > 0) h.E.CheckAboveRows();
            Draw();
        }

        public void Draw(bool final = false)
        {
            H.T.Output.Clear();
            if (final)
            {
                var m = H.E.BuildModel();
                m.Final = true;
                H.E.Renderer.Render(m);
            }
            else H.E.SafeRender();
            S.Feed(H.T.Output.ToString());
        }
    }

    [Fact]
    public void ModeLineIsAtTheBottomAndStaysWhenTheMenuOpens()
    {
        var h = new Harness().Vi();
        h.S.CompleteFrom("alpha", "alpine", "alps");
        var s = new Screen(h);
        Assert.Equal("PS>", s.S.Line(0));
        Assert.Equal("-- INSERT --", s.S.Line(9));
        Assert.Equal((0, 4), (s.S.Row, s.S.Col));
        h.Type("al");
        h.Keys("TAB");
        s.Draw();
        Assert.Contains("alpine", s.S.Line(1));
        Assert.Equal("-- INSERT --", s.S.Line(9));
        Assert.Equal((0, 7), (s.S.Row, s.S.Col));
        // the line is done: the bottom row goes away
        s.Draw(final: true);
        Assert.Equal("", s.S.Line(9));
    }

    [Fact]
    public void BottomRowsMakeRoomWhenTheInputIsAtTheLastRow()
    {
        var h = new Harness().Vi();
        h.T.Width = 40; h.T.Height = 10;
        var scr = new VtScreen(40, 10);
        scr.Feed("\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\n\r\nPS> ");
        h.E.Renderer.Begin(4);
        h.T.Output.Clear();
        h.E.SafeRender();
        scr.Feed(h.T.Output.ToString());
        Assert.Equal(1, scr.Scrolled);
        Assert.Equal("PS>", scr.Line(8));
        Assert.Equal("-- INSERT --", scr.Line(9));
        Assert.Equal((8, 4), (scr.Row, scr.Col));
    }

    [Fact]
    public void BelowPutsTheModeLineBetweenInputAndMenu()
    {
        var h = new Harness().Vi();
        Options.Set("prompt_mode_line_position", "below");
        h.S.CompleteFrom("alpha", "alpine", "alps");
        h.Type("al");
        h.Keys("TAB");
        var s = new Screen(h);
        Assert.Equal("PS> alp", s.S.Line(0));
        Assert.Equal("-- INSERT --", s.S.Line(1));
        Assert.Contains("alpine", s.S.Line(2));
    }

    [Fact]
    public void TopRowsAndStatusLine()
    {
        var h = new Harness();
        Options.Set("prompt_status_line", "status");
        Options.Set("prompt_status_line_position", "top");
        Options.Set("prompt_top_right", "R");
        var s = new Screen(h, "\r\n\r\nPS> ");
        Assert.Equal("status", s.S.Line(0));
        Assert.Equal("R".PadLeft(39), s.S.Line(1));
        Assert.Equal((2, 4), (s.S.Row, s.S.Col));
        s.Draw(final: true);
        Assert.Equal("", s.S.Line(0));
        Assert.Equal("", s.S.Line(1));
    }

    [Fact]
    public void SegmentRowsHaveLeftCenterAndRight()
    {
        static string Row(string l, string c, string r, int w) => TextWidth.StripAnsi(Editor.SegmentRow(l, c, r, w)!.PlainText);
        Assert.Equal("L        C         R", Row("L", "C", "R", 21));
        // the center gives way to the sides
        Assert.Equal("left_part    right", Row("left_part", "center", "right", 19));
        Assert.Null(Editor.SegmentRow("", "", "", 40));
    }

    [Fact]
    public void StatusLineJustify()
    {
        var h = new Harness();
        Options.Set("prompt_status_line", "a\rb\rc");
        Options.Set("prompt_status_align", "justify");
        h.T.Width = 12;
        h.E.Renderer.Begin(0);
        Assert.Equal("a    b    c", h.E.BuildModel().Bottom[0].PlainText);
        Options.Set("prompt_status_line", "a|b");
        Options.Set("prompt_status_align", "justify=|");
        Assert.Equal("a         b", h.E.BuildModel().Bottom[0].PlainText);
    }

    [Fact]
    public void InputLeftComesBeforeTheInput()
    {
        var h = new Harness();
        Options.Set("prompt_input_left", "[x] ");
        h.Type("ab");
        var s = new Screen(h);
        Assert.Equal("PS> [x] ab", s.S.Line(0));
        Assert.Equal((0, 10), (s.S.Row, s.S.Col));
        s.Draw(final: true);
        Assert.Equal("PS> ab", s.S.Line(0));
    }

    [Fact]
    public void InputAfterFollowsTheSuggestion()
    {
        var h = new Harness("echo hello");
        Options.Set("prompt_input_after", " <x>");
        h.Type("echo h");
        var s = new Screen(h);
        Assert.Equal("PS> echo hello <x>", s.S.Line(0));
        Assert.Equal((0, 10), (s.S.Row, s.S.Col));
        s.Draw(final: true);
        Assert.Equal("PS> echo h", s.S.Line(0));

        // before: between the input and the suggestion
        h = new Harness("echo hello");
        Options.Set("prompt_input_after", " <x>");
        Options.Set("prompt_input_after_position", "before");
        h.Type("echo h");
        s = new Screen(h);
        Assert.Equal("PS> echo h <x>ello", s.S.Line(0));
        Assert.Equal((0, 10), (s.S.Row, s.S.Col));
    }

    [Fact]
    public void RowFaceFillsTheRowAndSurvivesResets()
    {
        var h = new Harness();
        Faces.Apply("prompt_bottom=bg=240");
        Options.Set("prompt_bottom_left", "\x1b[31mA\x1b[m B");
        var row = h.E.BuildModel().Bottom[0];
        Assert.Equal(79, TextWidth.StringWidth(row.PlainText));   // filled to the width
        var sgr = Faces.Sgr("prompt_bottom");
        Assert.NotEqual("", sgr);
        var sb = new System.Text.StringBuilder();
        Renderer.AppendPanel(sb, row, 80);
        Assert.Contains("A\x1b[0m" + sgr + " B", sb.ToString());
    }

    [Fact]
    public void RightPromptFollowsTheMode()
    {
        var h = new Harness().Vi();
        Options.Set("prompt_rps1", @"\q{keymap:vi/mode-name}");
        h.E.StartLine();   // the right prompt is read when a line starts
        Assert.Equal("insert", h.E.BuildModel().Rps1);
        h.Keys("ESC");
        Assert.Equal("normal", h.E.BuildModel().Rps1);
    }

    [Fact]
    public void AboveRowsAreDrawnBetweenPromptAndInput()
    {
        var h = new Harness().Vi();
        Options.Set("prompt_mode_line_position", "above");
        Options.Set("prompt_above_right", "R");
        Assert.Equal(2, Editor.RowsWanted("above"));
        // the prompt wrapper inserted two empty rows before the prompt's last line
        var s = new Screen(h, "branch main\n\n\n$ ", aboveRows: 2);
        Assert.Equal("branch main", s.S.Line(0));
        Assert.Equal("-- INSERT --", s.S.Line(1));
        Assert.Equal("R".PadLeft(39), s.S.Line(2));
        Assert.Equal("$", s.S.Line(3));
        Assert.Equal((3, 2), (s.S.Row, s.S.Col));
        Assert.Equal("", s.S.Line(9));
        s.Draw(final: true);
        Assert.Equal("", s.S.Line(1));
        Assert.Equal("", s.S.Line(2));
    }

    [Fact]
    public void PromptWrapperReservesAboveRows()
    {
        Harness.ResetGlobals();
        Session.ResetForTests();
        Assert.Equal("a\n$ ", Session.AfterPrompt("a\n$ "));
        Options.Set("prompt_above_left", "x");
        Assert.Equal("a\n\n$ ", Session.AfterPrompt("a\n$ "));
        Assert.Equal("\nPS> ", Session.AfterPrompt("PS> "));
        Session.ResetForTests();
    }
}
