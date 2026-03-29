# Auto-Update Feature Implementation Plan

**Goal:** Add GitHub-release-based auto-update so the app silently checks for new versions on startup and lets users apply them with one click.

**Architecture:** `UpdateService` (Core, Singleton) owns all update state — polls the GitHub releases API on startup and every 5 minutes, holds `IsUpdateAvailable`/`IsUpdating`/`UpdateProgress`, and handles download+SHA256 verify+self-replace via a PowerShell helper script. A `Action? quitApp` callback is injected at registration time (in MauiProgram.cs) so Core stays free of MAUI API dependencies. `ProvidersDashboardViewModel` (MAUI Singleton) receives `IUpdateService` via constructor and exposes it as a bindable property so `ProvidersDashboardPage.xaml` can bind directly. Because both the ViewModel and the service are Singletons, update state persists for the entire app lifetime. The `release.ps1` script gains SHA256 sidecar file generation so the app can verify integrity before applying.

**Tech Stack:** C# + .NET 9 MAUI, CommunityToolkit.Mvvm (`ObservableObject`, `[RelayCommand]`), `System.Net.Http`, `System.IO.Compression`, `System.Security.Cryptography`, PowerShell self-replace script, GitHub Releases API, `gh` CLI (release pipeline)

---

## Progress

- [ ] Task 1: Core service layer — `UpdateInfo`, `IUpdateService`, `UpdateService`
- [ ] Task 2: ViewModel + UI — `ProvidersDashboardViewModel` integration, update banner in `ProvidersDashboardPage.xaml`, DI wiring in `MauiProgram.cs`
- [ ] Task 3: Release pipeline — SHA256 in `release.ps1`

---

## Files

- Create: `src/ClaudeUsageTracker.Core/Models/UpdateInfo.cs` — version comparison model
- Create: `src/ClaudeUsageTracker.Core/Services/IUpdateService.cs` — interface extending INotifyPropertyChanged
- Create: `src/ClaudeUsageTracker.Core/Services/UpdateService.cs` — GitHub API check, stream download, SHA256 verify, PowerShell self-replace
- Modify: `src/ClaudeUsageTracker.Maui/ViewModels/ProvidersDashboardViewModel.cs` — add `IUpdateService?` constructor parameter + public property
- Modify: `src/ClaudeUsageTracker.Maui/Views/ProvidersDashboardPage.xaml` — add update banner row (Row 1), shift auto-refresh → Row 2, ScrollView → Row 3
- Modify: `src/ClaudeUsageTracker.Maui/MauiProgram.cs` — register `IUpdateService` singleton with `quitApp` callback, update `ProvidersDashboardViewModel` factory
- Modify: `scripts/release.ps1` — add SHA256 computation and upload as sidecar asset

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

namespace ClaudeUsageTracker.Core.Services;

public interface IUpdateService : INotifyPropertyChanged
{
    bool   IsUpdateAvailable      { get; }
    bool   IsUpdating             { get; }
    int    UpdateProgress         { get; }   // 0–100
    string LatestVersion          { get; }
    string ReleaseNotes           { get; }
    bool   ShowUpdateBanner       { get; }   // true when update available or updating
    string BannerTitle            { get; }   // "↑ v1.2.0 available" or "Updating to v1.2.0…"
    string UpdateButtonText       { get; }   // "Update Now" or "Updating…"
    double UpdateProgressFraction { get; }   // UpdateProgress / 100.0

    Task<Models.UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default);
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
    private readonly Action?    _quitApp;
    private readonly HttpClient _http;
    private UpdateInfo?         _pendingUpdate;

    [ObservableProperty] private bool   _isUpdateAvailable;
    [ObservableProperty] private bool   _isUpdating;
    [ObservableProperty] private int    _updateProgress;
    [ObservableProperty] private string _latestVersion = "";
    [ObservableProperty] private string _releaseNotes  = "";

    // Computed display properties — re-raised via partial void hooks below
    public bool   ShowUpdateBanner        => IsUpdateAvailable || IsUpdating;
    public double UpdateProgressFraction  => UpdateProgress / 100.0;
    public string BannerTitle             => IsUpdating
        ? $"Updating to v{LatestVersion}…"
        : $"↑  v{LatestVersion} is available";
    public string UpdateButtonText        => IsUpdating ? "Updating…" : "Update Now";

    public UpdateService(
        string  currentVersion,
        Action? quitApp    = null,
        string  repoOwner  = "reckhou",
        string  repoName   = "ClaudeUsageTracker")
    {
        _currentVersion = currentVersion;
        _quitApp        = quitApp;
        _repoOwner      = repoOwner;
        _repoName       = repoName;

        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent
            .Add(new ProductInfoHeaderValue("ClaudeUsageTracker", currentVersion));

        // Start background polling: first check after 3 s, then every 5 min until found
        _ = StartBackgroundPollingAsync();
    }

    // Fire computed-property change notifications when backing fields change
    partial void OnIsUpdateAvailableChanged(bool _)
    {
        OnPropertyChanged(nameof(ShowUpdateBanner));
        OnPropertyChanged(nameof(BannerTitle));
    }

    partial void OnIsUpdatingChanged(bool _)
    {
        OnPropertyChanged(nameof(ShowUpdateBanner));
        OnPropertyChanged(nameof(BannerTitle));
        OnPropertyChanged(nameof(UpdateButtonText));
    }

    partial void OnUpdateProgressChanged(int _)
        => OnPropertyChanged(nameof(UpdateProgressFraction));

    partial void OnLatestVersionChanged(string _)
        => OnPropertyChanged(nameof(BannerTitle));

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

            // Strip "v" prefix from tag, e.g. "v1.2.3" → "1.2.3"
            var versionStr = tagName.TrimStart('v');
            if (!Version.TryParse(versionStr, out var latestVer)) return null;
            if (!Version.TryParse(_currentVersion, out var currentVer)) return null;

            // Find .zip asset (skip .sha256 files)
            string downloadUrl = "";
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    var url2 = asset.GetProperty("browser_download_url").GetString() ?? "";
                    if (name.EndsWith(".zip") && !name.EndsWith(".sha256"))
                        downloadUrl = url2;
                }
            }

            // Fetch sha256 sidecar (appended to zip URL by convention: foo.zip → foo.zip.sha256)
            string sha256 = "";
            if (!string.IsNullOrEmpty(downloadUrl))
            {
                try { sha256 = (await _http.GetStringAsync(downloadUrl + ".sha256", ct)).Trim(); }
                catch { /* sha256 optional — skip if not present */ }
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
                _pendingUpdate    = info;
                LatestVersion     = versionStr;
                ReleaseNotes      = body;
                IsUpdateAvailable = true;
            }

            return info;
        }
        catch { return null; }  // Network failure — silent, will retry at next interval
    }

    [RelayCommand(CanExecute = nameof(CanApplyUpdate))]
    public async Task ApplyUpdateAsync(CancellationToken ct = default)
    {
        if (_pendingUpdate is null || IsUpdating) return;

        IsUpdating     = true;
        UpdateProgress = 0;

        try
        {
            var tempDir    = Path.Combine(Path.GetTempPath(), "ClaudeUsageTracker-update");
            var zipPath    = Path.Combine(tempDir, "ClaudeUsageTracker-update.zip");
            var extractDir = Path.Combine(tempDir, "extracted");

            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            Directory.CreateDirectory(tempDir);

            // --- Phase 1: Download (reports 0–80%) ---
            using var response = await _http.GetAsync(
                _pendingUpdate.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var   total  = response.Content.Headers.ContentLength ?? -1L;
            var   buffer = new byte[81920];
            long  read   = 0;

            await using var src  = await response.Content.ReadAsStreamAsync(ct);
            await using var dest = File.Create(zipPath);
            int bytesRead;
            while ((bytesRead = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                read += bytesRead;
                if (total > 0) UpdateProgress = (int)(read * 80 / total);
            }
            await dest.FlushAsync(ct);

            // --- Phase 2: Verify SHA256 ---
            if (!string.IsNullOrEmpty(_pendingUpdate.Sha256))
            {
                using var hashAlg  = SHA256.Create();
                await using var fs = File.OpenRead(zipPath);
                var hashBytes      = await hashAlg.ComputeHashAsync(fs, ct);
                var actual         = Convert.ToHexString(hashBytes).ToLowerInvariant();
                var expected       = _pendingUpdate.Sha256.ToLowerInvariant();
                if (actual != expected)
                    throw new InvalidOperationException(
                        $"SHA256 mismatch.\nExpected: {expected}\nActual:   {actual}");
            }

            // --- Phase 3: Extract (80→100%) ---
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
            UpdateProgress = 100;

            // Find the .exe in the extracted folder
            var newExe = Directory.GetFiles(extractDir, "*.exe", SearchOption.AllDirectories)
                .FirstOrDefault()
                ?? throw new FileNotFoundException("No .exe found in update package.");

            var currentExe = Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot determine current process path.");

            // --- Phase 4: Write PowerShell self-replace script and launch it ---
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

            // Quit via injected callback (MAUI layer supplies Application.Current?.Quit())
            _quitApp?.Invoke();
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

**Verify:** Build the Core project. Set a temporary debug override at the end of `StartBackgroundPollingAsync()` that fires `IsUpdateAvailable = true` and `LatestVersion = "99.0.0"` immediately on start. Confirm `ShowUpdateBanner` returns `true`, `BannerTitle` returns `"↑  v99.0.0 is available"`, and `UpdateButtonText` returns `"Update Now"`. Remove override after verifying. No network required.

---

### Task 2: ViewModel integration + MAUI update banner UI

**Files:** `Maui/ViewModels/ProvidersDashboardViewModel.cs`, `Maui/Views/ProvidersDashboardPage.xaml`, `Maui/MauiProgram.cs`
**Depends on:** Task 1

#### `ProvidersDashboardViewModel.cs` — add `IUpdateService?` constructor parameter

Add `IUpdateService? updateService = null` as the last constructor parameter and expose it as a public property. Because `ProvidersDashboardViewModel` is a Singleton, the service reference is set once and lives for the entire app lifetime — no state-loss concern.

```csharp
// Change the constructor signature from:
public ProvidersDashboardViewModel(UsageDataService db, IEnumerable<IUsageProvider> providers, ISecureStorageService storage)

// To:
public ProvidersDashboardViewModel(
    UsageDataService           db,
    IEnumerable<IUsageProvider> providers,
    ISecureStorageService      storage,
    IUpdateService?            updateService = null)
{
    _db        = db;
    _providers = providers;
    _storage   = storage;
    UpdateService = updateService;
}

// Add the property (no [ObservableProperty] — it's a direct reference, set once in ctor):
public IUpdateService? UpdateService { get; }
```

No further ViewModel changes. XAML binds directly to `UpdateService.*` sub-properties — the Singleton `ObservableObject` raises its own `PropertyChanged` events.

#### `ProvidersDashboardPage.xaml` — add update banner row

The current outer grid has 3 rows (`Auto,Auto,*`). The hidden WebView spans all rows. Insert a new Row 1 for the update banner, shift auto-refresh to Row 2 and ScrollView to Row 3.

**Changes needed:**

1. `RowDefinitions="Auto,Auto,*"` → `RowDefinitions="Auto,Auto,Auto,*"`
2. `WebView Grid.RowSpan="3"` → `Grid.RowSpan="4"`
3. Auto-refresh Grid: `Grid.Row="1"` → `Grid.Row="2"`
4. ScrollView: `Grid.Row="2"` → `Grid.Row="3"`

Insert the update banner between header (Row 0) and auto-refresh (now Row 2):

```xml
<!-- Update Banner — Row 1, visible only when ShowUpdateBanner is true -->
<Border Grid.Row="1"
        IsVisible="{Binding UpdateService.ShowUpdateBanner}"
        BackgroundColor="{AppThemeBinding Light=#1B4332, Dark=#1A2E22}"
        StrokeThickness="1"
        Stroke="#2D6A4F"
        StrokeShape="RoundRectangle 8"
        Padding="16,10">
    <Grid ColumnDefinitions="*,Auto" ColumnSpacing="12">

        <!-- Left: banner title + progress bar (during update) + release notes (when waiting) -->
        <VerticalStackLayout Grid.Column="0" VerticalOptions="Center" Spacing="4">
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

        <!-- Right: Update Now button — disabled while updating -->
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

`InvertedBoolConverter` is already registered in `App.xaml` (used by existing Refresh All button) — no new resource needed.

When `UpdateService` is `null`, `{Binding UpdateService.ShowUpdateBanner}` resolves to `false` — the banner stays collapsed with zero height.

#### `MauiProgram.cs` — register `IUpdateService` and update ViewModel factory

Add `IUpdateService` registration before `ProvidersDashboardViewModel`, and update the factory to pass it:

```csharp
// After existing service registrations, add:
builder.Services.AddSingleton<IUpdateService>(_ =>
    new UpdateService(
        AppInfo.VersionString,
        quitApp: () => Application.Current?.Quit()));

// Update ProvidersDashboardViewModel factory to:
builder.Services.AddSingleton<ProvidersDashboardViewModel>(sp =>
    new ProvidersDashboardViewModel(
        sp.GetRequiredService<IUsageDataService>() as UsageDataService
            ?? throw new InvalidOperationException("UsageDataService must be UsageDataService"),
        sp.GetRequiredService<IEnumerable<IUsageProvider>>(),
        sp.GetRequiredService<ISecureStorageService>(),
        sp.GetRequiredService<IUpdateService>()));
```

Add `using ClaudeUsageTracker.Core.Services;` if not already present (it is — used by `IUsageDataService` etc.).

**Verify:** Launch the app. Banner is invisible. Open `UpdateService.cs` and temporarily add to the end of `StartBackgroundPollingAsync()`, before the `while` loop:

```csharp
// DEBUG ONLY — remove after testing
LatestVersion = "99.0.0"; IsUpdateAvailable = true; return;
```

Rebuild. The green banner `"↑  v99.0.0 is available"` appears between the "Plan Usage" header and the auto-refresh row. The "Update Now" button is visible and enabled. Remove the debug lines after confirming.

---

### Task 3: Release pipeline — SHA256 in `release.ps1`

**Files:** `scripts/release.ps1`

After the existing step 5 (zip creation — `Compress-Archive`), insert the SHA256 block:

```powershell
# 5b. Compute SHA256 hash for update integrity verification
Write-Host "Computing SHA256 hash..." -ForegroundColor Cyan

$hashValue = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLower()
$hashPath  = $ZipPath + ".sha256"
Set-Content -Path $hashPath -Value $hashValue -NoNewline -Encoding ASCII

Write-Host "  SHA256: $hashValue" -ForegroundColor Gray
```

Update step 9 (`gh release create`) to upload both files as assets:

```powershell
# 9. Create GitHub Release — attach both zip and sha256 sidecar
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

**Verify:** Run locally:
```powershell
# Test just the hash step in isolation
$ZipPath  = "C:\Temp\test.zip"
"test content" | Set-Content $ZipPath
$hashValue = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLower()
$hashPath  = $ZipPath + ".sha256"
Set-Content -Path $hashPath -Value $hashValue -NoNewline -Encoding ASCII
Get-Content $hashPath   # should print a single 64-char lowercase hex string, no newline
```

On a real release run, the GitHub Release page should show two assets: `ClaudeUsageTracker-v{Version}-win-x64.zip` and `ClaudeUsageTracker-v{Version}-win-x64.zip.sha256`.

---
