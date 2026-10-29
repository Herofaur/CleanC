namespace CleanC.Core.Models;

public sealed record FileEntry(
    string Name,
    string FullPath,
    long Size,
    DateTime LastWriteTimeUtc);
