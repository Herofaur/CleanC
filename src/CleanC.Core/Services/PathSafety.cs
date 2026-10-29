namespace CleanC.Core.Services;

public sealed class PathSafety
{
    private readonly string _systemDrive;
    private readonly string[] _protectedRoots;
    private readonly HashSet<string> _protectedFiles;

    public PathSafety(string? systemDrive = null)
    {
        _systemDrive = NormalizeRoot(systemDrive ?? Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\");
        _protectedRoots =
        [
            Combine("Windows", "WinSxS"),
            Combine("Windows", "Installer"),
            Combine("Windows", "System32"),
            Combine("Windows", "SysWOW64"),
            Combine("Program Files"),
            Combine("Program Files (x86)"),
            Combine("ProgramData"),
            Combine("System Volume Information"),
            Combine("Recovery"),
            Combine("$Recycle.Bin")
        ];

        _protectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Normalize(Combine("pagefile.sys")),
            Normalize(Combine("hiberfil.sys")),
            Normalize(Combine("swapfile.sys")),
            Normalize(Combine("bootmgr"))
        };
    }

    public bool IsProtected(string path)
    {
        var normalized = Normalize(path);
        return _protectedFiles.Contains(normalized) ||
               _protectedRoots.Any(root => IsWithin(normalized, root));
    }

    public bool IsWithin(string candidate, string parent)
    {
        var normalizedCandidate = Normalize(candidate);
        var normalizedParent = Normalize(parent);
        return normalizedCandidate.Equals(normalizedParent, StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private string Combine(params string[] parts) => Path.Combine([_systemDrive, .. parts]);

    private static string NormalizeRoot(string root) =>
        Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
}
