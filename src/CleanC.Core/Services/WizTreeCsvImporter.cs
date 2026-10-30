using System.Diagnostics;
using System.Globalization;
using System.Text;
using CleanC.Core.Models;

namespace CleanC.Core.Services;

/// <summary>
/// Imports the standard WizTree CSV export without touching the file system while analysing it.
/// The CSV may contain both file and directory rows; directory totals are rebuilt from file rows
/// so that files are never counted twice.
/// </summary>
public sealed class WizTreeCsvImporter
{
    private const int LargestFileLimit = 2_000;
    private const int CleanupCandidateLimitPerGroup = 50_000;
    private const int ErrorLimit = 1_000;
    private readonly CleanupRuleEngine _ruleEngine;

    public WizTreeCsvImporter(CleanupRuleEngine? ruleEngine = null)
    {
        _ruleEngine = ruleEngine ?? new CleanupRuleEngine();
    }

    public Task<ScanResult> ImportAsync(
        string csvPath,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Import(csvPath, progress, cancellationToken), cancellationToken);

    private ScanResult Import(string csvPath, IProgress<ScanProgress>? progress, CancellationToken cancellationToken)
    {
        var sourcePath = Path.GetFullPath(csvPath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("找不到 WizTree CSV 文件。", sourcePath);
        }

        var stopwatch = Stopwatch.StartNew();
        var now = DateTime.UtcNow;
        var largestFiles = new PriorityQueue<FileEntry, long>();
        var cleanupGroups = new Dictionary<string, CleanupGroup>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<ScanError>();
        var directories = new Dictionary<string, DirectoryEntry>(StringComparer.OrdinalIgnoreCase);
        DirectoryEntry? rootNode = null;
        string? rootPath = null;
        long fileCount = 0;
        long discoveredBytes = 0;
        var parsedRows = 0L;
        var lastProgress = TimeSpan.Zero;

        using var reader = new StreamReader(sourcePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var headerFound = false;
        var preambleRows = 0;
        CsvColumns columns = default;
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = ParseCsvLine(line);
            if (!headerFound)
            {
                preambleRows++;
                if (CsvColumns.TryFromHeaders(fields, out columns))
                {
                    headerFound = true;
                }
                else if (preambleRows >= 20)
                {
                    throw new InvalidDataException("不是可识别的 WizTree CSV：前 20 行中未找到 File Name（文件名称）表头。");
                }
                continue;
            }

            parsedRows++;
            try
            {
                var rawPath = GetField(fields, columns.Path);
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(rawPath.Trim());
                var itemRoot = Path.GetPathRoot(fullPath);
                if (string.IsNullOrWhiteSpace(itemRoot))
                {
                    AddError(errors, rawPath, "无法识别文件所在磁盘。");
                    continue;
                }

                rootPath ??= itemRoot;
                if (!itemRoot.Equals(rootPath, StringComparison.OrdinalIgnoreCase))
                {
                    AddError(errors, fullPath, $"该项目位于 {itemRoot}，与 CSV 的主磁盘 {rootPath} 不同，已跳过。");
                    continue;
                }

                rootNode ??= EnsureDirectory(directories, rootPath, rootPath);
                var attributes = GetField(fields, columns.Attributes);
                var isDirectory = rawPath.EndsWith('\\') || rawPath.EndsWith('/') ||
                                  attributes.Contains('D', StringComparison.OrdinalIgnoreCase);
                if (isDirectory)
                {
                    EnsureDirectory(directories, fullPath, rootPath);
                    continue;
                }

                var size = ParseSize(GetField(fields, columns.Size));
                var modifiedUtc = ParseModifiedUtc(GetField(fields, columns.Modified), now);
                var file = new FileEntry(Path.GetFileName(fullPath), fullPath, size, modifiedUtc);
                var parentPath = Path.GetDirectoryName(fullPath) ?? rootPath;
                var parent = EnsureDirectory(directories, parentPath, rootPath);
                parent.DirectFileSize += size;
                parent.Size += size;
                parent.FileCount++;
                fileCount++;
                discoveredBytes += size;

                AddLargestFile(largestFiles, file);
                AddCleanupCandidate(cleanupGroups, _ruleEngine.Classify(file, now));
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException or NotSupportedException or PathTooLongException)
            {
                AddError(errors, GetField(fields, columns.Path), exception.Message);
            }

            if (stopwatch.Elapsed - lastProgress >= TimeSpan.FromMilliseconds(150))
            {
                lastProgress = stopwatch.Elapsed;
                progress?.Report(new ScanProgress($"正在读取 CSV 第 {parsedRows:N0} 行", fileCount, directories.Count, discoveredBytes, stopwatch.Elapsed));
            }
        }

        if (!headerFound)
        {
            throw new InvalidDataException("CSV 文件为空，或没有可识别的 WizTree 表头。请在 WizTree 中重新导出文件列表。 ");
        }
        if (rootNode is null || rootPath is null)
        {
            throw new InvalidDataException("CSV 中没有可识别的绝对文件路径。请使用 WizTree 的标准 CSV 导出格式。 ");
        }

        AggregateDirectories(rootNode);
        SortTree(rootNode);
        stopwatch.Stop();

        return new ScanResult
        {
            RootPath = rootPath,
            RootDirectory = rootNode,
            LargestDirectories = directories.Values.OrderByDescending(directory => directory.Size).Take(2_000).ToArray(),
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

    private static DirectoryEntry EnsureDirectory(Dictionary<string, DirectoryEntry> directories, string path, string rootPath)
    {
        var normalizedRoot = Path.GetFullPath(rootPath);
        var normalized = NormalizeDirectory(path);
        if (directories.TryGetValue(normalized, out var existing))
        {
            return existing;
        }

        var missing = new Stack<string>();
        var current = normalized;
        while (!directories.ContainsKey(current))
        {
            missing.Push(current);
            if (current.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = NormalizeDirectory(Path.GetDirectoryName(current) ?? normalizedRoot);
        }

        DirectoryEntry? parent = directories.GetValueOrDefault(current);
        while (missing.Count > 0)
        {
            var directoryPath = missing.Pop();
            var node = new DirectoryEntry
            {
                Name = directoryPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                    ? normalizedRoot
                    : Path.GetFileName(directoryPath),
                FullPath = directoryPath
            };
            directories[directoryPath] = node;
            if (parent is not null)
            {
                parent.Children.Add(node);
            }
            parent = node;
        }

        return directories[normalized];
    }

    private static string NormalizeDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static void AggregateDirectories(DirectoryEntry root)
    {
        var stack = new Stack<(DirectoryEntry Node, bool IsExit)>();
        stack.Push((root, false));
        while (stack.Count > 0)
        {
            var (node, isExit) = stack.Pop();
            if (!isExit)
            {
                stack.Push((node, true));
                foreach (var child in node.Children)
                {
                    stack.Push((child, false));
                }
                continue;
            }

            foreach (var child in node.Children)
            {
                node.Size += child.Size;
                node.FileCount += child.FileCount;
                node.DirectoryCount += child.DirectoryCount + 1;
            }
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

    private static long ParseSize(string value) =>
        long.TryParse(value.Trim(), NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var size)
            ? Math.Max(0, size)
            : 0;

    private static DateTime ParseModifiedUtc(string value, DateTime fallbackUtc)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallbackUtc;
        }

        var styles = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal;
        return DateTime.TryParse(value, CultureInfo.CurrentCulture, styles, out var local) ||
               DateTime.TryParse(value, CultureInfo.InvariantCulture, styles, out local)
            ? local.ToUniversalTime()
            : fallbackUtc;
    }

    private static string GetField(string[] fields, int index) =>
        index >= 0 && index < fields.Length ? fields[index] : string.Empty;

    internal static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var value = new StringBuilder();
        var insideQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (insideQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    value.Append('"');
                    index++;
                }
                else
                {
                    insideQuotes = !insideQuotes;
                }
            }
            else if (character == ',' && !insideQuotes)
            {
                fields.Add(value.ToString());
                value.Clear();
            }
            else
            {
                value.Append(character);
            }
        }

        fields.Add(value.ToString());
        return fields.ToArray();
    }

    private static void AddError(List<ScanError> errors, string path, string message)
    {
        if (errors.Count < ErrorLimit)
        {
            errors.Add(new ScanError(path, message));
        }
    }

    private readonly record struct CsvColumns(int Path, int Size, int Modified, int Attributes)
    {
        public static bool TryFromHeaders(string[] headers, out CsvColumns columns)
        {
            var normalized = headers.Select(header => header.Trim().TrimStart('\uFEFF').ToLowerInvariant()).ToArray();
            var path = Find(normalized, "file name", "filename", "full path", "path", "文件名", "文件名称", "完整路径", "路径");
            var size = Find(normalized, "size", "文件大小", "大小");
            var modified = Find(normalized, "modified", "date modified", "last modified", "修改时间", "修改日期");
            var attributes = Find(normalized, "attributes", "attribute", "属性");

            if (path < 0)
            {
                columns = default;
                return false;
            }

            columns = new CsvColumns(path, size >= 0 ? size : 1, modified >= 0 ? modified : 3, attributes >= 0 ? attributes : 4);
            return true;
        }

        private static int Find(IReadOnlyList<string> headers, params string[] names)
        {
            for (var index = 0; index < headers.Count; index++)
            {
                if (names.Contains(headers[index], StringComparer.OrdinalIgnoreCase))
                {
                    return index;
                }
            }
            return -1;
        }
    }
}
