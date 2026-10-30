namespace Ble;

// Where the mode line, the status line and the prompt_{top,above,below,bottom}_{left,center,right} rows go.
//   top / bottom   docked to the first / last rows of the window (ble.sh docks prompt_status_line at the bottom)
//   below          right under the input, above messages, menus and lists, so it doesn't move when they open
//   above          rows the prompt wrapper reserves between the prompt and the input (Session.AfterPrompt)
public sealed partial class Editor
{
    static readonly string[] Positions = ["top", "above", "below", "bottom"];
    const int Top = 0, Above = 1, Below = 2, Bottom = 3;

    // the options of each position's left/center/right row, and its face
    static readonly (string Left, string Center, string Right, string Face)[] Segments =
        Positions.Select(p => ($"prompt_{p}_left", $"prompt_{p}_center", $"prompt_{p}_right", $"prompt_{p}")).ToArray();

    /// <summary>Rows reserved above the input by the prompt wrapper, and the rows of the prompt's last line under them.</summary>
    public int AboveRows;
    public string AbovePromptLine = "";
    bool _aboveOk;

    static int Position(string option)
    {
        int i = Array.IndexOf(Positions, Options.Get(option));
        return i >= 0 ? i : Bottom;
    }

    static bool HasSegments(int pos) => IsSet(Segments[pos].Left) || IsSet(Segments[pos].Center) || IsSet(Segments[pos].Right);

    static bool IsSet(string option) => Options.GetRaw(option) is { } v && !(v is string s && s.Length == 0);

    /// <summary>Rows a position ("top", "above" ...) needs at most, known before the line is read (the prompt wrapper reserves them).</summary>
    internal static int RowsWanted(string position)
    {
        int pos = Array.IndexOf(Positions, position), n = 0;
        if (Position("prompt_mode_line_position") == pos) n++;
        if (Position("prompt_status_line_position") == pos && IsSet("prompt_status_line")) n++;
        if (HasSegments(pos)) n++;
        return n;
    }

    /// <summary>Checks after Renderer.Begin that the reserved rows are where the prompt wrapper put them.</summary>
    internal void CheckAboveRows()
    {
        _aboveOk = AboveRows > 0 && TextWidth.Measure(AbovePromptLine, Renderer.Width).Col == Renderer.StartCol;
    }

    /// <summary>Rows per position (Top, Above ...), in order: mode line, status line, left/center/right row.</summary>
    List<PanelLine>[] LayoutRows(int width)
    {
        var rows = new[] { new List<PanelLine>(), new List<PanelLine>(), new List<PanelLine>(), new List<PanelLine>() };
        var mode = ModeLine();
        if (mode != null) rows[Position("prompt_mode_line_position")].Add(PanelLine.Of(mode, "status_line"));
        var status = StatusLine(width);
        if (status != null) rows[Position("prompt_status_line_position")].Add(status);
        for (int pos = 0; pos < Positions.Length; pos++)
        {
            if (!HasSegments(pos)) continue;
            var (l, c, r, face) = Segments[pos];
            var seg = SegmentRow(Expand(l), Expand(c), Expand(r), width, Faces.Sgr(face));
            if (seg != null) rows[pos].Add(seg);
        }
        return rows;
    }

    string Expand(string option) => ExpandPrompt(Options.GetRaw(option)).Replace("\r", "").Replace('\n', ' ');

    /// <summary>
    /// One row with a left, a centered and a right part, filled to the width with the face 'sgr'.
    /// The center gives way first, then the left part.
    /// </summary>
    internal static PanelLine? SegmentRow(string left, string center, string right, int width, string sgr = "")
    {
        int avail = Math.Max(1, width - 1);
        int lw = TextWidth.StringWidth(left), cw = TextWidth.StringWidth(center), rw = TextWidth.StringWidth(right);
        if (lw + cw + rw == 0) return null;
        if (rw > avail) { right = TextWidth.Truncate(right, avail); rw = TextWidth.StringWidth(right); }
        int room = avail - rw - (rw > 0 ? 1 : 0);
        if (lw > room) { left = TextWidth.Truncate(left, room); lw = TextWidth.StringWidth(left); }
        var line = new PanelLine();
        line.Add(left, sgr);
        int col = lw;
        if (cw > 0)
        {
            int start = Math.Max((avail - cw) / 2, lw + (lw > 0 ? 1 : 0));
            if (start + cw <= avail - rw - (rw > 0 ? 1 : 0))
            {
                line.Add(new string(' ', start - col), sgr).Add(center, sgr);
                col = start + cw;
            }
        }
        line.Add(new string(' ', avail - rw - col), sgr);
        if (rw > 0) line.Add(right, sgr);
        return line;
    }
}
