using CleanC.Core.Models;

namespace CleanC.Core.Services;

public sealed class CleanupRuleEngine
{
    private static readonly HashSet<string> InstallerExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".iso", ".img", ".msi", ".msix", ".exe", ".cab", ".tar", ".gz"
    };

    private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".docm", ".xls", ".xlsx", ".xlsm", ".ppt", ".pptx", ".pptm",
        ".pdf", ".txt", ".rtf", ".csv", ".odt", ".ods", ".odp", ".epub", ".mobi", ".md"
    };

    private static readonly HashSet<string> PersonalMediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".raw",
        ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".flv", ".mp3", ".wav", ".flac", ".aac", ".m4a"
    };

    private readonly PathSafety _pathSafety;
    private readonly string _userTemp;
    private readonly string _windowsTemp;
    private readonly string _crashDumps;
    private readonly string _downloads;
    private readonly string _desktop;

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
        _desktop = PathSafety.Normalize(Path.Combine(userProfile, "Desktop"));
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

        var path = NormalizeForMatching(file.FullPath);
        var extension = Path.GetExtension(file.FullPath);
        var isUserAppData = IsUserAppDataPath(path);

        if (IsSkillOrPluginRuntime(path))
        {
            return null;
        }

        if (age >= TimeSpan.FromDays(3) &&
            (_pathSafety.IsWithin(file.FullPath, _userTemp) || IsUserLocalTemp(path)))
        {
            return Create(file, "user-temp", "用户临时文件", "超过 3 天的用户临时文件，通常可安全清理；正在使用的文件会由系统拒绝删除。", CleanupLevel.Recommended);
        }

        if (age >= TimeSpan.FromDays(7) && _pathSafety.IsWithin(file.FullPath, _windowsTemp))
        {
            return Create(file, "windows-temp", "Windows 临时文件", "超过 7 天的系统临时文件，部分文件可能需要管理员权限。", CleanupLevel.Recommended);
        }

        if (age >= TimeSpan.FromDays(14) && IsStaleInstallerStaging(path))
        {
            return Create(file, "stale-install-staging", "残留安装暂存文件", "旧的 Codex 运行时安装暂存目录可在安装结束后删除，不会删除当前使用的主运行时。", CleanupLevel.Recommended);
        }

        if (age >= TimeSpan.FromDays(7) && IsUserCrashReport(path, extension))
        {
            return Create(file, "crash-dumps", "旧应用崩溃报告", "超过 7 天的用户应用崩溃记录，仅在故障诊断时需要。", CleanupLevel.Recommended);
        }

        if (age >= TimeSpan.FromDays(3) && IsShaderCache(path))
        {
            return Create(file, "shader-cache", "着色器与显卡缓存", "DirectX、NVIDIA、AMD 或游戏着色器缓存可自动重建；清理后首次启动游戏可能短暂重新编译。", CleanupLevel.Recommended);
        }

        if (age >= TimeSpan.FromDays(7) && IsBrowserCache(path))
        {
            return Create(file, "browser-cache", "浏览器缓存", "Chrome、Edge、Firefox 等浏览器的旧缓存；建议关闭浏览器后清理。", CleanupLevel.Recommended);
        }

        if (age >= TimeSpan.FromDays(30) && IsDeveloperCache(path))
        {
            return Create(file, "developer-cache", "开发工具下载缓存", "pip、npm、NuGet、Gradle 等缓存可以重新下载，但清理后离线构建可能缺少依赖，因此需要手动确认。", CleanupLevel.ConfirmRequired);
        }

        if (age >= TimeSpan.FromDays(14) && IsApplicationCache(path, isUserAppData))
        {
            return Create(file, "application-cache", "应用程序缓存", "超过 14 天的 Cache、Code Cache、GPUCache 等可重建缓存；建议先退出相关应用。", CleanupLevel.Recommended);
        }

        if (age >= TimeSpan.FromDays(14) && IsThumbnailCache(path, file.Name))
        {
            return Create(file, "thumbnail-cache", "缩略图与图标缓存", "Windows 会在需要时重新生成缩略图和图标缓存。", CleanupLevel.Recommended);
        }

        if (age >= TimeSpan.FromDays(30) && IsDisposableDiagnosticLog(path, extension, isUserAppData))
        {
            return Create(file, "diagnostic-logs", "旧诊断日志", "用户应用产生的旧日志与跟踪文件，通常只在排查问题时需要。", CleanupLevel.Recommended);
        }

        if (age >= TimeSpan.FromDays(30) && IsSystemCrashDump(path, extension))
        {
            return Create(file, "system-crash-dumps", "系统崩溃转储", "Windows 内核或硬件故障转储可能很大。确认不再排查蓝屏、显卡或 USB-C 故障后再手动选择。", CleanupLevel.ConfirmRequired);
        }

        if (IsBrowserDownloadedModel(path))
        {
            return Create(file, "browser-models", "浏览器离线模型", "Chrome 下载的本地模型可以重新下载，但清理后相关智能功能可能暂时不可用并消耗网络流量。", CleanupLevel.ConfirmRequired);
        }

        if (IsApplicationUpdateCache(path, age))
        {
            return Create(file, "application-update-cache", "旧应用更新包与旧版本", "NVIDIA App 或 Logitech G HUB 保存的旧更新包、安装缓存和旧版本；退出相关应用后手动选择。", CleanupLevel.ConfirmRequired);
        }

        var isDropZone = _pathSafety.IsWithin(file.FullPath, _downloads) ||
                         _pathSafety.IsWithin(file.FullPath, _desktop) ||
                         path.Contains("\\downloads\\", StringComparison.Ordinal) ||
                         path.Contains("\\desktop\\", StringComparison.Ordinal);
        if (age >= TimeSpan.FromDays(30) && isDropZone && InstallerExtensions.Contains(extension))
        {
            return Create(file, "old-installers", "旧安装包与压缩包", "可能是已使用过的安装包或归档，也可能是重要备份；不会默认勾选。", CleanupLevel.ConfirmRequired);
        }

        if (IsChatData(path))
        {
            return Create(file, "chat-data", "聊天记录与聊天附件", "可能包含微信、QQ、Telegram、钉钉等聊天记录、附件或账号数据；仅在确认备份后手动选择。", CleanupLevel.ConfirmRequired);
        }

        if (IsPersonalLibrary(path) && DocumentExtensions.Contains(extension))
        {
            return Create(file, "personal-documents", "个人文档", "文档、表格、演示文稿、PDF 或文本资料；必须由用户逐项确认，不会默认勾选。", CleanupLevel.ConfirmRequired);
        }

        if (age >= TimeSpan.FromDays(90) &&
            (_pathSafety.IsWithin(file.FullPath, _downloads) || path.Contains("\\downloads\\", StringComparison.Ordinal)))
        {
            return Create(file, "old-downloads", "较旧的下载文件", "下载目录中超过 90 天的文件，可能仍包含个人资料；不会默认勾选。", CleanupLevel.ConfirmRequired);
        }

        if (IsPersonalLibrary(path) && PersonalMediaExtensions.Contains(extension) && file.Size >= 100L * 1024 * 1024)
        {
            return Create(file, "personal-media", "大型照片、视频与音频", "属于个人媒体文件，仅用于帮助发现占用空间的内容；必须手动选择。", CleanupLevel.ConfirmRequired);
        }

        if (file.Size >= 1024L * 1024 * 1024)
        {
            return Create(file, "large-files", "超大文件", "文件超过 1 GB，删除前应确认用途并检查备份。", CleanupLevel.ConfirmRequired);
        }

        return null;
    }

    private bool IsUserCrashReport(string path, string extension) =>
        _pathSafety.IsWithin(path, _crashDumps) ||
        path.Contains("\\crashdumps\\", StringComparison.Ordinal) ||
        path.Contains("\\saved\\crashes\\", StringComparison.Ordinal) ||
        path.Contains("\\windows\\wer\\reportarchive\\", StringComparison.Ordinal) ||
        path.Contains("\\windows\\wer\\reportqueue\\", StringComparison.Ordinal) ||
        extension.Equals(".dmp", StringComparison.OrdinalIgnoreCase) && IsUserAppDataPath(path);

    private static bool IsSystemCrashDump(string path, string extension) =>
        extension.Equals(".dmp", StringComparison.OrdinalIgnoreCase) &&
        (path.Contains("\\windows\\livekernelreports\\", StringComparison.Ordinal) ||
         path.EndsWith("\\windows\\memory.dmp", StringComparison.Ordinal));

    private static bool IsStaleInstallerStaging(string path) =>
        path.Contains("\\.cache\\codex-runtimes\\codex-runtime-install-", StringComparison.Ordinal);

    private static bool IsSkillOrPluginRuntime(string path) =>
        path.Contains("\\.agents\\skills\\", StringComparison.Ordinal) ||
        path.Contains("\\.codex\\skills\\", StringComparison.Ordinal) ||
        path.Contains("\\.codex\\plugins\\", StringComparison.Ordinal) ||
        path.Contains("\\skills\\", StringComparison.Ordinal);

    private static bool IsBrowserDownloadedModel(string path) =>
        path.Contains("\\google\\chrome\\user data\\optguideondevicemodel\\", StringComparison.Ordinal);

    private static bool IsApplicationUpdateCache(string path, TimeSpan age) =>
        (age >= TimeSpan.FromDays(90) && path.Contains("\\programdata\\nvidia corporation\\nvidia app\\updateframework\\ota-artifacts\\", StringComparison.Ordinal)) ||
        (age >= TimeSpan.FromDays(30) && path.Contains("\\programdata\\lghub\\cache\\", StringComparison.Ordinal));

    private static bool IsDeveloperCache(string path) =>
        path.Contains("\\.nuget\\packages\\", StringComparison.Ordinal) ||
        path.Contains("\\npm-cache\\", StringComparison.Ordinal) ||
        path.Contains("\\pip\\cache\\", StringComparison.Ordinal) ||
        path.Contains("\\.gradle\\caches\\", StringComparison.Ordinal) ||
        path.Contains("\\.cargo\\registry\\", StringComparison.Ordinal) ||
        path.Contains("\\yarn\\cache\\", StringComparison.Ordinal) ||
        path.Contains("\\pnpm-store\\", StringComparison.Ordinal);

    private static bool IsShaderCache(string path) =>
        path.Contains("\\d3dscache\\", StringComparison.Ordinal) ||
        path.Contains("\\nvidia\\dxcache\\", StringComparison.Ordinal) ||
        path.Contains("\\nvidia\\glcache\\", StringComparison.Ordinal) ||
        path.Contains("\\nvidia\\computecache\\", StringComparison.Ordinal) ||
        path.Contains("\\amd\\dxcache\\", StringComparison.Ordinal) ||
        path.Contains("\\amd\\vkcache\\", StringComparison.Ordinal) ||
        path.Contains("\\intel\\shadercache\\", StringComparison.Ordinal) ||
        path.Contains("\\shadercache\\", StringComparison.Ordinal) ||
        path.Contains("\\shader cache\\", StringComparison.Ordinal);

    private static bool IsBrowserCache(string path)
    {
        var isBrowser = path.Contains("\\google\\chrome\\", StringComparison.Ordinal) ||
                        path.Contains("\\microsoft\\edge\\", StringComparison.Ordinal) ||
                        path.Contains("\\mozilla\\firefox\\", StringComparison.Ordinal) ||
                        path.Contains("\\bravesoftware\\brave-browser\\", StringComparison.Ordinal) ||
                        path.Contains("\\vivaldi\\", StringComparison.Ordinal) ||
                        path.Contains("\\opera software\\", StringComparison.Ordinal);
        return isBrowser && HasCacheDirectory(path);
    }

    private static bool IsApplicationCache(string path, bool isUserAppData) =>
        isUserAppData && HasCacheDirectory(path);

    private static bool HasCacheDirectory(string path) =>
        path.Contains("\\cache\\", StringComparison.Ordinal) ||
        path.Contains("\\caches\\", StringComparison.Ordinal) ||
        path.Contains("\\code cache\\", StringComparison.Ordinal) ||
        path.Contains("\\gpucache\\", StringComparison.Ordinal) ||
        path.Contains("\\cache2\\entries\\", StringComparison.Ordinal) ||
        path.Contains("\\cachestorage\\", StringComparison.Ordinal) ||
        path.Contains("\\cache_storage\\", StringComparison.Ordinal) ||
        path.Contains("\\webcache\\", StringComparison.Ordinal) ||
        path.Contains("\\media cache\\", StringComparison.Ordinal);

    private static bool IsThumbnailCache(string path, string fileName) =>
        path.Contains("\\microsoft\\windows\\explorer\\", StringComparison.Ordinal) &&
        (fileName.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) ||
         fileName.StartsWith("iconcache_", StringComparison.OrdinalIgnoreCase));

    private static bool IsDisposableDiagnosticLog(string path, string extension, bool isUserAppData) =>
        isUserAppData && (extension.Equals(".log", StringComparison.OrdinalIgnoreCase) || extension.Equals(".etl", StringComparison.OrdinalIgnoreCase)) &&
        (path.Contains("\\logs\\", StringComparison.Ordinal) || path.Contains("\\diagnostics\\", StringComparison.Ordinal));

    private static bool IsChatData(string path) =>
        path.Contains("\\documents\\wechat files\\", StringComparison.Ordinal) ||
        path.Contains("\\documents\\tencent files\\", StringComparison.Ordinal) ||
        path.Contains("\\wechat files\\", StringComparison.Ordinal) ||
        path.Contains("\\tencent files\\", StringComparison.Ordinal) ||
        path.Contains("\\telegram desktop\\tdata\\", StringComparison.Ordinal) ||
        path.Contains("\\dingtalk\\", StringComparison.Ordinal) && path.Contains("\\user data\\", StringComparison.Ordinal);

    private static bool IsUserLocalTemp(string path) =>
        IsUserAppDataPath(path) && path.Contains("\\appdata\\local\\temp\\", StringComparison.Ordinal);

    private static bool IsUserDataPath(string path) =>
        path.Contains("\\users\\", StringComparison.Ordinal);

    private static bool IsUserAppDataPath(string path) =>
        IsUserDataPath(path) && path.Contains("\\appdata\\", StringComparison.Ordinal);

    private static bool IsPersonalLibrary(string path) =>
        IsUserDataPath(path) && !IsUserAppDataPath(path) &&
        (path.Contains("\\documents\\", StringComparison.Ordinal) ||
         path.Contains("\\desktop\\", StringComparison.Ordinal) ||
         path.Contains("\\downloads\\", StringComparison.Ordinal) ||
         path.Contains("\\onedrive\\", StringComparison.Ordinal) ||
         path.Contains("\\pictures\\", StringComparison.Ordinal) ||
         path.Contains("\\videos\\", StringComparison.Ordinal) ||
         path.Contains("\\music\\", StringComparison.Ordinal));

    private static string NormalizeForMatching(string path) =>
        Path.GetFullPath(path).Replace('/', '\\').ToLowerInvariant();

    private static CleanupCandidate Create(FileEntry file, string id, string title, string reason, CleanupLevel level) =>
        new(file, id, title, reason, level);
}
