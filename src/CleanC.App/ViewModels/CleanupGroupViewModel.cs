using System.Collections.ObjectModel;
using System.ComponentModel;
using CleanC.App.Infrastructure;
using CleanC.Core.Formatting;
using CleanC.Core.Models;

namespace CleanC.App.ViewModels;

public sealed class CleanupGroupViewModel : ObservableObject
{
    private long _totalSize;
    private int _totalCount;

    public CleanupGroupViewModel(CleanupGroup group)
    {
        Group = group;
        _totalSize = group.TotalSize;
        _totalCount = group.TotalCount;
        Candidates = new ObservableCollection<CleanupCandidateViewModel>(
            group.Candidates.OrderByDescending(candidate => candidate.File.Size).Select(candidate => new CleanupCandidateViewModel(candidate)));
        foreach (var candidate in Candidates)
        {
            candidate.PropertyChanged += CandidateOnPropertyChanged;
        }
    }

    public CleanupGroup Group { get; }
    public string Id => Group.Id;
    public string Title => Group.Title;
    public string Reason => Group.Reason;
    public CleanupLevel Level => Group.Level;
    public long TotalSize => _totalSize;
    public int TotalCount => _totalCount;
    public bool IsTruncated => Group.IsTruncated;
    public ObservableCollection<CleanupCandidateViewModel> Candidates { get; }
    public string Summary => $"{TotalCount:N0} 个文件 · {SizeFormatter.Format(TotalSize)}";
    public long SelectedSize => Candidates.Where(item => item.IsSelected).Sum(item => item.Size);
    public int SelectedCount => Candidates.Count(item => item.IsSelected);
    public string SelectedSummary => $"已选择 {SelectedCount:N0} 个文件，共 {SizeFormatter.Format(SelectedSize)}";

    public void SelectAll(bool selected)
    {
        foreach (var candidate in Candidates)
        {
            candidate.IsSelected = selected;
        }
        RaiseSelectionChanged();
    }

    public void RemovePaths(ISet<string> removedPaths)
    {
        for (var index = Candidates.Count - 1; index >= 0; index--)
        {
            var candidate = Candidates[index];
            if (!removedPaths.Contains(candidate.FullPath))
            {
                continue;
            }

            candidate.PropertyChanged -= CandidateOnPropertyChanged;
            _totalSize -= candidate.Size;
            _totalCount--;
            Group.Candidates.Remove(candidate.Candidate);
            Candidates.RemoveAt(index);
        }

        OnPropertyChanged(nameof(TotalSize));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(Summary));
        RaiseSelectionChanged();
    }

    private void CandidateOnPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(CleanupCandidateViewModel.IsSelected))
        {
            RaiseSelectionChanged();
        }
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedSize));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedSummary));
    }
}
