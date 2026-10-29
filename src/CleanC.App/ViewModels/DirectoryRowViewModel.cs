using CleanC.Core.Models;

namespace CleanC.App.ViewModels;

public sealed class DirectoryRowViewModel
{
    public DirectoryRowViewModel(DirectoryEntry directory, long rootSize)
    {
        Directory = directory;
        SharePercent = rootSize <= 0 ? 0 : directory.Size * 100d / rootSize;
    }

    public DirectoryEntry Directory { get; }
    public string Name => Directory.Name;
    public string FullPath => Directory.FullPath;
    public long Size => Directory.Size;
    public int FileCount => Directory.FileCount;
    public int DirectoryCount => Directory.DirectoryCount;
    public double SharePercent { get; }
}
