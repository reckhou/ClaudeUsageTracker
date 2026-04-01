using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Core.Services;
using Microsoft.Maui.Storage;

namespace ClaudeUsageTracker.Maui.ViewModels;

public partial class ProvidersDashboardViewModel : ObservableObject, IDisposable
{
    private readonly UsageDataService _db;
    private readonly IEnumerable<IUsageProvider> _providers;
    private readonly ISecureStorageService _storage;
    private System.Timers.Timer? _autoRefreshTimer;
    private System.Timers.Timer? _googleAiRefreshTimer;
    private const int GoogleAiRefreshMinutes = 30;
    private static readonly Random _jitterRng = new();

    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _hasError;
    private const string PrefKeyMinutes = "auto_refresh_minutes";
    [ObservableProperty] private int _autoRefreshMinutes = 5;
    [ObservableProperty] private bool _isAutoRefreshRunning;
    private bool _isRefreshAllRunning;
    private bool _isGoogleAiRefreshing;

    public string AutoRefreshToggleText => IsAutoRefreshRunning ? "Stop" : "Start";

    public bool IsAnyRefreshing => Providers.Any(p => p.IsRefreshing);

    // True only when Refresh All was explicitly clicked (stays true until all done),
    // or when all individual card refresh buttons happen to be running simultaneously.
    public bool ShowRefreshAllSpinner =>
        _isRefreshAllRunning || (Providers.Count > 0 && Providers.All(p => p.IsRefreshing));

    public ObservableCollection<ProviderCardViewModel> Providers { get; } = [];

    public GoogleAiCardViewModel GoogleAiCard { get; } = new();

    public IUpdateService? UpdateService { get; }

    public ProvidersDashboardViewModel(UsageDataService db, IEnumerable<IUsageProvider> providers, ISecureStorageService storage, IUpdateService? updateService = null)
    {
        _db = db;
        _providers = providers;
        _storage = storage;
        UpdateService = updateService;
        _autoRefreshMinutes = Preferences.Get(PrefKeyMinutes, 5);
    }

    partial void OnIsAutoRefreshRunningChanged(bool value) => OnPropertyChanged(nameof(AutoRefreshToggleText));

    partial void OnAutoRefreshMinutesChanged(int value) =>
        Preferences.Set(PrefKeyMinutes, Math.Clamp(value, 1, 60));

    [RelayCommand]
    public void ToggleAutoRefresh()
    {
        if (IsAutoRefreshRunning)
        {
            StopAutoRefresh();
        }
        else
        {
            var minutes = Math.Clamp(AutoRefreshMinutes, 1, 60);
            AutoRefreshMinutes = minutes;
            IsAutoRefreshRunning = true;
            ScheduleNextRefresh(minutes);
        }
    }

    private void ScheduleNextRefresh(int minutes)
    {
        _autoRefreshTimer?.Dispose();
        var baseMs = TimeSpan.FromMinutes(minutes).TotalMilliseconds;
        var maxJitterSeconds = Math.Min(120, minutes * 60 * 0.5);
        var jitterMs = _jitterRng.NextDouble() * maxJitterSeconds * 1000;
        _autoRefreshTimer = new System.Timers.Timer(baseMs + jitterMs);
        _autoRefreshTimer.Elapsed += async (_, _) =>
        {
            await RefreshAllAsync();
            if (IsAutoRefreshRunning)
                ScheduleNextRefresh(AutoRefreshMinutes);
        };
        _autoRefreshTimer.AutoReset = false;
        _autoRefreshTimer.Start();
    }

    private void StopAutoRefresh()
    {
        _autoRefreshTimer?.Stop();
        _autoRefreshTimer?.Dispose();
        _autoRefreshTimer = null;
        IsAutoRefreshRunning = false;
    }

    public void Dispose()
    {
        StopAutoRefresh();
        _googleAiRefreshTimer?.Stop();
        _googleAiRefreshTimer?.Dispose();
        _googleAiRefreshTimer = null;
    }

    [RelayCommand]
    public async Task RefreshAllAsync()
    {
        HasError = false;
        ErrorMessage = "";
        _isRefreshAllRunning = true;
        OnPropertyChanged(nameof(ShowRefreshAllSpinner));
        try
        {
            var tasks = _providers.Select(p => RefreshProviderAsync(p)).ToList();
            await Task.WhenAll(tasks);
        }
        finally
        {
            _isRefreshAllRunning = false;
            OnPropertyChanged(nameof(IsAnyRefreshing));
            OnPropertyChanged(nameof(ShowRefreshAllSpinner));
        }

        // Also refresh Google AI if connected — fire-and-forget so a slow/failed
        // Google AI scrape doesn't block the next auto-refresh cycle (which would
        // cause Claude's _claudeTcs guard to reject the overlapping fetch).
        _ = RefreshGoogleAiAsync();
    }

    /// <summary>
    /// Triggered by the 30-minute timer or manual refresh. Calls back to the dashboard page
    /// via the static Current reference to use the embedded silent WebView.
    /// </summary>
    [RelayCommand]
    public async Task RefreshGoogleAiAsync()
    {
        var projectIds = await GetGoogleAiProjectIdsAsync();
        if (projectIds.Count == 0) return;
        if (_isGoogleAiRefreshing) return; // Prevent concurrent scrapes (avoids doubled records)

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (_isGoogleAiRefreshing) return; // Double-check on UI thread
            _isGoogleAiRefreshing = true;
            GoogleAiCard.IsRefreshing = true;
            GoogleAiCard.HasError = false;
            GoogleAiCard.ErrorMessage = "";
            try
            {
                // Delegate to the page's silent WebView
                var page = Views.ProvidersDashboardPage.Current;
                if (page == null) return;

                var records = await page.FetchGoogleAiUsageAsync(projectIds);
                if (records == null || records.Count == 0)
                {
                    GoogleAiCard.HasError = true;
                    GoogleAiCard.ErrorMessage = "No data returned — ensure you are signed in to Google AI Studio.";
                    return;
                }

                // Persist to SQLite by project + time range
                await _db.InitAsync();
                foreach (var group in records.GroupBy(r => new { r.ProjectId, r.TimeRange }))
                    await _db.UpsertGoogleAiRecordsAsync(group.Key.ProjectId, group.Key.TimeRange,
                        group.ToList());

                // Load back all records (covers multi-project totals)
                var allRecords = await _db.GetGoogleAiRecordsAsync();
                GoogleAiCard.IsConnected = true;
                GoogleAiCard.UpdateRecords(allRecords, projectIds);
            }
            catch (Exception ex)
            {
                GoogleAiCard.HasError = true;
                GoogleAiCard.ErrorMessage = ex.Message;
            }
            finally
            {
                GoogleAiCard.IsRefreshing = false;
                _isGoogleAiRefreshing = false;
            }
        });
    }

    public bool IsGoogleAiAutoRefreshRunning => _googleAiRefreshTimer?.Enabled == true;

    public void StartGoogleAiAutoRefresh()
    {
        if (_googleAiRefreshTimer?.Enabled == true) return; // Already running
        _googleAiRefreshTimer?.Dispose();
        _googleAiRefreshTimer = new System.Timers.Timer(TimeSpan.FromMinutes(GoogleAiRefreshMinutes).TotalMilliseconds);
        _googleAiRefreshTimer.Elapsed += async (_, _) => await RefreshGoogleAiAsync();
        _googleAiRefreshTimer.AutoReset = true;
        _googleAiRefreshTimer.Start();
    }

    /// <summary>
    /// Loads the last cached Google AI records from SQLite without triggering a WebView scrape.
    /// Called at startup so the card appears immediately with stale data; a live scrape follows.
    /// </summary>
    public async Task LoadGoogleAiFromCacheAsync()
    {
        var projectIds = await GetGoogleAiProjectIdsAsync();
        if (projectIds.Count == 0) return;

        await _db.InitAsync();
        var cached = await _db.GetGoogleAiRecordsAsync();
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            GoogleAiCard.IsConnected = true;
            GoogleAiCard.UpdateRecords(cached, projectIds);
        });
    }

    public async Task<List<string>> GetGoogleAiProjectIdsAsync()
    {
        var stored = await _storage.GetAsync("google_ai_projects");
        if (string.IsNullOrEmpty(stored)) return [];
        return stored.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public async Task SetGoogleAiProjectIdsAsync(IEnumerable<string> ids)
    {
        var joined = string.Join(",", ids);
        await _storage.SetAsync("google_ai_projects", joined);
    }

    private async Task RefreshProviderAsync(IUsageProvider provider)
    {
        var apiKey = await GetApiKeyForProvider(provider.ProviderName);
        if (string.IsNullOrEmpty(apiKey)) return;

        var card = Providers.FirstOrDefault(p => p.ProviderName == provider.ProviderName);
        if (card == null)
        {
            card = new ProviderCardViewModel { ProviderName = provider.ProviderName };
            card.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProviderCardViewModel.IsRefreshing))
                {
                    OnPropertyChanged(nameof(IsAnyRefreshing));
                    OnPropertyChanged(nameof(ShowRefreshAllSpinner));
                }
            };
            // Must add to ObservableCollection on main thread to avoid UI blackouts
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var insertIndex = Providers.Count(p => string.Compare(p.ProviderName, card.ProviderName, StringComparison.OrdinalIgnoreCase) < 0);
                Providers.Insert(insertIndex, card);
            });
        }

        // Must call RefreshAsync on main thread to avoid UI blackouts (property updates must be on UI thread)
        await MainThread.InvokeOnMainThreadAsync(() => card.RefreshAsync(provider, apiKey));
    }

    private async Task<string?> GetApiKeyForProvider(string provider)
    {
        return provider switch
        {
            "MiniMaxi" => await _storage.GetAsync("MiniMaxiApiKey"),
            "Claude" => await _storage.GetAsync("claude_pro_connected"),
            _ => null
        };
    }
}

public partial class ProviderCardViewModel : ObservableObject
{
    [ObservableProperty] private string _providerName = "";
    [ObservableProperty] private bool _showInMiniMode = true;
    [ObservableProperty] private int _intervalUtilization;
    [ObservableProperty] private long _intervalUsed;
    [ObservableProperty] private long _intervalTotal;
    [ObservableProperty] private string _intervalResetsAt = "";
    [ObservableProperty] private int _weeklyUtilization;
    [ObservableProperty] private long _weeklyUsed;
    [ObservableProperty] private long _weeklyTotal;
    [ObservableProperty] private string _weeklyResetsAt = "";
    [ObservableProperty] private string _lastUpdated = "";
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _isConnected;

    private IUsageProvider? _provider;
    private string? _apiKey;

    public async Task RefreshAsync(IUsageProvider provider, string apiKey)
    {
        if (IsRefreshing) return;
        _provider = provider;
        _apiKey = apiKey;
        IsRefreshing = true;
        HasError = false;
        ErrorMessage = "";
        try
        {
            var record = await provider.FetchAsync(apiKey);
            if (record == null)
            {
                HasError = true;
                ErrorMessage = "No data returned";
                return;
            }

            ProviderName = record.Provider;
            IntervalUtilization = record.IntervalUtilization;
            IntervalUsed = record.IntervalUsed;
            IntervalTotal = record.IntervalTotal;
            IntervalResetsAt = FormatResetsAt(record.IntervalResetsAt);
            WeeklyUtilization = record.WeeklyUtilization;
            WeeklyUsed = record.WeeklyUsed;
            WeeklyTotal = record.WeeklyTotal;
            WeeklyResetsAt = FormatWeeklyResetsAt(record.WeeklyResetsAt);
            LastUpdated = record.FetchedAt.ToLocalTime().ToString("h:mm tt");
            IsConnected = true;
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    public async Task RefreshThisAsync()
    {
        if (_provider == null || _apiKey == null) return;
        await RefreshAsync(_provider, _apiKey);
    }

    private static string FormatResetsAt(DateTime utc)
    {
        if (utc == DateTime.MinValue) return "—";
        var diff = utc - DateTime.UtcNow;
        if (diff <= TimeSpan.Zero) return "Resetting…";
        if (diff.TotalHours < 1) return $"Resets in {(int)diff.TotalMinutes} min";
        if (diff.TotalHours < 24) return $"Resets in {(int)diff.TotalHours} hr {diff.Minutes} min";
        return $"Resets {utc.ToLocalTime():ddd h:mm tt}";
    }

    // Weekly resets are days away — show full date/time in system locale plus a countdown.
    // Example: "29/03/2026 8:00 PM Sun (in 2 days 10 hrs 20 mins)"
    private static string FormatWeeklyResetsAt(DateTime utc)
    {
        if (utc == DateTime.MinValue) return "—";
        var diff = utc - DateTime.UtcNow;
        if (diff <= TimeSpan.Zero) return "Resetting…";

        var local   = utc.ToLocalTime();
        var dateStr = local.ToString("d");   // short date per system locale
        var timeStr = local.ToString("t");   // short time per system locale
        var dayStr  = local.ToString("ddd"); // abbreviated day name

        var days    = (int)diff.TotalDays;
        var hours   = diff.Hours;
        var minutes = diff.Minutes;

        var countdown = days > 0
            ? $"in {days} {(days == 1 ? "day" : "days")} {hours} {(hours == 1 ? "hr" : "hrs")} {minutes} {(minutes == 1 ? "min" : "mins")}"
            : hours > 0
                ? $"in {hours} {(hours == 1 ? "hr" : "hrs")} {minutes} {(minutes == 1 ? "min" : "mins")}"
                : $"in {minutes} {(minutes == 1 ? "min" : "mins")}";

        return $"{dateStr} {timeStr} {dayStr} ({countdown})";
    }
}
