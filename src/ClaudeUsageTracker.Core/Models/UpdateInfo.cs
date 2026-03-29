namespace ClaudeUsageTracker.Core.Models;

public class UpdateInfo
{
    public Version CurrentVersion { get; init; } = new(0, 0, 0);
    public Version LatestVersion  { get; init; } = new(0, 0, 0);
    public string TagName         { get; init; } = "";
    public string DownloadUrl     { get; init; } = "";
    public string Sha256          { get; init; } = "";
    public string ReleaseNotes    { get; init; } = "";

    public bool IsUpdateAvailable => LatestVersion > CurrentVersion;
}
