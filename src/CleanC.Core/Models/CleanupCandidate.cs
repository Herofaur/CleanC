namespace CleanC.Core.Models;

public sealed record CleanupCandidate(
    FileEntry File,
    string GroupId,
    string Title,
    string Reason,
    CleanupLevel Level);
