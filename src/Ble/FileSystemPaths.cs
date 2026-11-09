using System.Management.Automation;

namespace Ble;

public static class FileSystemPaths
{
    static readonly string[] ExeExtensions = [".exe", ".com", ".bat", ".cmd", ".ps1", ".msi", ".sh"];

    /// <summary>Whether Foo and foo are different files. Not on macOS, whose disks usually ignore case.</summary>
    internal static bool CaseSensitive = OperatingSystem.IsLinux();

    internal static StringComparison Comparison => CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    // the tooltip tells files from other providers' items (Env:/HOME), which look the same otherwise
    internal static bool IsCaseSensitiveFile(CompletionItem it) =>
        CaseSensitive && it.Type is CompletionResultType.ProviderItem or CompletionResultType.ProviderContainer && it.ToolTip.StartsWith('/');

    /// <summary>
    /// PowerShell ignores case when it completes file names, also on Linux. Like bash (ble.sh), drop
    /// the files that only match what was typed because of that. 'typed' may hold wildcards.
    /// </summary>
    internal static CompletionSet? KeepCase(CompletionSet? cs, string text, int cursor)
    {
        if (cs == null || !CaseSensitive || cs.ReplacementIndex < 0) return cs;
        int end = Math.Min(Math.Min(cursor, cs.ReplacementIndex + cs.ReplacementLength), text.Length);
        if (end <= cs.ReplacementIndex) return cs;
        var typed = text.Substring(cs.ReplacementIndex, end - cs.ReplacementIndex).Trim('\'', '"');
        typed = typed.Substring(Math.Max(typed.LastIndexOf('/'), typed.LastIndexOf('\\')) + 1);
        if (typed.Length == 0) return cs;
        var kept = cs.Items.Where(i => !IsCaseSensitiveFile(i) || !OnlyMatchesIgnoringCase(i.ListItemText, typed)).ToList();
        return kept.Count == cs.Items.Count ? cs : cs with { Items = kept };
    }

    static bool OnlyMatchesIgnoringCase(string name, string typed)
    {
        var pattern = typed + "*";
        try { return WildcardPattern.Get(pattern, WildcardOptions.IgnoreCase).IsMatch(name) && !WildcardPattern.Get(pattern, WildcardOptions.None).IsMatch(name); }
        catch (WildcardPatternException) { return false; }
    }

    /// <summary>Classifies a (possibly relative) file system path without touching other providers.</summary>
    public static PathKind Resolve(string path, string cwd)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || path.Length > 400) return PathKind.None;
            if (path.IndexOfAny(['*', '?', '[', '\n', '"', '<', '>', '|']) >= 0) return PathKind.None;
            if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)) return PathKind.None;
            int colon = path.IndexOf(':');
            if (colon >= 0 && !(colon == 1 && char.IsLetter(path[0]))) return PathKind.None; // provider path like env: or HKLM:
            string p = path;
            if (p == "~" || p.StartsWith("~/", StringComparison.Ordinal) || p.StartsWith(@"~\", StringComparison.Ordinal))
                p = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + p.Substring(1);
            p = Path.IsPathRooted(p) ? p : Path.Combine(cwd, p);
            bool isDir = Directory.Exists(p);
            if ((isDir || File.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) return PathKind.Link;
            if (isDir) return PathKind.Directory;
            if (File.Exists(p))
            {
                if (!OperatingSystem.IsWindows())
                    return (File.GetUnixFileMode(p) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0
                        ? PathKind.Executable : PathKind.File;
                var ext = Path.GetExtension(p);
                return ExeExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase) ? PathKind.Executable : PathKind.File;
            }
        }
        catch { }
        return PathKind.None;
    }
}
