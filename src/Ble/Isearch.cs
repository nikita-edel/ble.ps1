namespace Ble;

/// <summary>Incremental history search (C-r, C-s).</summary>
public sealed partial class Editor
{
    sealed class IsearchState
    {
        public string Query = "";
        public bool Backward;
        public int Index;     // history index (History.Count = the line being edited)
        public int Pos;       // match position
        public bool Failed;
        public string OrigText = "";
        public int OrigPoint;
        public int OrigIndex;
        public readonly Stack<(string Query, int Index, int Pos, bool Failed, bool Backward)> Stack = new();
    }

    void StartIsearch(bool backward)
    {
        var st = new IsearchState { Backward = backward, Index = HistIndex, Pos = Point, OrigText = Text, OrigPoint = Point, OrigIndex = HistIndex };
        UpdateIsearchInfo(st);
        Modal = key => IsearchKey(st, key);
    }

    void UpdateIsearchInfo(IsearchState st)
    {
        var dir = st.Backward ? "reverse-i-search" : "i-search";
        Info = (st.Failed ? "(failed " + dir + ")" : "(" + dir + ")") + "`" + st.Query + "'";
        InfoFace = st.Failed ? "status_error" : "status_info";
        MatchRegion = st.Query.Length > 0 && !st.Failed ? (st.Pos, st.Pos + st.Query.Length) : null;
    }

    bool IsearchFind(IsearchState st, int fromIndex, int fromPos, bool backward, bool includeCurrent)
    {
        var q = st.Query;
        if (q.Length == 0) return true;
        var cmp = History.SmartCase(q);
        int idx = fromIndex;
        int pos = fromPos;
        bool first = true;
        while (idx >= 0 && idx <= History.Count)
        {
            var line = idx == HistIndex ? Text : (idx < History.Count ? History[idx] : _histEdits.GetValueOrDefault(History.Count, ""));
            int found;
            if (line.Length < q.Length) found = -1;
            else if (backward)
            {
                int start = first ? (includeCurrent ? Math.Min(pos, line.Length - q.Length) : pos - 1) : line.Length - q.Length;
                found = start < 0 ? -1 : line.LastIndexOf(q, Math.Min(line.Length - 1, start + q.Length - 1), cmp);
                if (found > start) found = -1;
            }
            else
            {
                int start = first ? (includeCurrent ? pos : pos + 1) : 0;
                found = start > line.Length ? -1 : line.IndexOf(q, Math.Min(start, line.Length), cmp);
            }
            if (found >= 0)
            {
                if (idx != HistIndex) HistoryGoto(idx);
                st.Index = idx; st.Pos = found;
                Point = found;
                return true;
            }
            first = false;
            idx += backward ? -1 : 1;
        }
        return false;
    }

    bool IsearchKey(IsearchState st, KeyInput key)
    {
        void Finish() { Modal = null; Info = null; MatchRegion = null; }
        switch (key.Name)
        {
            case "C-r":
            case "C-s":
                {
                    bool back = key.Name == "C-r";
                    st.Stack.Push((st.Query, st.Index, st.Pos, st.Failed, st.Backward));
                    st.Backward = back;
                    if (st.Query.Length == 0 && _lastIsearch.Length > 0) st.Query = _lastIsearch;
                    st.Failed = !IsearchFind(st, st.Index, st.Pos, back, includeCurrent: false);
                    UpdateIsearchInfo(st);
                    return true;
                }
            case "DEL":
            case "C-h":
                if (st.Stack.Count == 0) { Bell(); return true; }
                {
                    var (q, idx, pos, failed, back) = st.Stack.Pop();
                    st.Query = q; st.Backward = back; st.Failed = failed;
                    if (idx != HistIndex) HistoryGoto(idx);
                    st.Index = idx; st.Pos = pos; Point = Math.Min(pos, Text.Length);
                    UpdateIsearchInfo(st);
                    return true;
                }
            case "C-g":
                Finish();
                if (HistIndex != st.OrigIndex) HistoryGoto(st.OrigIndex);
                Text = st.OrigText; Point = st.OrigPoint;
                return true;
            case "RET":
                _lastIsearch = st.Query;
                Finish();
                AcceptLine();
                return true;
            case "C-j":
            case "ESC":
                _lastIsearch = st.Query;
                Finish();
                return true;
        }
        if (key.IsPrintable)
        {
            st.Stack.Push((st.Query, st.Index, st.Pos, st.Failed, st.Backward));
            st.Query += key.Char;
            if (!st.Failed) st.Failed = !IsearchFind(st, st.Index, st.Pos, st.Backward, includeCurrent: true);
            UpdateIsearchInfo(st);
            return true;
        }
        // any other key: leave isearch and process the key normally
        _lastIsearch = st.Query;
        Finish();
        return false;
    }

    string _lastIsearch = "";
}
