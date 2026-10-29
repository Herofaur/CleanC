using System.IO;
using CleanC.Core.Services;
using Microsoft.VisualBasic.FileIO;

namespace CleanC.App.Services;

public sealed record CleanupFailure(string Path, string Message);

public sealed record CleanupResult(IReadOnlyList<string> SucceededPaths, IReadOnlyList<CleanupFailure> Failures);

public sealed class CleanupService
{
    private readonly PathSafety _pathSafety = new();

    public Task<CleanupResult> MoveToRecycleBinAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default) =>
        Task.Run(() => MoveToRecycleBin(paths, cancellationToken), cancellationToken);

    private CleanupResult MoveToRecycleBin(IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        var succeeded = new List<string>();
        var failures = new List<CleanupFailure>();

        foreach (var rawPath in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var path = Path.GetFullPath(rawPath);
                if (_pathSafety.IsProtected(path))
                {
                    failures.Add(new CleanupFailure(path, "受保护的系统路径"));
                    continue;
                }
                if (!File.Exists(path))
                {
                    failures.Add(new CleanupFailure(path, "文件不存在或已经被移动"));
                    continue;
                }
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                {
                    failures.Add(new CleanupFailure(path, "不处理符号链接或重解析点"));
                    continue;
                }

                FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
                succeeded.Add(path);
            }
            catch (Exception exception)
            {
                failures.Add(new CleanupFailure(rawPath, exception.Message));
            }
        }

        return new CleanupResult(succeeded, failures);
    }
}
