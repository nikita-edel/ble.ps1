namespace Ble;

public static class FileSystemPaths
{
    static readonly string[] ExeExtensions = [".exe", ".com", ".bat", ".cmd", ".ps1", ".msi", ".sh"];

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
