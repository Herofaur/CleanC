using CleanC.App.Infrastructure;
using CleanC.Core.Models;

namespace CleanC.App.ViewModels;

public sealed class CleanupCandidateViewModel : ObservableObject
{
    private static readonly HashSet<string> DefaultSelectedGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        "user-temp",
        "windows-temp",
        "shader-cache",
        "browser-cache",
        "application-cache",
        "thumbnail-cache"
    };

    private bool _isSelected;

    public CleanupCandidateViewModel(CleanupCandidate candidate)
    {
        Candidate = candidate;
        _isSelected = candidate.Level == CleanupLevel.Recommended &&
                      DefaultSelectedGroups.Contains(candidate.GroupId);
    }

    public CleanupCandidate Candidate { get; }
    public string Name => Candidate.File.Name;
    public string FullPath => Candidate.File.FullPath;
    public long Size => Candidate.File.Size;
    public DateTime LastWriteTime => Candidate.File.LastWriteTimeUtc.ToLocalTime();
    public CleanupLevel Level => Candidate.Level;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
