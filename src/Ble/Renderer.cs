using System.Text;

namespace Ble;

/// <summary>A line drawn below the command line (status, menu, mode line).</summary>
public sealed class PanelLine
{
    public readonly List<(string Text, string Sgr)> Segments = new();
    public PanelLine Add(string text, string sgr = "") { Segments.Add((text, sgr)); return this; }
    public static PanelLine Of(string text, string face) => new PanelLine().Add(text, Faces.Sgr(face));
    public string PlainText => string.Concat(Segments.Select(s => s.Text));
}

public sealed class RenderModel
{
    public string Text = "";
    public string?[]? Faces;
    public int Point;
    public string? Suggestion;
    public string Ps2 = ">> ";
    public string? Rps1;
    public string? InputLeft;                       // drawn in front of the input
    public string? InputAfter;                      // drawn after the input and the suggestion
    public bool InputAfterFirst;                    // ... or between the input and the suggestion
    public readonly List<PanelLine> Panels = new();  // rows under the input
    public List<PanelLine> Top = new(), Bottom = new();   // rows docked to the top / bottom of the window
    public List<PanelLine>? Above;                  // rows reserved between the prompt and the input
    public int AboveGap;                            // rows of the prompt's last line under them
    public bool Final;
    public string? FinalSuffix;
}

/// <summary>
/// Draws the editing area relative to the position where the prompt ended.
/// The cursor is always moved relatively (CUU/CR/CUF) so the drawing survives scrolling.
/// Rows are broken explicitly (CR LF) instead of relying on terminal auto-wrap.
/// </summary>
public sealed class Renderer
{
    readonly ITerminal _term;

    public int StartCol;            // column where the editing area starts on its first row
    public int CurRow;              // cursor row relative to the first row
    public int Width;               // terminal width used for the last drawing
    public int Height;              // terminal height used for the last drawing (rows docked to the bottom)
    public bool Active;             // something is drawn and the cursor is inside it
    List<int> _rowWidths = new();   // used columns per row in the last drawing (row 0 includes StartCol)
    int _cursorCol;
    int _topDrawn;                  // rows drawn at the top of the window

    public Renderer(ITerminal term) { _term = term; }

    public void Begin(int startCol)
    {
        Width = _term.Width;
        Height = _term.Height;
        StartCol = Math.Clamp(startCol, 0, Math.Max(0, Width - 1));
        CurRow = 0;
        Active = false;
        _rowWidths = new List<int> { StartCol };
        _cursorCol = StartCol;
    }

    public int RowCount => _rowWidths.Count;

    /// <summary>After the terminal changed its width, estimate where the drawing ended up (hard line breaks re-wrap).</summary>
    public void AdjustForResize(int newWidth)
    {
        if (newWidth <= 0 || newWidth == Width) return;
        if (Active && newWidth < Width)
        {
            int row = 0;
            for (int r = 0; r < CurRow && r < _rowWidths.Count; r++)
                row += Math.Max(1, (_rowWidths[r] + newWidth - 1) / newWidth);
            row += _cursorCol / newWidth;
            // the origin row itself may have been split by the prompt part
            int originShift = StartCol / newWidth;
            CurRow = Math.Max(0, row - originShift);
            StartCol %= newWidth;
        }
        StartCol = Math.Min(StartCol, newWidth - 1);
        Width = newWidth;
    }

    sealed class Ctx
    {
        public readonly StringBuilder Sb = new();
        public int Row, Col, W;
        public string CurSgr = "";
        public bool JustWrapped;
        public readonly List<int> RowWidths = new();
        public string Ps2 = "";
        public string Ps2Sgr = "";
    }

    static void NewRow(Ctx c)
    {
        if (c.CurSgr.Length > 0) c.Sb.Append("\x1b[0m");
        c.RowWidths.Add(c.Col);
        c.Sb.Append("\r\n");
        c.Row++;
        c.Col = 0;
        if (c.CurSgr.Length > 0) c.Sb.Append(c.CurSgr);
    }

    static void SetSgr(Ctx c, string sgr)
    {
        if (sgr == c.CurSgr) return;
        c.Sb.Append("\x1b[0m");
        c.Sb.Append(sgr);
        c.CurSgr = sgr;
    }

    // Emits text; records the cursor at index 'cursor' (-1 for none). Returns the cursor position if recorded.
    static (int Row, int Col)? Emit(Ctx c, string text, Func<int, string> sgrAt, int cursor)
    {
        (int, int)? cur = null;
        for (int i = 0; i < text.Length; i++)
        {
            if (i == cursor) cur = (c.Row, c.Col);
            char ch = text[i];
            if (ch == '\n')
            {
                SetSgr(c, "");
                NewRow(c);
                c.JustWrapped = false;
                if (c.Ps2.Length > 0) EmitPlain(c, c.Ps2, c.Ps2Sgr);
                continue;
            }
            string sgr = sgrAt(i);
            if (ch == '\t')
            {
                int tw = Editor.TabWidth;
                int n = tw - c.Col % tw;
                for (int k = 0; k < n; k++) Put(c, " ", 1, sgr);
                continue;
            }
            int cp = ch;
            string str;
            if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(ch, text[i + 1]);
                str = text.Substring(i, 2);
                i++;
            }
            else if (cp < 0x20 || cp == 0x7F)
            {
                str = "^" + (cp == 0x7F ? '?' : (char)(cp + 64));
                Put(c, str, 2, sgr);
                continue;
            }
            else if (char.IsSurrogate(ch)) { str = "�"; cp = 0xFFFD; }
            else str = ch.ToString();
            int w = cp < 0x7F ? 1 : TextWidth.CharWidth(cp);
            if (w == 0)
            {
                SetSgr(c, sgr);
                c.Sb.Append(str);
                continue;
            }
            Put(c, str, w, sgr);
        }
        if (cursor == text.Length) cur = (c.Row, c.Col);
        return cur;
    }

    static void Put(Ctx c, string str, int w, string sgr)
    {
        if (c.Col + w > c.W && c.Col > 0) NewRow(c);
        SetSgr(c, sgr);
        c.Sb.Append(str);
        c.Col += w;
        c.JustWrapped = false;
        if (c.Col >= c.W) { NewRow(c); c.JustWrapped = true; }
    }

    static void EmitPlain(Ctx c, string text, string sgr)
    {
        Emit(c, text, _ => sgr, -1);
    }

    static readonly System.Text.RegularExpressions.Regex SgrReset = new(@"\x1b\[0*m");

    // Draws one panel row from the cursor, cut to the width. Returns the columns used.
    internal static int AppendPanel(StringBuilder sb, PanelLine p, int w)
    {
        int avail = w - 1, used = 0;
        foreach (var (text, sgr) in p.Segments)
        {
            if (avail <= 0) break;
            var t = OneLine(text);
            int tw = TextWidth.StringWidth(t);
            if (tw > avail) { t = TextWidth.Truncate(t, avail); tw = TextWidth.StringWidth(t); }
            // set the face for each part, and again after every reset inside the text (from prompt options),
            // so '\e[31mx\e[m' colors only x and the row keeps its background
            if (sgr.Length > 0 && t.Contains('\x1b')) t = SgrReset.Replace(t, "\x1b[0m" + sgr);
            sb.Append("\x1b[0m").Append(sgr).Append(t);
            used += tw;
            avail -= tw;
        }
        sb.Append("\x1b[0m");
        return used;
    }

    static string OneLine(string s) => s.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');

    static readonly List<PanelLine> NoRows = new();

    // Draws the rows docked to the top of the window and clears the rows left from the last drawing
    // (no rows: clears them). The bottom ones go with the \e[J of the drawing.
    void DrawTop(StringBuilder sb, List<PanelLine> rows, int w)
    {
        int n = Math.Max(rows.Count, _topDrawn);
        if (n == 0) return;
        sb.Append("\u001b7");
        for (int i = 0; i < n; i++)
        {
            sb.Append("\x1b[").Append(i + 1).Append(";1H\x1b[2K");
            if (i < rows.Count) AppendPanel(sb, rows[i], w);
        }
        sb.Append("\u001b8");
        _topDrawn = rows.Count;
    }

    /// <summary>Builds the escape sequence string for a model. Pure apart from reading state.</summary>
    public string Build(RenderModel m, out int cursorRow, out int cursorCol, out List<int> rowWidths)
    {
        int w = Width > 0 ? Width : 80;
        var c = new Ctx { W = w, Col = StartCol };
        var sb = c.Sb;
        sb.Append("\x1b[?25l");
        if (Active)
        {
            if (CurRow > 0) sb.Append("\x1b[").Append(CurRow).Append('A');
            sb.Append('\r');
            if (StartCol > 0) sb.Append("\x1b[").Append(StartCol).Append('C');
        }

        // rows the prompt wrapper reserved between the prompt and the input (emptied when the line is done)
        if (m.Above is { Count: > 0 } above)
        {
            sb.Append("\x1b[").Append(m.AboveGap + above.Count).Append("A\r");
            for (int i = 0; i < above.Count; i++)
            {
                if (i > 0) sb.Append("\x1b[B\r");
                sb.Append("\x1b[2K");
                if (!m.Final) AppendPanel(sb, above[i], w);
            }
            sb.Append("\x1b[").Append(m.AboveGap + 1).Append("B\r");
            if (StartCol > 0) sb.Append("\x1b[").Append(StartCol).Append('C');
        }
        sb.Append("\x1b[J");

        DrawTop(sb, m.Final ? NoRows : m.Top, w);

        // right prompt on the first row, if there is room
        if (!m.Final && !string.IsNullOrEmpty(m.Rps1))
        {
            int rw = TextWidth.StringWidth(m.Rps1);
            int nl = m.Text.IndexOf('\n');
            string first = nl >= 0 ? m.Text.Substring(0, nl) : m.Text;
            if (nl < 0 && m.Suggestion != null)
            {
                int snl = m.Suggestion.IndexOf('\n');
                first += snl >= 0 ? m.Suggestion.Substring(0, snl) : m.Suggestion;
            }
            int used = StartCol + TextWidth.StringWidth(m.InputLeft) + TextWidth.StringWidth(first);
            if (nl < 0 && m.Suggestion?.IndexOf('\n') is null or < 0) used += TextWidth.StringWidth(m.InputAfter);
            if (rw > 0 && used + 1 + rw <= w - 1 && w - rw - StartCol > 0)
            {
                sb.Append("\x1b[").Append(w - 1 - rw - StartCol).Append('C');
                sb.Append(m.Rps1).Append("\x1b[0m\r");
                if (StartCol > 0) sb.Append("\x1b[").Append(StartCol).Append('C');
            }
        }

        if (!m.Final && !string.IsNullOrEmpty(m.InputLeft))
        {
            var left = TextWidth.Truncate(OneLine(m.InputLeft), Math.Max(0, w - 1 - StartCol));
            sb.Append(left).Append("\x1b[0m");
            c.Col += TextWidth.StringWidth(left);
        }

        c.Ps2 = m.Ps2 ?? "";
        c.Ps2Sgr = Faces.Sgr("prompt_ps2");
        var faces = m.Faces;
        var sgrCache = new Dictionary<string, string>();
        string SgrAt(int i)
        {
            var f = faces != null && i < faces.Length ? faces[i] : null;
            if (f == null) return "";
            if (!sgrCache.TryGetValue(f, out var s)) { s = Ble.Faces.Sgr(f); sgrCache[f] = s; }
            return s;
        }
        var cur = Emit(c, m.Text, SgrAt, Math.Clamp(m.Point, 0, m.Text.Length));
        var cpos = cur ?? (c.Row, c.Col);

        // prompt_input_after: after the suggestion, or between the input and the suggestion; only if it fits on the row
        void EmitInputAfter()
        {
            if (m.Final || string.IsNullOrEmpty(m.InputAfter)) return;
            var after = OneLine(m.InputAfter);
            int aw = TextWidth.StringWidth(after);
            if (c.Col + aw > w - 1) return;
            SetSgr(c, "");
            sb.Append(after).Append("\x1b[0m");
            c.Col += aw;
        }
        if (m.InputAfterFirst) EmitInputAfter();
        if (!m.Final && !string.IsNullOrEmpty(m.Suggestion))
        {
            var ssgr = Faces.Sgr("auto_complete");
            Emit(c, m.Suggestion, _ => ssgr, -1);
        }
        if (!m.InputAfterFirst) EmitInputAfter();
        if (m.Final && !string.IsNullOrEmpty(m.FinalSuffix))
            Emit(c, m.FinalSuffix, _ => "", -1);
        SetSgr(c, "");

        if (!m.Final)
        {
            foreach (var p in m.Panels)
            {
                NewRow(c);
                c.Col = AppendPanel(sb, p, w);
                c.JustWrapped = false;
            }
        }
        c.RowWidths.Add(c.Col);

        // rows docked to the bottom of the window: make room under the drawing (the terminal scrolls if it has
        // to), then draw them at absolute rows. The \e[J of the next drawing erases them.
        if (!m.Final && m.Bottom.Count > 0)
        {
            int n = m.Bottom.Count, h = Math.Max(n + 1, Height);
            for (int i = 0; i < n; i++) sb.Append("\r\n");
            sb.Append("\x1b[").Append(n).Append("A\u001b7");
            for (int i = 0; i < n; i++)
            {
                sb.Append("\x1b[").Append(h - n + 1 + i).Append(";1H");
                AppendPanel(sb, m.Bottom[i], w);
            }
            sb.Append("\u001b8");
        }

        if (m.Final)
        {
            if (!c.JustWrapped) sb.Append("\r\n");
            cursorRow = 0; cursorCol = 0;
        }
        else
        {
            int up = c.Row - cpos.Item1;
            if (up > 0) sb.Append("\x1b[").Append(up).Append('A');
            sb.Append('\r');
            if (cpos.Item2 > 0) sb.Append("\x1b[").Append(cpos.Item2).Append('C');
            cursorRow = cpos.Item1; cursorCol = cpos.Item2;
        }
        sb.Append("\x1b[?25h");
        rowWidths = c.RowWidths;
        return sb.ToString();
    }

    public void Render(RenderModel m)
    {
        Height = _term.Height;
        var s = Build(m, out var row, out var col, out var widths);
        _term.Write(s);
        if (m.Final)
        {
            Active = false;
            CurRow = 0;
            _rowWidths = new List<int> { 0 };
            _cursorCol = 0;
        }
        else
        {
            Active = true;
            CurRow = row;
            _cursorCol = col;
            _rowWidths = widths;
        }
    }

    /// <summary>Erases the drawing and leaves the cursor where the editing area starts (for external programs).</summary>
    public void EraseDrawing()
    {
        if (!Active) return;
        var sb = new StringBuilder();
        DrawTop(sb, NoRows, 0);
        if (CurRow > 0) sb.Append("\x1b[").Append(CurRow).Append('A');
        sb.Append('\r');
        if (StartCol > 0) sb.Append("\x1b[").Append(StartCol).Append('C');
        sb.Append("\x1b[J\x1b[?25h");
        _term.Write(sb.ToString());
        Active = false;
        CurRow = 0;
    }

    /// <summary>Moves the cursor to the start of the drawing's first row (column 0) and erases everything below.</summary>
    public void EraseFromPromptRow(int promptRowsAbove)
    {
        var sb = new StringBuilder();
        DrawTop(sb, NoRows, 0);
        int up = (Active ? CurRow : 0) + promptRowsAbove;
        if (up > 0) sb.Append("\x1b[").Append(up).Append('A');
        sb.Append("\r\x1b[J");
        _term.Write(sb.ToString());
        Active = false;
        CurRow = 0;
    }
}
