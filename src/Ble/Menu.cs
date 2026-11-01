using System.Management.Automation;

namespace Ble;

/// <summary>Completion menu shown below the command line.</summary>
public sealed class Menu
{
    public List<CompletionItem> All;
    public List<CompletionItem> Items;
    public int Selected = -1;
    public int ReplacementIndex;
    public int ReplacementLength;
    public string Orig;           // text the replacement range refers to
    public int OrigPoint;
    public bool Selecting => Selected >= 0;
    public int Columns { get; private set; } = 1;
    public int VisibleRows { get; private set; } = 1;
    int _scroll;

    public Menu(List<CompletionItem> items, int ri, int rl, string orig, int origPoint)
    {
        All = items; Items = items;
        ReplacementIndex = ri; ReplacementLength = rl; Orig = orig; OrigPoint = origPoint;
    }

    public string Word => Orig.Substring(ReplacementIndex, Math.Min(ReplacementLength, Orig.Length - ReplacementIndex));

    static bool ColorOn(string option) => Options.Get(option) is not ("" or "off" or "0");

    static string FaceOf(CompletionResultType t) => t switch
    {
        CompletionResultType.ProviderContainer => "filename_directory",
        CompletionResultType.ProviderItem => "filename_other",
        CompletionResultType.Command => "command_builtin",
        CompletionResultType.ParameterName => "argument_option",
        CompletionResultType.Variable => "syntax_varname",
        CompletionResultType.Keyword => "command_keyword",
        CompletionResultType.Type => "syntax_type",
        CompletionResultType.Namespace => "syntax_type",
        CompletionResultType.Method or CompletionResultType.Property => "syntax_member",
        _ => "",
    };

    public List<PanelLine> Layout(int width, int maxLines)
    {
        var lines = new List<PanelLine>();
        int n = Items.Count;
        if (n == 0) return lines;
        var style = Options.Get("complete_menu_style");
        int avail = Math.Max(10, width - 1);
        var word = Word;
        string selSgr = Faces.Sgr("menu_complete_selected");
        string matchSgr = ColorOn("complete_menu_color_match") ? Faces.Sgr("menu_complete_match") : "";

        if (style is "linewise" or "desc" or "desc-text")   // ble.sh: align(-nowrap), dense(-nowrap) use the grid
        {
            Columns = 1;
            int nameW = Math.Min(Items.Max(i => TextWidth.StringWidth(i.ListItemText)), Math.Max(8, avail / 2));
            int rows = n;
            bool needInfo = rows > maxLines;
            VisibleRows = Math.Max(1, needInfo ? maxLines - 1 : rows);
            Scroll(rows);
            for (int r = _scroll; r < Math.Min(rows, _scroll + VisibleRows); r++)
            {
                var it = Items[r];
                var pl = new PanelLine();
                var name = TextWidth.Truncate(it.ListItemText, nameW);
                AddItem(pl, name, it, r == Selected, word, selSgr, matchSgr);
                if (style == "desc" && it.ToolTip.Length > 0 && it.ToolTip.Trim() != it.ListItemText)
                {
                    pl.Add(new string(' ', nameW - TextWidth.StringWidth(name) + 2));
                    var desc = it.ToolTip.Replace("\r", "").Split('\n')[0].Trim();
                    pl.Add(TextWidth.Truncate(desc, Math.Max(0, avail - nameW - 2)), Faces.Sgr("menu_desc"));
                }
                lines.Add(pl);
            }
            if (needInfo) lines.Add(InfoLine(rows));
            return lines;
        }

        int maxw = Items.Max(i => TextWidth.StringWidth(i.ListItemText));
        int cellMax = Math.Max(4, Options.GetInt("menu_align_max", 32));
        int cell = Math.Max(Math.Min(maxw, cellMax), Math.Max(1, Options.GetInt("menu_align_min", 4))) + 2;
        Columns = Math.Max(1, avail / cell);
        if (Columns == 1) cell = Math.Min(cell, avail);
        int totalRows = (n + Columns - 1) / Columns;
        bool info = totalRows > maxLines;
        VisibleRows = Math.Max(1, info ? maxLines - 1 : totalRows);
        Scroll(totalRows);
        for (int r = _scroll; r < Math.Min(totalRows, _scroll + VisibleRows); r++)
        {
            var pl = new PanelLine();
            for (int c = 0; c < Columns; c++)
            {
                int i = r * Columns + c;
                if (i >= n) break;
                var it = Items[i];
                var name = TextWidth.Truncate(it.ListItemText, cell - 2);
                AddItem(pl, name, it, i == Selected, word, selSgr, matchSgr);
                if (c < Columns - 1 && i + 1 < n) pl.Add(new string(' ', cell - TextWidth.StringWidth(name)));
            }
            lines.Add(pl);
        }
        if (info) lines.Add(InfoLine(totalRows));
        return lines;
    }

    static void AddItem(PanelLine pl, string name, CompletionItem it, bool selected, string word, string selSgr, string matchSgr)
    {
        var face = ColorOn("complete_menu_color") ? Faces.Sgr(FaceOf(it.Type)) : "";
        if (selected) { pl.Add(name, face + selSgr); return; }
        var bare = word.TrimStart('\'', '"');
        int slash = Math.Max(bare.LastIndexOf('\\'), bare.LastIndexOf('/'));
        if (slash >= 0) bare = bare.Substring(slash + 1);
        if (bare.Length > 0 && name.StartsWith(bare, StringComparison.OrdinalIgnoreCase))
        {
            pl.Add(name.Substring(0, bare.Length), face + matchSgr);
            pl.Add(name.Substring(bare.Length), face);
        }
        else pl.Add(name, face);
    }

    PanelLine InfoLine(int totalRows)
    {
        int first = _scroll + 1, last = Math.Min(totalRows, _scroll + VisibleRows);
        var sel = Selected >= 0 ? $"{Selected + 1}/{Items.Count}" : $"{Items.Count} items";
        return PanelLine.Of($"({sel}, rows {first}-{last} of {totalRows})", "menu_info");
    }

    void Scroll(int totalRows)
    {
        if (Selected >= 0)
        {
            int row = Selected / Math.Max(1, Columns);
            if (row < _scroll) _scroll = row;
            if (row >= _scroll + VisibleRows) _scroll = row - VisibleRows + 1;
        }
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, totalRows - VisibleRows));
    }

    public void Move(int delta, bool wrap = true)
    {
        int n = Items.Count;
        if (n == 0) return;
        if (Selected < 0) { Selected = delta >= 0 ? 0 : n - 1; return; }
        int s = Selected + delta;
        if (wrap) s = ((s % n) + n) % n;
        else s = Math.Clamp(s, 0, n - 1);
        Selected = s;
    }

    public void MoveRow(int rows)
    {
        int n = Items.Count;
        if (n == 0) return;
        if (Selected < 0) { Selected = 0; return; }
        int cols = Math.Max(1, Columns);
        int s = Selected + rows * cols;
        if (s >= n)
        {
            // wrap to the top of the next column
            int col = Selected % cols;
            s = rows == 1 ? (col + 1) % cols : n - 1;
            if (s >= n) s = 0;
        }
        else if (s < 0)
        {
            int col = Selected % cols;
            int lastRow = (n - 1) / cols;
            int prevCol = (col - 1 + cols) % cols;
            s = rows == -1 ? Math.Min(n - 1, lastRow * cols + prevCol) : 0;
            if (s >= n) s -= cols;
        }
        Selected = Math.Clamp(s, 0, n - 1);
    }
}
