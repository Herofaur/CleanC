using System.Collections.ObjectModel;
using System.IO;
using CleanC.App.Infrastructure;
using CleanC.Core.Formatting;
using CleanC.Core.Models;

namespace CleanC.App.ViewModels;

public sealed class CleanupGroupViewModel : ObservableObject
{
    private long _totalSize;
    private int _totalCount;

    public CleanupGroupViewModel(CleanupGroup group)
    {
        Group = group;
        _totalSize = group.TotalSize;
        _totalCount = group.TotalCount;
        Candidates = new ObservableCollection<CleanupCandidateViewModel>(
            group.Candidates.OrderByDescending(candidate => candidate.File.Size).Select(candidate => new CleanupCandidateViewModel(candidate)));
        Buckets = new ObservableCollection<CleanupBucketViewModel>(CreateBuckets(Candidates));
    }

    public CleanupGroup Group { get; }
    public string Id => Group.Id;
    public string Title => Group.Title;
    public string Reason => Group.Reason;
    public CleanupLevel Level => Group.Level;
    public long TotalSize => _totalSize;
    public int TotalCount => _totalCount;
    public bool IsTruncated => Group.IsTruncated;
    public ObservableCollection<CleanupCandidateViewModel> Candidates { get; }
    public ObservableCollection<CleanupBucketViewModel> Buckets { get; }
    public string Summary => $"{TotalCount:N0} 个文件 · {SizeFormatter.Format(TotalSize)}";
    public long SelectedSize => Candidates.Where(item => item.IsSelected).Sum(item => item.Size);
    public int SelectedCount => Candidates.Count(item => item.IsSelected);
    public string SelectedSummary => $"已选择 {SelectedCount:N0} 个文件，共 {SizeFormatter.Format(SelectedSize)}";

    public void SelectAll(bool selected)
    {
        foreach (var bucket in Buckets)
        {
            bucket.IsSelected = selected;
        }
        RaiseSelectionChanged();
    }

    public void RemovePaths(ISet<string> removedPaths)
    {
        for (var index = Candidates.Count - 1; index >= 0; index--)
        {
            var candidate = Candidates[index];
            if (!removedPaths.Contains(candidate.FullPath))
            {
                continue;
            }

            _totalSize -= candidate.Size;
            _totalCount--;
            Group.Candidates.Remove(candidate.Candidate);
            Candidates.RemoveAt(index);
        }

        for (var index = Buckets.Count - 1; index >= 0; index--)
        {
            if (Buckets[index].RemovePaths(removedPaths))
            {
                Buckets.RemoveAt(index);
            }
        }

        OnPropertyChanged(nameof(TotalSize));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(Summary));
        RaiseSelectionChanged();
    }

    private IEnumerable<CleanupBucketViewModel> CreateBuckets(IEnumerable<CleanupCandidateViewModel> candidates)
    {
        return candidates
            .Select(candidate => (Candidate: candidate, Descriptor: ResolveBucket(candidate)))
            .GroupBy(item => item.Descriptor.Key, StringComparer.OrdinalIgnoreCase)
            .Select(grouping =>
            {
                var descriptor = grouping.First().Descriptor;
                return new CleanupBucketViewModel(
                    descriptor.Key,
                    descriptor.Title,
                    descriptor.LocationPath,
                    grouping.Select(item => item.Candidate),
                    RaiseSelectionChanged);
            })
            .OrderByDescending(bucket => bucket.TotalSize)
            .ThenBy(bucket => bucket.Title, StringComparer.CurrentCultureIgnoreCase);
    }

    private static BucketDescriptor ResolveBucket(CleanupCandidateViewModel candidate)
    {
        var path = candidate.FullPath.Replace('/', '\\');
        var normalized = path.ToLowerInvariant();

        if (normalized.Contains("\\programdata\\nvidia corporation\\nvidia app\\updateframework\\ota-artifacts\\", StringComparison.Ordinal))
        {
            return Specific(path, "ota-artifacts", "NVIDIA App 旧更新包");
        }
        if (normalized.Contains("\\programdata\\lghub\\cache\\", StringComparison.Ordinal))
        {
            return Specific(path, "cache", "Logitech G HUB 更新缓存", "lghub");
        }
        if (normalized.Contains("\\optguideondevicemodel\\", StringComparison.Ordinal))
        {
            return Specific(path, "optguideondevicemodel", "Chrome 离线模型");
        }
        if (normalized.Contains("\\codex-runtimes\\codex-runtime-install-", StringComparison.Ordinal))
        {
            var location = FindLocation(path, segment => segment.StartsWith("codex-runtime-install-", StringComparison.OrdinalIgnoreCase));
            return new BucketDescriptor(location, $"Codex 安装暂存 · {Path.GetFileName(location)}", location);
        }

        var cacheLocation = FindLocation(path, IsCacheSegment);
        if (!string.IsNullOrWhiteSpace(cacheLocation))
        {
            var cacheName = Path.GetFileName(cacheLocation);
            var application = DetectApplication(normalized) ?? ParentName(cacheLocation);
            return new BucketDescriptor(cacheLocation, $"{application} · {cacheName}", cacheLocation);
        }

        var crashLocation = FindLocation(path, segment =>
            segment.Equals("CrashDumps", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("Crashes", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("LiveKernelReports", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(crashLocation))
        {
            return new BucketDescriptor(crashLocation, Path.GetFileName(crashLocation), crashLocation);
        }

        var parent = Path.GetDirectoryName(path) ?? path;
        return new BucketDescriptor(parent, ParentName(parent), parent);
    }

    private static BucketDescriptor Specific(string path, string segment, string title, string? requiredParent = null)
    {
        var location = FindLocation(path, value =>
            value.Equals(segment, StringComparison.OrdinalIgnoreCase) &&
            (requiredParent is null || path.Contains($"\\{requiredParent}\\", StringComparison.OrdinalIgnoreCase)));
        return new BucketDescriptor(location, title, location);
    }

    private static string FindLocation(string path, Func<string, bool> predicate)
    {
        var root = Path.GetPathRoot(path) ?? string.Empty;
        var remainder = path[root.Length..];
        var segments = remainder.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var current = root.TrimEnd('\\');
        for (var index = 0; index < segments.Length - 1; index++)
        {
            current = string.IsNullOrEmpty(current) ? segments[index] : Path.Combine(current, segments[index]);
            if (predicate(segments[index]))
            {
                return current;
            }
        }

        return Path.GetDirectoryName(path) ?? path;
    }

    private static bool IsCacheSegment(string segment) =>
        segment.Equals("Cache", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("Caches", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("Code Cache", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("GPUCache", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("CacheStorage", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("cache_storage", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("Cache2", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("WebCache", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("Media Cache", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("D3DSCache", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("DXCache", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("GLCache", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("ComputeCache", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("ShaderCache", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("Shader Cache", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("Temp", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("npm-cache", StringComparison.OrdinalIgnoreCase) ||
        segment.Equals("pnpm-store", StringComparison.OrdinalIgnoreCase);

    private static string? DetectApplication(string path) =>
        path.Contains("\\google\\chrome\\", StringComparison.Ordinal) ? "Chrome" :
        path.Contains("\\microsoft\\edge\\", StringComparison.Ordinal) ? "Edge" :
        path.Contains("\\mozilla\\firefox\\", StringComparison.Ordinal) ? "Firefox" :
        path.Contains("\\nvidia\\", StringComparison.Ordinal) ? "NVIDIA" :
        path.Contains("\\amd\\", StringComparison.Ordinal) ? "AMD" :
        path.Contains("\\steam\\", StringComparison.Ordinal) ? "Steam" :
        path.Contains("\\tencent\\", StringComparison.Ordinal) ? "腾讯应用" :
        path.Contains("\\douyin\\", StringComparison.Ordinal) ? "抖音" : null;

    private static string ParentName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd('\\'));
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedSize));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedSummary));
    }

    private sealed record BucketDescriptor(string Key, string Title, string LocationPath);
}
