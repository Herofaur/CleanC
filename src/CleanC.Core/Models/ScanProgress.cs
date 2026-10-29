namespace CleanC.Core.Models;

public sealed record ScanProgress(
    string CurrentPath,
    long ScannedFiles,
    long ScannedDirectories,
    long DiscoveredBytes,
    TimeSpan Elapsed);
