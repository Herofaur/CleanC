using System.Collections.ObjectModel;
using System.IO;
using CleanC.App.Infrastructure;
using CleanC.Core.Formatting;
using CleanC.Core.Models;
using CleanC.Core.Services;

namespace CleanC.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly DiskScanner _scanner = new();
    private CancellationTokenSource? _scanCancellation;
    private string _scanPath = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
    private bool _isScanning;
    private string _statusText = "准备就绪，选择磁盘后开始扫描";
    private string _currentPath = string.Empty;
    private string _elapsedText = "--";
    private long _scannedBytes;
    private long _scannedFiles;
    private long _scannedDirectories;
    private long _potentialCleanupBytes;
    private int _accessErrorCount;
    private DriveSpaceInfo? _drive;
    private CleanupGroupViewModel? _selectedCleanupGroup;

    public string ScanPath
    {
        get => _scanPath;
        set => SetProperty(ref _scanPath, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                OnPropertyChanged(nameof(IsNotScanning));
            }
        }
    }

    public bool IsNotScanning => !IsScanning;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string CurrentPath
    {
        get => _currentPath;
        private set => SetProperty(ref _currentPath, value);
    }

    public string ElapsedText
    {
        get => _elapsedText;
        private set => SetProperty(ref _elapsedText, value);
    }

    public long ScannedBytes
    {
        get => _scannedBytes;
        private set => SetProperty(ref _scannedBytes, value);
    }

    public long ScannedFiles
    {
        get => _scannedFiles;
        private set => SetProperty(ref _scannedFiles, value);
    }

    public long ScannedDirectories
    {
        get => _scannedDirectories;
        private set => SetProperty(ref _scannedDirectories, value);
    }

    public long PotentialCleanupBytes
    {
        get => _potentialCleanupBytes;
        private set => SetProperty(ref _potentialCleanupBytes, value);
    }

    public int AccessErrorCount
    {
        get => _accessErrorCount;
        private set => SetProperty(ref _accessErrorCount, value);
    }

    public DriveSpaceInfo? Drive
    {
        get => _drive;
        private set
        {
            if (SetProperty(ref _drive, value))
            {
                OnPropertyChanged(nameof(DriveTitle));
                OnPropertyChanged(nameof(DriveUsageText));
                OnPropertyChanged(nameof(DriveUsedPercent));
            }
        }
    }

    public string DriveTitle => Drive is null ? "磁盘信息" : $"{Drive.Name} {Drive.Label}".Trim();
    public string DriveUsageText => Drive is null ? "--" : $"已用 {SizeFormatter.Format(Drive.UsedBytes)} / {SizeFormatter.Format(Drive.TotalBytes)}";
    public double DriveUsedPercent => Drive?.UsedPercent ?? 0;
    public ObservableCollection<DirectoryEntry> DirectoryRoots { get; } = [];
    public ObservableCollection<DirectoryRowViewModel> LargestDirectories { get; } = [];
    public ObservableCollection<FileEntry> LargestFiles { get; } = [];
    public ObservableCollection<CleanupGroupViewModel> CleanupGroups { get; } = [];

    public CleanupGroupViewModel? SelectedCleanupGroup
    {
        get => _selectedCleanupGroup;
        set => SetProperty(ref _selectedCleanupGroup, value);
    }

    public IReadOnlyList<CleanupCandidateViewModel> GetSelectedCleanupItems() =>
        CleanupGroups.SelectMany(group => group.Candidates).Where(candidate => candidate.IsSelected).ToArray();

    public async Task StartScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        var normalizedPath = Path.GetFullPath(ScanPath);
        if (!Directory.Exists(normalizedPath))
        {
            throw new DirectoryNotFoundException($"扫描路径不存在：{normalizedPath}");
        }

        ResetResults();
        ScanPath = normalizedPath;
        Drive = DiskInfoService.GetForPath(normalizedPath);
        _scanCancellation = new CancellationTokenSource();
        IsScanning = true;
        StatusText = "正在扫描，请稍候…";

        var progress = new Progress<ScanProgress>(update =>
        {
            CurrentPath = update.CurrentPath;
            ScannedFiles = update.ScannedFiles;
            ScannedDirectories = update.ScannedDirectories;
            ScannedBytes = update.DiscoveredBytes;
            ElapsedText = FormatElapsed(update.Elapsed);
            StatusText = $"正在扫描 · {update.ScannedFiles:N0} 个文件";
        });

        try
        {
            var result = await _scanner.ScanAsync(normalizedPath, progress, _scanCancellation.Token);
            ApplyResult(result);
            StatusText = $"扫描完成 · 用时 {FormatElapsed(result.Elapsed)}";
            CurrentPath = $"发现 {result.Errors.Count:N0} 个无法访问的项目；这通常是 Windows 权限保护导致的。";
        }
        catch (OperationCanceledException)
        {
            StatusText = "扫描已取消";
            CurrentPath = string.Empty;
        }
        finally
        {
            IsScanning = false;
            _scanCancellation.Dispose();
            _scanCancellation = null;
        }
    }

    public void CancelScan() => _scanCancellation?.Cancel();

    public void RemoveCleanedFiles(IEnumerable<string> paths)
    {
        var removed = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        foreach (var group in CleanupGroups)
        {
            group.RemovePaths(removed);
        }
        PotentialCleanupBytes = CleanupGroups.Sum(group => group.TotalSize);
    }

    public void SetCleanupStatus(string message) => StatusText = message;

    private void ApplyResult(ScanResult result)
    {
        DirectoryRoots.Add(result.RootDirectory);
        foreach (var directory in result.LargestDirectories.Where(item => !item.FullPath.Equals(result.RootPath, StringComparison.OrdinalIgnoreCase)).Take(500))
        {
            LargestDirectories.Add(new DirectoryRowViewModel(directory, result.TotalSize));
        }
        foreach (var file in result.LargestFiles)
        {
            LargestFiles.Add(file);
        }
        foreach (var group in result.CleanupGroups)
        {
            CleanupGroups.Add(new CleanupGroupViewModel(group));
        }

        SelectedCleanupGroup = CleanupGroups.FirstOrDefault();
        ScannedBytes = result.TotalSize;
        ScannedFiles = result.FileCount;
        ScannedDirectories = result.DirectoryCount;
        PotentialCleanupBytes = result.CleanupGroups.Sum(group => group.TotalSize);
        AccessErrorCount = result.Errors.Count;
        ElapsedText = FormatElapsed(result.Elapsed);
    }

    private void ResetResults()
    {
        DirectoryRoots.Clear();
        LargestDirectories.Clear();
        LargestFiles.Clear();
        CleanupGroups.Clear();
        SelectedCleanupGroup = null;
        ScannedBytes = 0;
        ScannedFiles = 0;
        ScannedDirectories = 0;
        PotentialCleanupBytes = 0;
        AccessErrorCount = 0;
        ElapsedText = "--";
        CurrentPath = string.Empty;
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalMinutes >= 1 ? $"{(int)elapsed.TotalMinutes} 分 {elapsed.Seconds} 秒" : $"{elapsed.TotalSeconds:0.0} 秒";
}
