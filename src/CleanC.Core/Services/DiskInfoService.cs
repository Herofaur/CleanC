using CleanC.Core.Models;

namespace CleanC.Core.Services;

public static class DiskInfoService
{
    public static DriveSpaceInfo? GetForPath(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var drive = new DriveInfo(root);
        if (!drive.IsReady)
        {
            return null;
        }

        return new DriveSpaceInfo(drive.Name, drive.VolumeLabel, drive.TotalSize, drive.AvailableFreeSpace);
    }
}
