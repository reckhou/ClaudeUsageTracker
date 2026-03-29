# Auto-Update Feature Implementation Plan

**Goal:** Add GitHub-release-based auto-update so the app silently checks for new versions on startup and lets users apply them with one click.

**Architecture:** Three layers work together. `UpdateService` (Core, Singleton) owns all update state — it polls the GitHub releases API on startup and every 5 minutes, holds `IsUpdateAvailable`/`IsUpdating`/`UpdateProgress`, and handles download+verify+self-replace via a PowerShell helper script. `DashboardViewModel` exposes `UpdateService` directly as a bindable property so `DashboardPage.xaml` can bind to it without forwarding every property. Because `UpdateService` is a Singleton and `DashboardViewModel` is Transient, the update state survives ViewModel recreation across page navigations. The `release.ps1` script gains SHA256 generation so the app can verify integrity before applying.

**Tech Stack:** C# + .NET 9 MAUI, CommunityToolkit.Mvvm (`ObservableObject`, `[RelayCommand]`), `System.Net.Http`, `System.IO.Compression`, `System.Security.Cryptography`, PowerShell self-replace script, GitHub Releases API, `gh` CLI (release pipeline)

---

## Progress

- [ ] Task 1: Core service layer — `UpdateInfo`, `IUpdateService`, `UpdateService`
- [ ] Task 2: ViewModel + UI — DashboardViewModel integration, update banner, DI wiring
- [ ] Task 3: Release pipeline — SHA256 in `release.ps1`

---

## Files

- Create: `src/ClaudeUsageTracker.Core/Models/UpdateInfo.cs` — version comparison model
- Create: `src/ClaudeUsageTracker.Core/Services/IUpdateService.cs` — interface + INotifyPropertyChanged
- Create: `src/ClaudeUsageTracker.Core/Services/UpdateService.cs` — GitHub API check, download, SHA256 verify, PowerShell self-replace
- Modify: `src/ClaudeUsageTracker.Core/ViewModels/DashboardViewModel.cs` — add `UpdateService` property + startup hook
- Modify: `src/ClaudeUsageTracker.Maui/Views/DashboardPage.xaml` — add update banner row
- Modify: `src/ClaudeUsageTracker.Maui/MauiProgram.cs` — register `IUpdateService` singleton
- Modify: `scripts/release.ps1` — add SHA256 computation and upload

---

### Task 1: Core service layer

**Files:** `Models/UpdateInfo.cs`, `Services/IUpdateService.cs`, `Services/UpdateService.cs`

#### `Models/UpdateInfo.cs`

```csharp
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
```

#### `Services/IUpdateService.cs`

```csharp
using System.ComponentModel;
using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Core.Services;

public interface IUpdateService : INotifyPropertyChanged
{
    bool   IsUpdateAvailable    { get; }
    bool   IsUpdating           { get; }
    int    UpdateProgress       { get; }  // 0–100
    string LatestVersion        { get; }
    string ReleaseNotes         { get; }
    bool   ShowUpdateBanner     { get; }  // IsUpdateAvailable || IsUpdating
    string BannerTitle          { get; }  // e.g. "↑ v1.2.0 available"
    string UpdateButtonText     { get; }  // "Update Now" or "Updating…"
    double UpdateProgressFraction { get; } // UpdateProgress / 100.0

    Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default);
    Task ApplyUpdateAsync(CancellationToken ct = default);
}
```

#### `Services/UpdateService.cs`

```csharp
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Core.Services;

public partial class UpdateService : ObservableObject, IUpdateService
{
    private readonly string     _currentVersion;
    private readonly string     _repoOwner;
    private readonly string     _repoName;
    private readonly HttpClient _http;
    private UpdateInfo?         _pendingUpdate;

    [ObservableProperty] private bool   _isUpdateAvailable;
    [ObservableProperty] private bool   _isUpdating;
    [ObservableProperty] private int    _updateProgress;
    [ObservableProperty] private string _latestVersion  = "";
    [ObservableProperty] private string _releaseNotes   = "";

    // Computed display properties — recalculate whenever backing fields change
    public bool   ShowUpdateBanner      => IsUpdateAvailable || IsUpdating;
    public double UpdateProgressFraction => UpdateProgress / 100.0;
    public string BannerTitle           => IsUpdating
        ? $"Updating to v{LatestVersion}…"
        : $"↑  v{LatestVersion} is available";
    public string UpdateButtonText      => IsUpdating ? "Updating…" : "Update Now";

    public UpdateService(string currentVersion,
                         string repoOwner = "reckhou",
                         string repoName  = "ClaudeUsageTracker")
    {
        _currentVersion = currentVersion;
        _repoOwner      = repoOwner;
        _repoName       = repoName;

        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent
            .Add(new ProductInfoHeaderValue("ClaudeUsageTracker", currentVersion));

        // Start background polling: first check after 3 s, then every 5 min
        _ = StartBackgroundPollingAsync();
    }

    // Re-raise computed property changes when backing fields change
    partial void OnIsUpdateAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowUpdateBanner));
        OnPropertyChanged(nameof(BannerTitle));
    }

    partial void OnIsUpdatingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowUpdateBanner));
        OnPropertyChanged(nameof(BannerTitle));
        OnPropertyChanged(nameof(UpdateButtonText));
    }

    partial void OnUpdateProgressChanged(int value)
        => OnPropertyChanged(nameof(UpdateProgressFraction));

    partial void OnLatestVersionChanged(string value)
    {
        OnPropertyChanged(nameof(BannerTitle));
        OnPropertyChanged(nameof(UpdateButtonText));
    }

    private async Task StartBackgroundPollingAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(3));
        while (!IsUpdateAvailable)
        {
            await CheckForUpdateAsync();
            if (IsUpdateAvailable) break;
            await Task.Delay(TimeSpan.FromMinutes(5));
        }
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            var url  = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/latest";
            var json = await _http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var body    = root.GetProperty("body").GetString() ?? "";

            // Parse version from tag (e.g. "v1.2.3" → "1.2.3")
            var versionStr = tagName.TrimStart('v');
            if (!Version.TryParse(versionStr, out var latestVer)) return null;
            if (!Version.TryParse(_currentVersion, out var currentVer)) return null;

            // Find zip + sha256 assets
            string downloadUrl = "", sha256 = "";
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name         = asset.GetProperty("name").GetString() ?? "";
                    var browserUrl   = asset.GetProperty("browser_download_url").GetString() ?? "";
                    if (name.EndsWith(".zip") && !name.EndsWith(".sha256"))
                        downloadUrl = browserUrl;
                }
            }

            // Fetch sha256 file content (small, single line)
            if (!string.IsNullOrEmpty(downloadUrl))
            {
                var sha256Url = downloadUrl + ".sha256";
                try { sha256 = (await _http.GetStringAsync(sha256Url, ct)).Trim(); }
                catch { /* sha256 optional */ }
            }

            var info = new UpdateInfo
            {
                CurrentVersion = currentVer,
                LatestVersion  = latestVer,
                TagName        = tagName,
                DownloadUrl    = downloadUrl,
                Sha256         = sha256,
                ReleaseNotes   = body
            };

            if (info.IsUpdateAvailable)
            {
                _pendingUpdate      = info;
                LatestVersion       = versionStr;
                ReleaseNotes        = body;
                IsUpdateAvailable   = true;
            }

            return info;
        }
        catch { return null; }  // Network failure — silent, will retry
    }

    [RelayCommand(CanExecute = nameof(CanApplyUpdate))]
    public async Task ApplyUpdateAsync(CancellationToken ct = default)
    {
        if (_pendingUpdate is null || IsUpdating) return;

        IsUpdating = true;
        UpdateProgress = 0;

        try
        {
            var tempDir  = Path.Combine(Path.GetTempPath(), "ClaudeUsageTracker-update");
            var zipPath  = Path.Combine(tempDir, "ClaudeUsageTracker-update.zip");
            var extractDir = Path.Combine(tempDir, "extracted");

            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            Directory.CreateDirectory(tempDir);

            // --- 1. Download (0–80%) ---
            using var response = await _http.GetAsync(
                _pendingUpdate.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var total  = response.Content.Headers.ContentLength ?? -1L;
            var buffer = new byte[81920];
            long read  = 0;

            await using var src  = await response.Content.ReadAsStreamAsync(ct);
            await using var dest = File.Create(zipPath);

            int bytesRead;
            while ((bytesRead = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                read += bytesRead;
                if (total > 0)
                    UpdateProgress = (int)(read * 80 / total);
            }
            await dest.FlushAsync(ct);

            // --- 2. Verify SHA256 ---
            if (!string.IsNullOrEmpty(_pendingUpdate.Sha256))
            {
                using var hashAlg   = SHA256.Create();
                await using var fs  = File.OpenRead(zipPath);
                var hashBytes       = await hashAlg.ComputeHashAsync(fs, ct);
                var actual          = Convert.ToHexString(hashBytes).ToLowerInvariant();
                var expected        = _pendingUpdate.Sha256.ToLowerInvariant();
                if (actual != expected)
                    throw new InvalidOperationException($"SHA256 mismatch.\nExpected: {expected}\nActual:   {actual}");
            }

            // --- 3. Extract (80–100%) ---
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
            UpdateProgress = 100;

            // Find exe in extracted dir
            var newExe = Directory.GetFiles(extractDir, "*.exe", SearchOption.AllDirectories)
                .FirstOrDefault() ?? throw new FileNotFoundException("Exe not found in update package.");

            var currentExe = Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot determine current exe path.");

            // --- 4. PowerShell self-replace ---
            var scriptPath = Path.Combine(Path.GetTempPath(), "ClaudeUsageTracker-updater.ps1");
            var destDir    = Path.GetDirectoryName(currentExe)!;
            var srcDir     = Path.GetDirectoryName(newExe)!;

            await File.WriteAllTextAsync(scriptPath, $"""
                Start-Sleep -Seconds 3
                Copy-Item -Path '{srcDir}\*' -Destination '{destDir}' -Recurse -Force
                cmd /c start "" "{currentExe}"
                """, ct);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = "powershell.exe",
                Arguments       = $"-ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                UseShellExecute = true
            });

            Application.Current?.Quit();
        }
        catch
        {
            IsUpdating     = false;
            UpdateProgress = 0;
            throw;
        }
    }

    private bool CanApplyUpdate() => IsUpdateAvailable && !IsUpdating;
}
```

**Verify:** In `UpdateService`, set a debug breakpoint or temporarily force `IsUpdateAvailable = true` with a fake `_pendingUpdate`. Confirm `ShowUpdateBanner`, `BannerTitle`, and `UpdateButtonText` return correct computed values. No network call needed for this verification.

---

### Task 2: ViewModel integration + MAUI update banner UI

**Files:** `Core/ViewModels/DashboardViewModel.cs`, `Maui/Views/DashboardPage.xaml`, `Maui/MauiProgram.cs`
**Depends on:** Task 1

#### `DashboardViewModel.cs` — add `UpdateService` property

Add `IUpdateService? updateService = null` as a new optional constructor parameter, and expose it as a public property:

```csharp
// Constructor signature change:
public partial class DashboardViewModel(
    ISecureStorageService storage,
    AnthropicApiService api,
    IUsageDataService db,
    IClaudeAiUsageService? claudeAi = null,
    IUpdateService? updateService = null) : ObservableObject
{
    // ... existing fields ...

    public IUpdateService? UpdateService { get; } = updateService;
```

No further changes to the ViewModel — `UpdateService` is a Singleton `ObservableObject`, so XAML binds directly to its properties and commands.

#### `DashboardPage.xaml` — update banner

Change the outer Grid's `RowDefinitions` from 4 rows to 5, insert the update banner at Row 1, and shift existing rows down:

```xml
<!-- Change this: -->
<Grid RowDefinitions="Auto,Auto,Auto,Auto" Padding="20" RowSpacing="16">

<!-- To this: -->
<Grid RowDefinitions="Auto,Auto,Auto,Auto,Auto" Padding="20" RowSpacing="16">
```

Shift existing Grid.Row values: Row 1→2, Row 2→3, Row 3→4 (Row 0 header stays at 0).

Insert the update banner after the header (Row 1):

```xml
<!-- Update Banner — shown when update available or actively updating -->
<Border Grid.Row="1"
        IsVisible="{Binding UpdateService.ShowUpdateBanner}"
        BackgroundColor="{AppThemeBinding Light=#1B4332, Dark=#1A2E22}"
        StrokeThickness="1"
        Stroke="{AppThemeBinding Light=#2D6A4F, Dark=#2D6A4F}"
        StrokeShape="RoundRectangle 8"
        Padding="16,10">
    <Grid ColumnDefinitions="*,Auto" ColumnSpacing="12">

        <!-- Left: title + progress bar -->
        <VerticalStackLayout Grid.Column="0" VerticalOptions="Center" Spacing="6">
            <Label Text="{Binding UpdateService.BannerTitle}"
                   FontSize="13"
                   FontAttributes="Bold"
                   TextColor="#81C995" />
            <ProgressBar IsVisible="{Binding UpdateService.IsUpdating}"
                         Progress="{Binding UpdateService.UpdateProgressFraction}"
                         ProgressColor="#4CAF50"
                         HeightRequest="4" />
            <Label Text="{Binding UpdateService.ReleaseNotes}"
                   FontSize="11"
                   TextColor="{AppThemeBinding Light={StaticResource Gray400}, Dark={StaticResource Gray500}}"
                   IsVisible="{Binding UpdateService.IsUpdating, Converter={StaticResource InvertedBoolConverter}}"
                   MaxLines="2"
                   LineBreakMode="TailTruncation" />
        </VerticalStackLayout>

        <!-- Right: Update Now button -->
        <Button Grid.Column="1"
                Text="{Binding UpdateService.UpdateButtonText}"
                Command="{Binding UpdateService.ApplyUpdateCommand}"
                IsEnabled="{Binding UpdateService.IsUpdating, Converter={StaticResource InvertedBoolConverter}}"
                BackgroundColor="#4CAF50"
                TextColor="White"
                CornerRadius="6"
                FontSize="12"
                HeightRequest="36"
                VerticalOptions="Center" />
    </Grid>
</Border>
```

Note: `InvertedBoolConverter` is already in use in the existing XAML (Refresh button uses it), so no new converter is needed.

When `UpdateService` is `null` (e.g., in design-time previews), the `IsVisible` binding returns `false` — the banner stays hidden.

#### `MauiProgram.cs` — register `IUpdateService`

Add after the `HttpClient` registration:

```csharp
builder.Services.AddSingleton<IUpdateService>(_ =>
    new UpdateService(AppInfo.VersionString));
```

Update the `DashboardViewModel` factory to inject it:

```csharp
builder.Services.AddTransient<DashboardViewModel>(sp => new DashboardViewModel(
    sp.GetRequiredService<ISecureStorageService>(),
    sp.GetRequiredService<AnthropicApiService>(),
    sp.GetRequiredService<IUsageDataService>(),
    sp.GetRequiredService<IClaudeAiUsageService>(),
    sp.GetRequiredService<IUpdateService>()));
```

**Verify:** Launch the app. The update banner is invisible (no update available). Temporarily add a debug override in `UpdateService.CheckForUpdateAsync()` that sets `LatestVersion = "99.0.0"` and `IsUpdateAvailable = true` after 2 seconds. The green banner should appear above the stats cards with `↑  v99.0.0 is available` and an "Update Now" button. Remove the debug override after verifying.

---

### Task 3: Release pipeline — SHA256 in `release.ps1`

**Files:** `scripts/release.ps1`

After step 5 (zip creation), add SHA256 computation and save it as a sidecar file:

```powershell
# 5b. Compute SHA256 for integrity verification
Write-Host "Computing SHA256 hash..." -ForegroundColor Cyan

$hashValue = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLower()
$hashPath  = $ZipPath + ".sha256"
Set-Content -Path $hashPath -Value $hashValue -NoNewline -Encoding ASCII

Write-Host "  SHA256: $hashValue" -ForegroundColor Gray
```

Update step 9 (`gh release create`) to upload both files:

```powershell
# 9. Create GitHub Release (upload zip + sha256 sidecar)
Write-Host "Creating GitHub Release v$Version..." -ForegroundColor Cyan

$releaseUrl = gh release create "v$Version" `
    --title "v$Version" `
    --notes $Notes `
    $ZipPath `
    $hashPath

if ($LASTEXITCODE -ne 0) {
    Write-Error "gh release create failed"
}
```

**Verify:** Run `./scripts/release.ps1 -Version 0.0.1-test` (or check locally by calling just the hash steps). The GitHub release should show two assets: `ClaudeUsageTracker-v0.0.1-test-win-x64.zip` and `ClaudeUsageTracker-v0.0.1-test-win-x64.zip.sha256`. The `.sha256` file should contain a single lowercase hex string with no newline.

---
