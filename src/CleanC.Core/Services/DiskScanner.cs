using System.Diagnostics;
using CleanC.Core.Models;

namespace CleanC.Core.Services;

public sealed class DiskScanner
{
    private const int LargestFileLimit = 2_000;
    private const int CleanupCandidateLimitPerGroup = 50_000;
    private const int ErrorLimit = 1_000;
    private readonly CleanupRuleEngine _ruleEngine;

    public DiskScanner(CleanupRuleEngine? ruleEngine = null)
    {
        _ruleEngine = ruleEngine ?? new CleanupRuleEngine();
    }

    public Task<ScanResult> ScanAsync(
        string rootPath,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(rootPath, progress, cancellationToken), cancellationToken);

    private ScanResult Scan(string rootPath, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"扫描路径不存在：{root}");
        }

        var stopwatch = Stopwatch.StartNew();
        var now = DateTime.UtcNow;
        var rootNode = CreateNode(root);
        var allDirectories = new List<DirectoryEntry>();
        var largestFiles = new PriorityQueue<FileEntry, long>();
        var cleanupGroups = new Dictionary<string, CleanupGroup>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<ScanError>();
        var stack = new Stack<ScanTask>();
        long scannedFiles = 0;
        long scannedDirectories = 0;
        long discoveredBytes = 0;
        var lastProgress = TimeSpan.Zero;

        stack.Push(new ScanTask(rootNode, null, false));
        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var task = stack.Pop();

            if (task.IsExit)
            {
                allDirectories.Add(task.Node);
                if (task.Parent is not null)
                {
                    task.Parent.Size += task.Node.Size;
                    task.Parent.FileCount += task.Node.FileCount;
                    task.Parent.DirectoryCount += task.Node.DirectoryCount + 1;
                }
                continue;
            }

            scannedDirectories++;
            stack.Push(task with { IsExit = true });
            var directory = new DirectoryInfo(task.Node.FullPath);
            List<FileSystemInfo> entries;

            try
            {
                entries = directory.EnumerateFileSystemInfos("*", new EnumerationOptions
                {
                    AttributesToSkip = 0,
                    IgnoreInaccessible = false,
                    RecurseSubdirectories = false,
                    ReturnSpecialDirectories = false
                }).ToList();
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                task.Node.IsInaccessible = true;
                AddError(errors, task.Node.FullPath, exception);
                entries = [];
            }

            for (var index = entries.Count - 1; index >= 0; index--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entry = entries[index];
                try
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    if ((entry.Attributes & FileAttributes.Directory) != 0)
                    {
                        var child = CreateNode(entry.FullName);
                        task.Node.Children.Add(child);
                        stack.Push(new ScanTask(child, task.Node, false));
                        continue;
                    }

                    if (entry is not FileInfo fileInfo)
                    {
                        fileInfo = new FileInfo(entry.FullName);
                    }

                    var file = new FileEntry(fileInfo.Name, fileInfo.FullName, fileInfo.Length, fileInfo.LastWriteTimeUtc);
                    task.Node.Size += file.Size;
                    task.Node.DirectFileSize += file.Size;
                    task.Node.FileCount++;
                    scannedFiles++;
                    discoveredBytes += file.Size;
                    AddLargestFile(largestFiles, file);
                    AddCleanupCandidate(cleanupGroups, _ruleEngine.Classify(file, now));
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                {
                    AddError(errors, entry.FullName, exception);
                }
            }

            if (stopwatch.Elapsed - lastProgress >= TimeSpan.FromMilliseconds(150))
            {
                lastProgress = stopwatch.Elapsed;
                progress?.Report(new ScanProgress(task.Node.FullPath, scannedFiles, scannedDirectories, discoveredBytes, stopwatch.Elapsed));
            }
        }

        stopwatch.Stop();
        rootNode.Children.Sort((left, right) => right.Size.CompareTo(left.Size));
        SortTree(rootNode);

        return new ScanResult
        {
            RootPath = root,
            RootDirectory = rootNode,
            LargestDirectories = allDirectories.OrderByDescending(directory => directory.Size).Take(2_000).ToArray(),
            LargestFiles = largestFiles.UnorderedItems.Select(item => item.Element).OrderByDescending(file => file.Size).ToArray(),
            CleanupGroups = cleanupGroups.Values
                .OrderBy(group => group.Level)
                .ThenBy(group => CleanupGroupPriority.Get(group.Id))
                .ThenByDescending(group => group.TotalSize)
                .ToArray(),
            Errors = errors,
            Elapsed = stopwatch.Elapsed
        };
    }

    private static DirectoryEntry CreateNode(string path) => new()
    {
        Name = new DirectoryInfo(path).Name is { Length: > 0 } name ? name : path,
        FullPath = path
    };

    private static void AddLargestFile(PriorityQueue<FileEntry, long> queue, FileEntry file)
    {
        if (queue.Count < LargestFileLimit)
        {
            queue.Enqueue(file, file.Size);
            return;
        }

        queue.TryPeek(out _, out var smallestSize);
        if (file.Size > smallestSize)
        {
            queue.Dequeue();
            queue.Enqueue(file, file.Size);
        }
    }

    private static void AddCleanupCandidate(Dictionary<string, CleanupGroup> groups, CleanupCandidate? candidate)
    {
        if (candidate is null)
        {
            return;
        }

        if (!groups.TryGetValue(candidate.GroupId, out var group))
        {
            group = new CleanupGroup
            {
                Id = candidate.GroupId,
                Title = candidate.Title,
                Reason = candidate.Reason,
                Level = candidate.Level
            };
            groups.Add(group.Id, group);
        }

        group.TotalSize += candidate.File.Size;
        group.TotalCount++;
        if (group.Candidates.Count < CleanupCandidateLimitPerGroup)
        {
            group.Candidates.Add(candidate);
        }
        else
        {
            group.IsTruncated = true;
        }
    }

    private static void AddError(List<ScanError> errors, string path, Exception exception)
    {
        if (errors.Count < ErrorLimit)
        {
            errors.Add(new ScanError(path, exception.Message));
        }
    }

    private static void SortTree(DirectoryEntry root)
    {
        var stack = new Stack<DirectoryEntry>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            node.Children.Sort((left, right) => right.Size.CompareTo(left.Size));
            foreach (var child in node.Children)
            {
                stack.Push(child);
            }
        }
    }

    private sealed record ScanTask(DirectoryEntry Node, DirectoryEntry? Parent, bool IsExit);
}
