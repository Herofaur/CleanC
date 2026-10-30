using CleanC.Core.Formatting;
using CleanC.Core.Models;
using CleanC.Core.Services;

if (args is ["--analyze", var csvPath])
{
    var result = await new WizTreeCsvImporter().ImportAsync(csvPath);
    Console.WriteLine($"{result.FileCount:N0} files, {SizeFormatter.Format(result.TotalSize)}");
    foreach (var group in result.CleanupGroups)
    {
        Console.WriteLine($"{group.Level,-15} {SizeFormatter.Format(group.TotalSize),10} {group.TotalCount,8:N0}  {group.Id}  {group.Title}");
    }
    return;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("SizeFormatter formats binary units", TestSizeFormatter),
    ("PathSafety blocks Windows system paths", TestPathSafety),
    ("Cleanup rules distinguish recommended and confirm items", TestCleanupRules),
    ("WizTree CSV importer rebuilds the tree and cleanup groups", TestWizTreeCsvImporter),
    ("DiskScanner aggregates directories and orders files", TestScanner)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.WriteLine($"FAIL  {test.Name}");
        Console.WriteLine($"      {exception.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed.");
if (failures.Count > 0)
{
    Environment.ExitCode = 1;
}

return;

static Task TestSizeFormatter()
{
    AssertEqual("0 B", SizeFormatter.Format(0));
    AssertEqual("1.00 KB", SizeFormatter.Format(1024));
    AssertEqual("1.00 GB", SizeFormatter.Format(1024L * 1024 * 1024));
    return Task.CompletedTask;
}

static Task TestPathSafety()
{
    var safety = new PathSafety(@"C:\");
    AssertTrue(safety.IsProtected(@"C:\Windows\System32\kernel32.dll"), "System32 should be protected.");
    AssertTrue(safety.IsProtected(@"C:\Program Files\Example\app.exe"), "Program Files should be protected.");
    AssertTrue(safety.IsProtected(@"C:\ProgramData\Example\data.bin"), "ProgramData should remain protected by default.");
    AssertTrue(!safety.IsProtected(@"C:\ProgramData\NVIDIA Corporation\NVIDIA App\UpdateFramework\ota-artifacts\old.exe"),
        "Known NVIDIA update artifacts should be eligible for manual cleanup.");
    AssertTrue(!safety.IsProtected(@"C:\ProgramData\LGHUB\cache\old.pkg"),
        "Known LGHUB cache should be eligible for manual cleanup.");
    AssertTrue(safety.IsProtected(@"C:\ProgramData\LGHUB\depots\old\lghub.exe"),
        "LGHUB depots may contain active application versions and must remain protected.");
    AssertTrue(safety.IsProtected(@"C:\Users\Example\.agents\skills\cleanup-helper\README.md"),
        "Skill documentation must remain protected even when a cleanup rule matches its path.");
    AssertTrue(safety.IsProtected(@"C:\Users\Example\.codex\plugins\cache\plugin\skills\tool\SKILL.md"),
        "Codex plugin and skill runtime files must remain protected.");
    AssertTrue(!safety.IsProtected(@"C:\Users\Example\Downloads\archive.zip"), "User downloads should not be globally protected.");
    return Task.CompletedTask;
}

static Task TestCleanupRules()
{
    var engine = new CleanupRuleEngine();
    var now = DateTime.UtcNow;
    var oldTemp = new FileEntry("old.tmp", Path.Combine(Path.GetTempPath(), "old.tmp"), 1024, now.AddDays(-3));
    var tempCandidate = engine.Classify(oldTemp, now);
    AssertNotNull(tempCandidate, "Old temp file should be classified.");
    AssertEqual(CleanupLevel.Recommended, tempCandidate!.Level);

    var recentTemp = new FileEntry("recent.tmp", Path.Combine(Path.GetTempPath(), "recent.tmp"), 1024, now.AddDays(-1));
    AssertTrue(engine.Classify(recentTemp, now) is null, "Recent temporary files should be left alone.");

    var largeUserFile = new FileEntry(
        "video.mkv",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos", "video.mkv"),
        2L * 1024 * 1024 * 1024,
        now);
    var largeCandidate = engine.Classify(largeUserFile, now);
    AssertNotNull(largeCandidate, "Large user file should be classified.");
    AssertEqual(CleanupLevel.ConfirmRequired, largeCandidate!.Level);

    var shaderCache = new FileEntry(
        "cache.bin",
        @"C:\Users\Example\AppData\Local\D3DSCache\cache.bin",
        4096,
        now.AddDays(-10));
    var shaderCandidate = engine.Classify(shaderCache, now);
    AssertNotNull(shaderCandidate, "Shader cache should be classified.");
    AssertEqual("shader-cache", shaderCandidate!.GroupId);
    AssertEqual(CleanupLevel.Recommended, shaderCandidate.Level);

    var document = new FileEntry(
        "plan.docx",
        @"C:\Users\Example\Documents\plan.docx",
        8192,
        now.AddDays(-20));
    var documentCandidate = engine.Classify(document, now);
    AssertNotNull(documentCandidate, "Personal document should be shown for manual review.");
    AssertEqual("personal-documents", documentCandidate!.GroupId);
    AssertEqual(CleanupLevel.ConfirmRequired, documentCandidate.Level);

    var internalMarkdown = new FileEntry(
        "reference.md",
        @"C:\Users\Example\.cache\tool\reference.md",
        4096,
        now.AddDays(-60));
    AssertTrue(engine.Classify(internalMarkdown, now) is null,
        "Developer tool documentation outside personal libraries should not be treated as a personal document.");

    var skillReadme = new FileEntry(
        "README.md",
        @"C:\Users\Example\AppData\Local\AgentSuite\skills\cleanup-tool\cache\README.md",
        4096,
        now.AddDays(-180));
    AssertTrue(engine.Classify(skillReadme, now) is null,
        "README files inside skill runtimes must never be offered for cleanup, even below a cache directory.");

    var skillResource = new FileEntry(
        "rules.json",
        @"C:\Users\Example\.codex\plugins\cache\cleanup-plugin\skills\cleanup\assets\rules.json",
        4096,
        now.AddDays(-180));
    AssertTrue(engine.Classify(skillResource, now) is null,
        "Skill resources and plugin runtime content must remain intact.");

    var ordinaryApplicationCache = new FileEntry(
        "cache.bin",
        @"C:\Users\Example\AppData\Local\ExampleApp\Cache\cache.bin",
        4096,
        now.AddDays(-30));
    var ordinaryCacheCandidate = engine.Classify(ordinaryApplicationCache, now);
    AssertNotNull(ordinaryCacheCandidate, "Ordinary rebuildable application caches should still be recognized.");
    AssertEqual("application-cache", ordinaryCacheCandidate!.GroupId);

    var staleCodexRuntime = new FileEntry(
        "node-runtime.tar.gz",
        @"C:\Users\Example\.cache\codex-runtimes\codex-runtime-install-old\node-runtime.tar.gz",
        500L * 1024 * 1024,
        now.AddDays(-45));
    var stagingCandidate = engine.Classify(staleCodexRuntime, now);
    AssertNotNull(stagingCandidate, "Old Codex installer staging files should be classified.");
    AssertEqual("stale-install-staging", stagingCandidate!.GroupId);
    AssertEqual(CleanupLevel.Recommended, stagingCandidate.Level);

    var activeCodexRuntime = new FileEntry(
        "node.exe",
        @"C:\Users\Example\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\node.exe",
        100L * 1024 * 1024,
        now.AddDays(-60));
    AssertTrue(engine.Classify(activeCodexRuntime, now) is null,
        "The active Codex primary runtime must never be treated as installer staging.");

    var chromeModel = new FileEntry(
        "weights.bin",
        @"C:\Users\Example\AppData\Local\Google\Chrome\User Data\OptGuideOnDeviceModel\2025.8.8.1141\weights.bin",
        4L * 1024 * 1024 * 1024,
        now.AddDays(-100));
    var modelCandidate = engine.Classify(chromeModel, now);
    AssertNotNull(modelCandidate, "Chrome downloaded models should be shown for manual review.");
    AssertEqual("browser-models", modelCandidate!.GroupId);
    AssertEqual(CleanupLevel.ConfirmRequired, modelCandidate.Level);

    var oldNvidiaPackage = new FileEntry(
        "driver.exe",
        @"C:\ProgramData\NVIDIA Corporation\NVIDIA App\UpdateFramework\ota-artifacts\grd\old\driver.exe",
        800L * 1024 * 1024,
        now.AddDays(-90));
    var updateCandidate = engine.Classify(oldNvidiaPackage, now);
    AssertNotNull(updateCandidate, "Old NVIDIA update packages should be shown for manual review.");
    AssertEqual("application-update-cache", updateCandidate!.GroupId);
    AssertEqual(CleanupLevel.ConfirmRequired, updateCandidate.Level);

    var recentNvidiaPackage = oldNvidiaPackage with
    {
        Name = "recent.exe",
        FullPath = @"C:\ProgramData\NVIDIA Corporation\NVIDIA App\UpdateFramework\ota-artifacts\grd\new\recent.exe",
        Size = 100L * 1024 * 1024,
        LastWriteTimeUtc = now.AddDays(-14)
    };
    AssertTrue(engine.Classify(recentNvidiaPackage, now) is null,
        "Recent NVIDIA update packages should remain available to the updater.");

    var kernelDump = new FileEntry(
        "old.dmp",
        @"C:\Windows\LiveKernelReports\old.dmp",
        1400L * 1024 * 1024,
        now.AddDays(-60));
    var kernelDumpCandidate = engine.Classify(kernelDump, now);
    AssertNotNull(kernelDumpCandidate, "Old Windows kernel dumps should be shown for manual review.");
    AssertEqual("system-crash-dumps", kernelDumpCandidate!.GroupId);
    AssertEqual(CleanupLevel.ConfirmRequired, kernelDumpCandidate.Level);

    var pipCache = new FileEntry(
        "package.whl",
        @"C:\Users\Example\AppData\Local\pip\cache\wheels\package.whl",
        20L * 1024 * 1024,
        now.AddDays(-60));
    var developerCandidate = engine.Classify(pipCache, now);
    AssertNotNull(developerCandidate, "Developer download caches should be shown for manual review.");
    AssertEqual("developer-cache", developerCandidate!.GroupId);
    AssertEqual(CleanupLevel.ConfirmRequired, developerCandidate.Level);
    AssertTrue(CleanupGroupPriority.Get("application-cache") < CleanupGroupPriority.Get("old-installers"),
        "Rebuildable caches should be shown before old installer packages.");
    AssertTrue(CleanupGroupPriority.Get("old-installers") < CleanupGroupPriority.Get("personal-documents"),
        "Installer packages should be shown before personal content while still requiring confirmation.");
    return Task.CompletedTask;
}

static async Task TestWizTreeCsvImporter()
{
    var csvPath = Path.Combine(Path.GetTempPath(), $"CleanC-WizTree-{Guid.NewGuid():N}.csv");
    var modified = DateTime.Now.AddDays(-10).ToString("yyyy-MM-dd HH:mm:ss");
    var csv = string.Join(Environment.NewLine,
    [
        "生成由 WizTree 4.25 2026/10/29 22:23:27 (您可以通过捐赠隐藏此信息)",
        "文件名称,大小,分配,修改时间,属性,文件,文件夹",
        $"\"C:\\\",\"9999\",\"9999\",\"{modified}\",\"D\"",
        $"\"C:\\Users\\Example\\AppData\\Local\\D3DSCache\\shader,one.bin\",\"1024\",\"4096\",\"{modified}\",\"A\"",
        $"\"C:\\Users\\Example\\Documents\\budget.xlsx\",\"2048\",\"4096\",\"{modified}\",\"A\""
    ]);

    await File.WriteAllTextAsync(csvPath, csv);
    try
    {
        var result = await new WizTreeCsvImporter().ImportAsync(csvPath);
        AssertEqual(@"C:\", result.RootPath);
        AssertEqual(3072L, result.TotalSize);
        AssertEqual(2L, result.FileCount);
        AssertEqual("budget.xlsx", result.LargestFiles[0].Name);
        AssertTrue(result.CleanupGroups.Any(group => group.Id == "shader-cache" && group.Level == CleanupLevel.Recommended),
            "Shader cache group should be recommended.");
        AssertTrue(result.CleanupGroups.Any(group => group.Id == "personal-documents" && group.Level == CleanupLevel.ConfirmRequired),
            "Documents should require explicit confirmation.");
    }
    finally
    {
        File.Delete(csvPath);
    }
}

static async Task TestScanner()
{
    var testRoot = Path.Combine(Path.GetTempPath(), $"CleanC-Core-Tests-{Guid.NewGuid():N}");
    var nested = Path.Combine(testRoot, "nested");
    Directory.CreateDirectory(nested);
    try
    {
        var first = Path.Combine(testRoot, "first.bin");
        var second = Path.Combine(nested, "second.bin");
        await File.WriteAllBytesAsync(first, new byte[2048]);
        await File.WriteAllBytesAsync(second, new byte[4096]);
        File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddDays(-3));
        File.SetLastWriteTimeUtc(second, DateTime.UtcNow.AddDays(-3));

        var result = await new DiskScanner().ScanAsync(testRoot);
        AssertEqual(6144L, result.TotalSize);
        AssertEqual(2L, result.FileCount);
        AssertEqual(2L, result.DirectoryCount);
        AssertEqual(second, result.LargestFiles[0].FullPath);
        AssertTrue(result.CleanupGroups.Any(group => group.Id == "user-temp"), "Old temporary files should be recommended.");
    }
    finally
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, true);
        }
    }
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertNotNull(object? value, string message) => AssertTrue(value is not null, message);

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}
