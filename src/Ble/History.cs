using System.Text;
using System.Text.RegularExpressions;

namespace Ble;

/// <summary>
/// Command history. Entries are kept unique (most recent occurrence wins), oldest first.
/// The on-disk format is PSReadLine's: one entry per line, embedded newlines written as "`\n".
/// </summary>
public sealed class History
{
    const char Sep = '\u0001';

    readonly List<string> _items = new();
    string? _blob;          // Sep + newest + Sep + ... + oldest + Sep
    int[]? _offsets;        // start offset in _blob of k-th newest entry
    long _knownSize;

    public string? FilePath { get; private set; }
    public int MaxSize { get; set; } = 10000;
    public IReadOnlyList<string> Items => _items;
    public int Count => _items.Count;
    public string this[int i] => _items[i];

    public void Clear()
    {
        _items.Clear();
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
        Invalidate();
        FilePath = string.IsNullOrEmpty(path) ? null : path;
        _knownSize = 0;
        if (FilePath == null) return;
        try
        {
            if (!File.Exists(FilePath)) return;
            var bytes = ReadShared(FilePath, 0, out _knownSize);
            AddRange(Parse(Encoding.UTF8.GetString(bytes)));
        }
        catch { }
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
            if (fi.Length == _knownSize) return;
            var bytes = ReadShared(FilePath, _knownSize, out var len);
            _knownSize = len;
            AddRange(Parse(Encoding.UTF8.GetString(bytes)));
        }
        catch { }
    }

    /// <summary>Records an accepted line. Returns false if it was ignored.</summary>
    public bool Add(string line)
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
        if (save) Append(line);
        return true;
    }

    void Append(string line)
    {
        if (FilePath == null) return;
        var data = Encoding.UTF8.GetBytes(Serialize(line) + Environment.NewLine);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                bool wasUpToDate = File.Exists(FilePath) ? new FileInfo(FilePath).Length == _knownSize : _knownSize == 0;
                using (var fs = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                {
                    fs.Write(data, 0, data.Length);
                }
                // Only skip over our own write if we had already seen everything before it.
                if (wasUpToDate) _knownSize = new FileInfo(FilePath).Length;
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
    public string? FindPrefix(string prefix)
    {
        if (prefix.Length == 0 || _items.Count == 0) return null;
        if (prefix.IndexOf(Sep) >= 0) return null;
        if (_blob == null) BuildBlob();
        var blob = _blob!;
        var needle = Sep + prefix;
        int from = 0;
        while (true)
        {
            int idx = blob.IndexOf(needle, from, StringComparison.Ordinal);
            if (idx < 0) return null;
            int start = idx + 1;
            int end = blob.IndexOf(Sep, start + prefix.Length);
            if (end < 0) return null;
            if (end - start > prefix.Length) return blob.Substring(start, end - start);
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
