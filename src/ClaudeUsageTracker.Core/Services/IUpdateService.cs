using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeUsageTracker.Core.Services;

public interface IUpdateService : INotifyPropertyChanged
{
    bool   IsUpdateAvailable      { get; }
    bool   IsUpdating             { get; }
    int    UpdateProgress         { get; }
    string LatestVersion          { get; }
    string ReleaseNotes           { get; }
    bool   ShowUpdateBanner       { get; }
    string BannerTitle            { get; }
    string UpdateButtonText       { get; }
    double UpdateProgressFraction { get; }

    IAsyncRelayCommand CheckForUpdateCommand     { get; }
    IAsyncRelayCommand ForceApplyUpdateCommand   { get; }

    Task<Models.UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default);
    Task ApplyUpdateAsync(CancellationToken ct = default);
    Task ForceApplyUpdateAsync(CancellationToken ct = default);
}
