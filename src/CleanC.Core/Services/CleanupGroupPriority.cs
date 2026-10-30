namespace CleanC.Core.Services;

public static class CleanupGroupPriority
{
    public static int Get(string groupId) => groupId switch
    {
        "user-temp" => 0,
        "windows-temp" => 1,
        "shader-cache" => 2,
        "browser-cache" => 3,
        "application-cache" => 4,
        "thumbnail-cache" => 5,
        "stale-install-staging" => 6,
        "crash-dumps" => 7,
        "diagnostic-logs" => 8,
        "old-installers" => 20,
        "application-update-cache" => 21,
        "developer-cache" => 22,
        "browser-models" => 23,
        "system-crash-dumps" => 24,
        "old-downloads" => 30,
        "large-files" => 31,
        "personal-documents" => 40,
        "chat-data" => 41,
        "personal-media" => 42,
        _ => 100
    };
}
