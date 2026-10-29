using System.Text;
using System.Text.RegularExpressions;

namespace Ble;

/// <summary>
/// Command history. Entries are kept unique (most recent occurrence wins), oldest first.
/// The on-disk format is PSReadLine's: one entry per line, embedded newlines written as "`\n".
/// The directories each entry ran in (ble.ps1, for complete_auto_history_cwd) don't fit in that
/// format; they go to "&lt;history file&gt;.dirs", one "directory TAB entry" record per line, the
/// entry escaped the same way.
/// </summary>
public sealed class History
{
    const char Sep = '\u0001';
    const int MaxDirsPerEntry = 16;

    readonly List<string> _items = new();
    string? _blob;          // Sep + newest + Sep + ... + oldest + Sep
    int[]? _offsets;        // start offset in _blob of k-th newest entry
    long _knownSize;
    readonly Dictionary<string, List<string>> _dirs = new(StringComparer.Ordinal);   // entry -> directories, most recent last
    long _knownDirsSize;

    static readonly StringComparer DirComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public string? FilePath { get; private set; }
    public string? DirsFilePath => FilePath == null ? null : FilePath + ".dirs";
    public int MaxSize { get; set; } = 10000;
    public IReadOnlyList<string> Items => _items;
    public int Count => _items.Count;
    public string this[int i] => _items[i];

    public void Clear()
    {
        _items.Clear();
        _dirs.Clear();
        Invalidate();
    }

    void Invalidate() { _blob = null; _offsets = null; }

    /// <summary>Parses PSReadLine history text into entries.</summary>
    public static List<string> Parse(string content)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool continuing = false;
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.EndsWith('\r') ? raw.Substring(0, raw.Length - 1) : raw;
            if (line.EndsWith('`'))
            {
                sb.Append(line, 0, line.Length - 1).Append('\n');
                continuing = true;
                continue;
            }
            sb.Append(line);
            var entry = sb.ToString();
            sb.Clear();
            continuing = false;
            if (entry.Trim().Length > 0) result.Add(entry);
        }
        if (continuing && sb.Length > 0)
        {
            var entry = sb.ToString().TrimEnd('\n');
            if (entry.Trim().Length > 0) result.Add(entry);
        }
        return result;
    }

    public static string Serialize(string entry) => entry.Replace("\r\n", "\n").Replace("\n", "`\n");

    public void Load(string? path)
    {
        _items.Clear();
        _dirs.Clear();
        Invalidate();
        FilePath = string.IsNullOrEmpty(path) ? null : path;
        _knownSize = 0;
        _knownDirsSize = 0;
        if (FilePath == null) return;
        try
        {
            if (File.Exists(FilePath))
            {
                var bytes = ReadShared(FilePath, 0, out _knownSize);
                AddRange(Parse(Encoding.UTF8.GetString(bytes)));
            }
        }
        catch { }
        try
        {
            if (!File.Exists(DirsFilePath)) return;
            var bytes = ReadShared(DirsFilePath!, 0, out _knownDirsSize);
            int records = ParseDirs(Encoding.UTF8.GetString(bytes));
            // the file only grows; drop records of entries that left the history once it gets big
            if (records > 2 * Math.Max(MaxSize, 1000)) RewriteDirs();
        }
        catch { }
    }

    int ParseDirs(string content)
    {
        int n = 0;
        foreach (var rec in Parse(content))
        {
            int tab = rec.IndexOf('\t');
            if (tab <= 0) continue;
            NoteDir(rec.Substring(tab + 1), rec.Substring(0, tab));
            n++;
        }
        return n;
    }

    static string NormalizeDir(string dir) => Path.TrimEndingDirectorySeparator(dir);

    void NoteDir(string entry, string dir)
    {
        dir = NormalizeDir(dir);
        if (!_dirs.TryGetValue(entry, out var list)) _dirs[entry] = list = new List<string>(1);
        int i = list.FindIndex(d => DirComparer.Equals(d, dir));
        if (i >= 0) list.RemoveAt(i);
        list.Add(dir);
        if (list.Count > MaxDirsPerEntry) list.RemoveAt(0);
    }

    /// <summary>The directories the entry ran in (the last 16, most recent last), or null.</summary>
    public IReadOnlyList<string>? DirsOf(string entry) => _dirs.TryGetValue(entry, out var list) ? list : null;

    /// <summary>Whether the entry was run in this directory (one of the last 16 it ran in).</summary>
    public bool RanIn(string entry, string dir)
    {
        if (!_dirs.TryGetValue(entry, out var list)) return false;
        dir = NormalizeDir(dir);
        foreach (var d in list) if (DirComparer.Equals(d, dir)) return true;
        return false;
    }

    static byte[] ReadShared(string path, long from, out long length)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        length = fs.Length;
        if (from >= length) return [];
        fs.Seek(from, SeekOrigin.Begin);
        var buf = new byte[length - from];
        int read = 0;
        while (read < buf.Length)
        {
            int n = fs.Read(buf, read, buf.Length - read);
            if (n <= 0) break;
            read += n;
        }
        return read == buf.Length ? buf : buf.AsSpan(0, read).ToArray();
    }

    /// <summary>Adds entries (oldest first) to memory only, removing older duplicates.</summary>
    public void AddRange(IEnumerable<string> entries)
    {
        foreach (var e in entries) _items.Add(e);
        // dedupe keeping the newest occurrence
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<string>(_items.Count);
        for (int i = _items.Count - 1; i >= 0; i--)
            if (seen.Add(_items[i])) kept.Add(_items[i]);
        kept.Reverse();
        if (kept.Count > MaxSize) kept.RemoveRange(0, kept.Count - MaxSize);
        _items.Clear();
        _items.AddRange(kept);
        Invalidate();
    }

    /// <summary>Picks up entries appended to the history file by other sessions.</summary>
    public void Sync()
    {
        if (FilePath == null) return;
        try
        {
            var fi = new FileInfo(FilePath);
            if (!fi.Exists) return;
            if (fi.Length < _knownSize) { Load(FilePath); return; }
            if (fi.Length != _knownSize)
            {
                var bytes = ReadShared(FilePath, _knownSize, out var len);
                _knownSize = len;
                AddRange(Parse(Encoding.UTF8.GetString(bytes)));
            }
        }
        catch { }
        try
        {
            var di = new FileInfo(DirsFilePath!);
            if (!di.Exists || di.Length == _knownDirsSize) return;
            long from = di.Length < _knownDirsSize ? 0 : _knownDirsSize;   // rewritten by another session
            var bytes = ReadShared(DirsFilePath!, from, out var len);
            _knownDirsSize = len;
            ParseDirs(Encoding.UTF8.GetString(bytes));
        }
        catch { }
    }

    /// <summary>Records an accepted line, and the directory it runs in. Returns false if it was ignored.</summary>
    public bool Add(string line, string? dir = null)
    {
        line = line.Replace("\r\n", "\n");
        if (line.Trim().Length == 0) return false;
        if (Options.GetBool("history_ignore_space") && line.StartsWith(' ')) return false;
        bool save = true;
        var pat = Options.Get("history_ignore_pattern");
        if (!string.IsNullOrEmpty(pat))
        {
            try { if (Regex.IsMatch(line, pat, RegexOptions.IgnoreCase)) save = false; } catch { }
        }
        _items.Remove(line);
        _items.Add(line);
        if (_items.Count > MaxSize) _items.RemoveRange(0, _items.Count - MaxSize);
        Invalidate();
        // a tab or newline in the directory would break the record format
        if (dir != null && dir.IndexOfAny(['\t', '\n', '\r']) >= 0) dir = null;
        if (dir != null) NoteDir(line, dir);
        if (save && FilePath != null)
        {
            Append(FilePath, Serialize(line), ref _knownSize);
            if (dir != null) Append(DirsFilePath!, Serialize(NormalizeDir(dir) + "\t" + line), ref _knownDirsSize);
        }
        return true;
    }

    static void Append(string path, string record, ref long knownSize)
    {
        var data = Encoding.UTF8.GetBytes(record + Environment.NewLine);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                bool wasUpToDate = File.Exists(path) ? new FileInfo(path).Length == knownSize : knownSize == 0;
                using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                {
                    fs.Write(data, 0, data.Length);
                }
                // Only skip over our own write if we had already seen everything before it.
                if (wasUpToDate) knownSize = new FileInfo(path).Length;
                return;
            }
            catch (IOException) { Thread.Sleep(10); }
            catch { return; }
        }
    }

    /// <summary>Removes entries (index 0 = oldest) from memory. Call Rewrite to update the file.</summary>
    public void RemoveRange(int index, int count)
    {
        _items.RemoveRange(index, count);
        Invalidate();
    }

    /// <summary>Replaces the history file with the entries in memory (an empty file if there are none).</summary>
    public void Rewrite()
    {
        if (FilePath == null) return;
        var sb = new StringBuilder();
        foreach (var e in _items) sb.Append(Serialize(e)).Append(Environment.NewLine);
        var data = Encoding.UTF8.GetBytes(sb.ToString());
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using (var fs = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            fs.Write(data, 0, data.Length);
        _knownSize = data.Length;
        RewriteDirs();
    }

    /// <summary>Replaces the .dirs file with the directories of the entries in memory.</summary>
    void RewriteDirs()
    {
        if (DirsFilePath == null) return;
        try
        {
            var sb = new StringBuilder();
            foreach (var e in _items)
                if (_dirs.TryGetValue(e, out var list))
                    foreach (var d in list) sb.Append(Serialize(d + "\t" + e)).Append(Environment.NewLine);
            if (sb.Length == 0 && !File.Exists(DirsFilePath)) return;
            var data = Encoding.UTF8.GetBytes(sb.ToString());
            using (var fs = new FileStream(DirsFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                fs.Write(data, 0, data.Length);
            _knownDirsSize = data.Length;
        }
        catch { }
    }

    void BuildBlob()
    {
        var sb = new StringBuilder();
        var offs = new int[_items.Count];
        sb.Append(Sep);
        for (int k = 0; k < _items.Count; k++)
        {
            offs[k] = sb.Length;
            sb.Append(_items[_items.Count - 1 - k]).Append(Sep);
        }
        _blob = sb.ToString();
        _offsets = offs;
    }

    /// <summary>Most recent entry that starts with prefix and is longer than it.</summary>
    public string? FindPrefix(string prefix) => PrefixMatches(prefix).FirstOrDefault();

    /// <summary>Entries that start with prefix and are longer than it, newest first.</summary>
    public IEnumerable<string> PrefixMatches(string prefix)
    {
        if (prefix.Length == 0 || _items.Count == 0) yield break;
        if (prefix.IndexOf(Sep) >= 0) yield break;
        if (_blob == null) BuildBlob();
        var blob = _blob!;
        var needle = Sep + prefix;
        int from = 0;
        while (true)
        {
            int idx = blob.IndexOf(needle, from, StringComparison.Ordinal);
            if (idx < 0) yield break;
            int start = idx + 1;
            int end = blob.IndexOf(Sep, start + prefix.Length);
            if (end < 0) yield break;
            if (end - start > prefix.Length) yield return blob.Substring(start, end - start);
            from = end;
        }
    }

    /// <summary>Index of the nearest entry before/after 'from' that starts with prefix and differs from 'current'.</summary>
    public int SearchPrefix(string prefix, int from, bool backward, string? current = null)
    {
        if (backward)
        {
            for (int i = Math.Min(from, _items.Count - 1); i >= 0; i--)
                if (_items[i].StartsWith(prefix, StringComparison.Ordinal) && _items[i] != current) return i;
        }
        else
        {
            for (int i = Math.Max(from, 0); i < _items.Count; i++)
                if (_items[i].StartsWith(prefix, StringComparison.Ordinal) && _items[i] != current) return i;
        }
        return -1;
    }

    /// <summary>Substring search used by incremental search. Smart case: case-sensitive only if query has upper case.</summary>
    public static StringComparison SmartCase(string query) =>
        query.Any(char.IsUpper) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
}
