using System.IO;
using CleanC.Core.Services;

namespace CleanC.App.Services;

public sealed record CleanupFailure(string Path, string Message);

public sealed record CleanupRequest(string Path, long ExpectedSize);

public sealed record CleanupResult(IReadOnlyList<string> SucceededPaths, IReadOnlyList<CleanupFailure> Failures);

public sealed class CleanupService
{
    private readonly PathSafety _pathSafety = new();

    public Task<CleanupResult> DeletePermanentlyAsync(IEnumerable<CleanupRequest> requests, CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<CleanupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestList = requests.ToArray();
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(DeletePermanently(requestList, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                completion.SetCanceled(cancellationToken);
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "CleanC permanent-delete worker"
        };
        thread.Start();
        return completion.Task;
    }

    private CleanupResult DeletePermanently(IEnumerable<CleanupRequest> requests, CancellationToken cancellationToken)
    {
        var succeeded = new List<string>();
        var failures = new List<CleanupFailure>();
        foreach (var request in requests.DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rawPath = request.Path;
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
                if (new FileInfo(path).Length != request.ExpectedSize)
                {
                    failures.Add(new CleanupFailure(path, "文件大小与 CSV 导出时不同，可能已经被修改或替换"));
                    continue;
                }
                if (!CanAcquireExclusiveAccess(path, out var accessFailure))
                {
                    failures.Add(new CleanupFailure(path, accessFailure));
                    continue;
                }

                File.Delete(path);
                if (File.Exists(path))
                {
                    failures.Add(new CleanupFailure(path, "Windows 未能删除该文件，文件可能在检查后被占用"));
                    continue;
                }

                succeeded.Add(path);
            }
            catch (Exception exception)
            {
                failures.Add(new CleanupFailure(rawPath, exception.Message));
            }
        }

        return new CleanupResult(succeeded, failures);
    }

    private static bool CanAcquireExclusiveAccess(string path, out string failure)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 1, FileOptions.None);
            failure = string.Empty;
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            failure = "没有足够权限或应用禁止访问，已跳过";
            return false;
        }
        catch (IOException)
        {
            failure = "文件正在被其他应用使用，已跳过";
            return false;
        }
    }
}
