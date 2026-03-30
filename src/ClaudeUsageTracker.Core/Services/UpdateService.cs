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

            // Normalize to 3 components (Major.Minor.Build) before comparing.
            // AppInfo.VersionString on Windows returns 4 parts (e.g. "1.3.3.0") while
            // GitHub tags use 3 (e.g. "v1.3.3"). Without normalization, Version(1,3,3)
            // has Revision=-1 which compares as less than Version(1,3,3,0).
            static Version Trim3(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
            latestVer  = Trim3(latestVer);
            currentVer = Trim3(currentVer);

            // Find .zip asset (skip .sha256 sidecar files)
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

            // Fetch sha256 sidecar (foo.zip → foo.zip.sha256)
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

            var newExe = Directory.GetFiles(extractDir, "*.exe", SearchOption.AllDirectories)
                .FirstOrDefault()
                ?? throw new FileNotFoundException("No .exe found in update package.");

            var currentExe = Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot determine current process path.");

            // --- Phase 4: Write PowerShell self-replace script and launch ---
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

            // Quit via injected callback — MAUI layer supplies Application.Current?.Quit()
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
