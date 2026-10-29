namespace CleanC.Core.Models;

public sealed class ScanResult
{
    public required string RootPath { get; init; }
    public required DirectoryEntry RootDirectory { get; init; }
    public required IReadOnlyList<DirectoryEntry> LargestDirectories { get; init; }
    public required IReadOnlyList<FileEntry> LargestFiles { get; init; }
    public required IReadOnlyList<CleanupGroup> CleanupGroups { get; init; }
    public required IReadOnlyList<ScanError> Errors { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public long TotalSize => RootDirectory.Size;
    public long FileCount => RootDirectory.FileCount;
    public long DirectoryCount => RootDirectory.DirectoryCount + 1L;
}
