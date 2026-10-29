namespace CleanC.Core.Models;

public sealed record DriveSpaceInfo(
    string Name,
    string Label,
    long TotalBytes,
    long FreeBytes)
{
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
    public double UsedPercent => TotalBytes <= 0 ? 0 : UsedBytes * 100d / TotalBytes;
}
