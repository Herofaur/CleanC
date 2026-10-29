using CleanC.Core.Formatting;
using CleanC.Core.Models;
using CleanC.Core.Services;

var tests = new (string Name, Func<Task> Run)[]
{
    ("SizeFormatter formats binary units", TestSizeFormatter),
    ("PathSafety blocks Windows system paths", TestPathSafety),
    ("Cleanup rules distinguish recommended and confirm items", TestCleanupRules),
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

    var largeUserFile = new FileEntry(
        "video.mkv",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos", "video.mkv"),
        2L * 1024 * 1024 * 1024,
        now);
    var largeCandidate = engine.Classify(largeUserFile, now);
    AssertNotNull(largeCandidate, "Large user file should be classified.");
    AssertEqual(CleanupLevel.ConfirmRequired, largeCandidate!.Level);
    return Task.CompletedTask;
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
