using System.IO;
using System.Windows;
using System.Windows.Interop;
using CleanC.App;
using CleanC.App.Services;
using CleanC.App.ViewModels;
using CleanC.Core.Models;

namespace CleanC.App.SmokeTests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--cleanup-service", StringComparer.OrdinalIgnoreCase))
            {
                TestCleanupService().GetAwaiter().GetResult();
                Console.WriteLine("PASS  Cleanup service permanently deleted an available file and skipped a locked file.");
                return 0;
            }

            TestGroupedSelection();
            Console.WriteLine("PASS  Cleanup candidates are selected and maintained by source/directory bucket.");

            var application = new App
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            application.InitializeComponent();
            var window = new MainWindow();
            application.MainWindow = window;
            window.Show();
            window.Measure(new Size(1440, 900));
            window.Arrange(new Rect(0, 0, 1440, 900));
            window.UpdateLayout();

            if (window.DataContext is not MainViewModel mainViewModel)
            {
                throw new InvalidOperationException("MainWindow DataContext was not initialized.");
            }

            var bindingGroup = new CleanupGroup
            {
                Id = "binding-test",
                Title = "绑定测试",
                Reason = "test",
                Level = CleanupLevel.Recommended
            };
            bindingGroup.Candidates.Add(CreateCandidate(
                @"C:\Users\Example\AppData\Local\D3DSCache\binding-test.bin",
                1024,
                CleanupLevel.Recommended,
                "shader-cache"));
            mainViewModel.CleanupGroups.Add(new CleanupGroupViewModel(bindingGroup));
            mainViewModel.SelectedCleanupGroup = mainViewModel.CleanupGroups[0];
            window.UpdateLayout();
            if (!window.Title.Contains("CleanC", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("MainWindow title was not initialized.");
            }
            if (!window.IsVisible || new WindowInteropHelper(window).Handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("MainWindow did not create a visible native window.");
            }

            window.Close();
            application.Shutdown();
            Console.WriteLine("PASS  MainWindow XAML loaded and completed layout.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL  MainWindow smoke test");
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void TestGroupedSelection()
    {
        var recommendedGroup = new CleanupGroup
        {
            Id = "shader-cache",
            Title = "着色器与显卡缓存",
            Reason = "test",
            Level = CleanupLevel.Recommended
        };
        recommendedGroup.Candidates.Add(CreateCandidate(
            @"C:\Users\Example\AppData\Local\D3DSCache\first.bin",
            1024,
            CleanupLevel.Recommended,
            "shader-cache"));
        recommendedGroup.Candidates.Add(CreateCandidate(
            @"C:\Users\Example\AppData\Local\D3DSCache\nested\second.bin",
            2048,
            CleanupLevel.Recommended,
            "shader-cache"));

        var recommended = new CleanupGroupViewModel(recommendedGroup);
        Assert(recommended.Buckets.Count == 1, "Files below one cache root should form one bucket.");
        Assert(recommended.Buckets[0].IsSelected, "Safest rebuildable cache buckets should be selected by default.");
        Assert(recommended.SelectedCount == 2, "Both files in the safe cache bucket should be selected.");

        recommended.Buckets[0].IsSelected = false;
        Assert(recommended.Candidates.All(candidate => !candidate.IsSelected),
            "Clearing a bucket should clear every contained candidate.");

        recommended.Buckets[0].IsSelected = true;
        var firstPath = recommended.Candidates[0].FullPath;
        recommended.RemovePaths(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { firstPath });
        Assert(recommended.Buckets.Count == 1 && recommended.Buckets[0].FileCount == 1,
            "A partially cleaned bucket should retain only remaining files.");

        var remainingPath = recommended.Candidates[0].FullPath;
        recommended.RemovePaths(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { remainingPath });
        Assert(recommended.Buckets.Count == 0, "An empty bucket should be removed after cleanup.");

        var confirmGroup = new CleanupGroup
        {
            Id = "personal-documents",
            Title = "个人文档",
            Reason = "test",
            Level = CleanupLevel.ConfirmRequired
        };
        confirmGroup.Candidates.Add(CreateCandidate(
            @"C:\Users\Example\Documents\budget.xlsx",
            4096,
            CleanupLevel.ConfirmRequired));
        var confirm = new CleanupGroupViewModel(confirmGroup);
        Assert(!confirm.Buckets[0].IsSelected && confirm.SelectedCount == 0,
            "Personal and confirm-required buckets must not be selected by default.");

        var cautiousRecommendedGroup = new CleanupGroup
        {
            Id = "stale-install-staging",
            Title = "残留安装暂存文件",
            Reason = "test",
            Level = CleanupLevel.Recommended
        };
        cautiousRecommendedGroup.Candidates.Add(CreateCandidate(
            @"C:\Users\Example\.cache\codex-runtimes\codex-runtime-install-old\runtime.tar.gz",
            8192,
            CleanupLevel.Recommended,
            "stale-install-staging"));
        var cautiousRecommended = new CleanupGroupViewModel(cautiousRecommendedGroup);
        Assert(!cautiousRecommended.Buckets[0].IsSelected,
            "Recommended categories that are less obviously disposable must remain opt-in.");

        var mainViewModel = new MainViewModel();
        mainViewModel.CleanupGroups.Add(confirm);
        mainViewModel.CleanupGroups.Add(cautiousRecommended);
        mainViewModel.SelectedCleanupGroup = confirm;
        mainViewModel.RemoveProcessedCleanupItems(confirm.Candidates.Select(candidate => candidate.FullPath));
        Assert(mainViewModel.CleanupGroups.Count == 1 &&
               ReferenceEquals(mainViewModel.CleanupGroups[0], cautiousRecommended),
            "A fully processed cleanup category should disappear from the list.");
        Assert(ReferenceEquals(mainViewModel.SelectedCleanupGroup, cautiousRecommended),
            "Selection should move to the next remaining category after the current category disappears.");
    }

    private static async Task TestCleanupService()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), $"CleanC-Delete-Test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testRoot);
        var availablePath = Path.Combine(testRoot, "available.tmp");
        var lockedPath = Path.Combine(testRoot, "locked.tmp");
        var skillDirectory = Path.Combine(testRoot, "skills", "cleanup-helper");
        var protectedSkillReadme = Path.Combine(skillDirectory, "README.md");
        Directory.CreateDirectory(skillDirectory);
        await File.WriteAllBytesAsync(availablePath, new byte[32]);
        await File.WriteAllBytesAsync(lockedPath, new byte[64]);
        await File.WriteAllTextAsync(protectedSkillReadme, "Skill documentation must remain intact.");

        try
        {
            CleanupResult result;
            using (var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                result = await new CleanupService().DeletePermanentlyAsync(
                [
                    new CleanupRequest(availablePath, 32),
                    new CleanupRequest(lockedPath, 64),
                    new CleanupRequest(protectedSkillReadme, new FileInfo(protectedSkillReadme).Length)
                ]);
            }

            Assert(result.SucceededPaths.Contains(availablePath, StringComparer.OrdinalIgnoreCase),
                "The available test file should be permanently deleted.");
            Assert(!File.Exists(availablePath), "The available test file should no longer exist at its original path.");
            Assert(File.Exists(lockedPath), "The locked test file should remain untouched.");
            Assert(result.Failures.Any(failure =>
                    failure.Path.Equals(lockedPath, StringComparison.OrdinalIgnoreCase) &&
                    failure.Message.Contains("正在被其他应用使用", StringComparison.Ordinal)),
                "The locked file should be reported as skipped without invoking system error UI.");
            Assert(File.Exists(protectedSkillReadme), "Skill README files must be blocked by the final deletion safety check.");
            Assert(result.Failures.Any(failure =>
                    failure.Path.Equals(protectedSkillReadme, StringComparison.OrdinalIgnoreCase) &&
                    failure.Message.Contains("受保护", StringComparison.Ordinal)),
                "Protected skill content should be reported instead of deleted.");
        }
        finally
        {
            if (File.Exists(availablePath))
            {
                File.Delete(availablePath);
            }
            if (File.Exists(lockedPath))
            {
                File.Delete(lockedPath);
            }
            if (File.Exists(protectedSkillReadme))
            {
                File.Delete(protectedSkillReadme);
            }
            if (Directory.Exists(skillDirectory))
            {
                Directory.Delete(skillDirectory, false);
            }
            var skillsRoot = Path.Combine(testRoot, "skills");
            if (Directory.Exists(skillsRoot))
            {
                Directory.Delete(skillsRoot, false);
            }
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, false);
            }
        }
    }

    private static CleanupCandidate CreateCandidate(string path, long size, CleanupLevel level, string groupId = "test")
    {
        var file = new FileEntry(Path.GetFileName(path), path, size, DateTime.UtcNow.AddDays(-30));
        return new CleanupCandidate(file, groupId, "test", "test", level);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
