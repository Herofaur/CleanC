namespace CleanC.Core.Models;

public sealed class CleanupGroup
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Reason { get; init; }
    public required CleanupLevel Level { get; init; }
    public long TotalSize { get; internal set; }
    public int TotalCount { get; internal set; }
    public bool IsTruncated { get; internal set; }
    public List<CleanupCandidate> Candidates { get; } = [];
}
