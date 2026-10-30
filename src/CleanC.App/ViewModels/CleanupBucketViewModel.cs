using CleanC.App.Infrastructure;
using CleanC.Core.Formatting;

namespace CleanC.App.ViewModels;

public sealed class CleanupBucketViewModel : ObservableObject
{
    private readonly Action _selectionChanged;
    private bool _isSelected;
    private long _totalSize;

    public CleanupBucketViewModel(
        string key,
        string title,
        string locationPath,
        IEnumerable<CleanupCandidateViewModel> candidates,
        Action selectionChanged)
    {
        Key = key;
        Title = title;
        LocationPath = locationPath;
        Candidates = candidates.OrderByDescending(candidate => candidate.Size).ToList();
        _selectionChanged = selectionChanged;
        _totalSize = Candidates.Sum(candidate => candidate.Size);
        _isSelected = Candidates.Count > 0 && Candidates.All(candidate => candidate.IsSelected);
    }

    public string Key { get; }
    public string Title { get; }
    public string LocationPath { get; }
    public List<CleanupCandidateViewModel> Candidates { get; }
    public int FileCount => Candidates.Count;
    public long TotalSize => _totalSize;
    public string Summary => $"{FileCount:N0} 个文件 · {SizeFormatter.Format(TotalSize)}";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value))
            {
                return;
            }

            foreach (var candidate in Candidates)
            {
                candidate.IsSelected = value;
            }
            _selectionChanged();
        }
    }

    public bool RemovePaths(ISet<string> removedPaths)
    {
        for (var index = Candidates.Count - 1; index >= 0; index--)
        {
            if (!removedPaths.Contains(Candidates[index].FullPath))
            {
                continue;
            }

            _totalSize -= Candidates[index].Size;
            Candidates.RemoveAt(index);
        }

        OnPropertyChanged(nameof(FileCount));
        OnPropertyChanged(nameof(TotalSize));
        OnPropertyChanged(nameof(Summary));
        return Candidates.Count == 0;
    }
}
