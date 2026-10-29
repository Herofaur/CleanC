using CleanC.Core.Models;

namespace CleanC.Core.Services;

public sealed class CleanupRuleEngine
{
    private static readonly HashSet<string> InstallerExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".iso", ".img", ".msi", ".msix", ".exe", ".cab", ".tar", ".gz"
    };

    private readonly PathSafety _pathSafety;
    private readonly string _userTemp;
    private readonly string _windowsTemp;
    private readonly string _crashDumps;
    private readonly string _downloads;
    private readonly string _desktop;
    private readonly string[] _browserCaches;

    public CleanupRuleEngine(PathSafety? pathSafety = null, string? userProfile = null, string? localAppData = null)
    {
        _pathSafety = pathSafety ?? new PathSafety();
        userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        _userTemp = PathSafety.Normalize(Path.GetTempPath());
        _windowsTemp = PathSafety.Normalize(Path.Combine(systemRoot, "Temp"));
        _crashDumps = PathSafety.Normalize(Path.Combine(localAppData, "CrashDumps"));
        _downloads = PathSafety.Normalize(Path.Combine(userProfile, "Downloads"));
        _desktop = PathSafety.Normalize(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        _browserCaches =
        [
            PathSafety.Normalize(Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache")),
            PathSafety.Normalize(Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Code Cache")),
            PathSafety.Normalize(Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache")),
            PathSafety.Normalize(Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Code Cache"))
        ];
    }

    public CleanupCandidate? Classify(FileEntry file, DateTime utcNow)
    {
        if (file.Size <= 0 || _pathSafety.IsProtected(file.FullPath))
        {
            return null;
        }

        var age = utcNow - file.LastWriteTimeUtc;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age >= TimeSpan.FromDays(1) && _pathSafety.IsWithin(file.FullPath, _userTemp))
        {
            return Create(file, "user-temp", "用户临时文件", "超过 24 小时的临时文件，通常可以安全清理。", CleanupLevel.Recommended);
        }

        if (age >= TimeSpan.FromDays(2) && _pathSafety.IsWithin(file.FullPath, _windowsTemp))
        {
            return Create(file, "windows-temp", "Windows 临时文件", "超过 48 小时的系统临时文件，部分文件可能需要管理员权限。", CleanupLevel.Recommended);
        }

        if (_pathSafety.IsWithin(file.FullPath, _crashDumps))
        {
            return Create(file, "crash-dumps", "应用崩溃转储", "用于故障诊断；不排查崩溃时通常可以清理。", CleanupLevel.Recommended);
        }

        if (age >= TimeSpan.FromDays(7) && _browserCaches.Any(cache => _pathSafety.IsWithin(file.FullPath, cache)))
        {
            return Create(file, "browser-cache", "浏览器缓存", "较旧的 Chrome 或 Edge 缓存，关闭浏览器后可清理。", CleanupLevel.Recommended);
        }

        var extension = Path.GetExtension(file.FullPath);
        var isDropZone = _pathSafety.IsWithin(file.FullPath, _downloads) || _pathSafety.IsWithin(file.FullPath, _desktop);
        if (age >= TimeSpan.FromDays(30) && isDropZone && InstallerExtensions.Contains(extension))
        {
            return Create(file, "old-installers", "旧安装包与压缩包", "可能是已经使用过的安装包或归档，请确认不再需要。", CleanupLevel.ConfirmRequired);
        }

        if (age >= TimeSpan.FromDays(90) && _pathSafety.IsWithin(file.FullPath, _downloads))
        {
            return Create(file, "old-downloads", "较旧的下载文件", "下载目录中超过 90 天的文件，可能仍包含个人资料。", CleanupLevel.ConfirmRequired);
        }

        if (file.Size >= 1024L * 1024 * 1024)
        {
            return Create(file, "large-files", "超大文件", "文件超过 1 GB，删除前应确认用途并检查备份。", CleanupLevel.ConfirmRequired);
        }

        return null;
    }

    private static CleanupCandidate Create(FileEntry file, string id, string title, string reason, CleanupLevel level) =>
        new(file, id, title, reason, level);
}
