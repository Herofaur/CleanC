using System.Collections.ObjectModel;
using System.IO;
using CleanC.App.Infrastructure;
using CleanC.Core.Formatting;
using CleanC.Core.Models;
using CleanC.Core.Services;

namespace CleanC.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly WizTreeCsvImporter _csvImporter = new();
    private CancellationTokenSource? _scanCancellation;
    private string _csvPath = string.Empty;
    private bool _isScanning;
    private string _statusText = "准备就绪，请选择 WizTree 导出的 CSV 文件";
    private string _currentPath = string.Empty;
    private string _elapsedText = "--";
    private long _scannedBytes;
    private long _scannedFiles;
    private long _scannedDirectories;
    private long _potentialCleanupBytes;
    private int _accessErrorCount;
    private DriveSpaceInfo? _drive;
    private CleanupGroupViewModel? _selectedCleanupGroup;

    public string CsvPath
    {
        get => _csvPath;
        set => SetProperty(ref _csvPath, value);
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

    public async Task ImportCsvAsync()
    {
        if (IsScanning)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(CsvPath))
        {
            throw new InvalidOperationException("请先选择 WizTree 导出的 CSV 文件。");
        }

        var normalizedPath = Path.GetFullPath(CsvPath);
        if (!File.Exists(normalizedPath))
        {
            throw new FileNotFoundException("CSV 文件不存在。", normalizedPath);
        }
        if (!Path.GetExtension(normalizedPath).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("请选择扩展名为 .csv 的 WizTree 导出文件。");
        }

        ResetResults();
        CsvPath = normalizedPath;
        _scanCancellation = new CancellationTokenSource();
        IsScanning = true;
        StatusText = "正在分析 WizTree CSV，请稍候…";

        var progress = new Progress<ScanProgress>(update =>
        {
            CurrentPath = update.CurrentPath;
            ScannedFiles = update.ScannedFiles;
            ScannedDirectories = update.ScannedDirectories;
            ScannedBytes = update.DiscoveredBytes;
            ElapsedText = FormatElapsed(update.Elapsed);
            StatusText = $"正在分析 CSV · {update.ScannedFiles:N0} 个文件";
        });

        try
        {
            var result = await _csvImporter.ImportAsync(normalizedPath, progress, _scanCancellation.Token);
            Drive = DiskInfoService.GetForPath(result.RootPath);
            ApplyResult(result);
            StatusText = $"CSV 分析完成 · 用时 {FormatElapsed(result.Elapsed)}";
            CurrentPath = result.Errors.Count == 0
                ? $"已读取 {result.FileCount:N0} 个文件；清理时会再次检查文件是否仍存在。"
                : $"有 {result.Errors.Count:N0} 行无法识别或被跳过；其余数据已正常导入。";
        }
        catch (OperationCanceledException)
        {
            StatusText = "CSV 分析已取消";
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

    public void RemoveProcessedCleanupItems(IEnumerable<string> paths)
    {
        var removed = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        if (removed.Count == 0)
        {
            return;
        }

        var selectedIndex = SelectedCleanupGroup is null ? -1 : CleanupGroups.IndexOf(SelectedCleanupGroup);
        var selectedWasRemoved = false;
        for (var index = CleanupGroups.Count - 1; index >= 0; index--)
        {
            var group = CleanupGroups[index];
            group.RemovePaths(removed);
            if (group.Candidates.Count > 0)
            {
                continue;
            }

            selectedWasRemoved |= ReferenceEquals(group, SelectedCleanupGroup);
            CleanupGroups.RemoveAt(index);
        }

        if (selectedWasRemoved)
        {
            SelectedCleanupGroup = CleanupGroups.Count == 0
                ? null
                : CleanupGroups[Math.Min(Math.Max(selectedIndex, 0), CleanupGroups.Count - 1)];
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
