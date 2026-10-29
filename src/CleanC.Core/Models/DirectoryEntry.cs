namespace CleanC.Core.Models;

public sealed class DirectoryEntry
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public long Size { get; internal set; }
    public long DirectFileSize { get; internal set; }
    public int FileCount { get; internal set; }
    public int DirectoryCount { get; internal set; }
    public bool IsInaccessible { get; internal set; }
    public List<DirectoryEntry> Children { get; } = [];
}
