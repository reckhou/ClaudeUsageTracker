using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Core.Services;

namespace ClaudeUsageTracker.Core.ViewModels;

public partial class DashboardViewModel(
    ISecureStorageService storage,
    AnthropicApiService api,
    IUsageDataService db,
    IClaudeAiUsageService? claudeAi = null) : ObservableObject
{
    [ObservableProperty] private decimal _todayCostUsd;
    [ObservableProperty] private decimal _monthCostUsd;
    [ObservableProperty] private long _todayTotalTokens;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private string _lastUpdated = "";
    [ObservableProperty] private string _topModelName = "";
    [ObservableProperty] private bool _hasTopModel;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _hasError;

    // Weekly (Admin API)
    [ObservableProperty] private decimal _weekCostUsd;
    [ObservableProperty] private long _weekTotalTokens;
    [ObservableProperty] private bool _hasAdminApiData;

    // Claude Pro quota
    [ObservableProperty] private int _sessionUtilization;
    [ObservableProperty] private string _sessionResetsAt = "";
    [ObservableProperty] private int _weeklyUtilization;
    [ObservableProperty] private string _weeklyResetsAt = "";
    [ObservableProperty] private bool _hasQuota;

    // Token chart
    [ObservableProperty] private ProviderFilter _selectedProvider = ProviderFilter.Anthropic;
    [ObservableProperty] private TokenTimeRange _selectedTimeRange = TokenTimeRange.Past24Hours;
    [ObservableProperty] private string _timeRangeLabel = "Past 24 Hours";

    public ObservableCollection<DailyUsage> DailyUsages { get; } = new();
    public ObservableCollection<TokenUsage> TokenChartData { get; } = new();

    public string PageTitle => SelectedProvider switch
    {
        ProviderFilter.Anthropic => "Anthropic Usage Tracker",
        ProviderFilter.MiniMaxi  => "MiniMaxi Usage Tracker",
        ProviderFilter.GoogleAI  => "Google AI Usage Tracker",
        _                        => "Usage Tracker"
    };

    public bool IsTimeRange24h => SelectedTimeRange == TokenTimeRange.Past24Hours;
    public bool IsTimeRange7d  => SelectedTimeRange == TokenTimeRange.Past7Days;
    public bool IsTimeRange30d => SelectedTimeRange == TokenTimeRange.Past30Days;

    // Shows "N/A" when current provider has no Admin API cost data
    private bool IsAdminApiAvailableForProvider =>
        HasAdminApiData && SelectedProvider == ProviderFilter.Anthropic;

    public string TodayCostDisplay  => IsAdminApiAvailableForProvider ? TodayCostUsd.ToString("C")  : "N/A";
    public string WeekCostDisplay   => IsAdminApiAvailableForProvider ? WeekCostUsd.ToString("C")   : "N/A";
    public string MonthCostDisplay  => IsAdminApiAvailableForProvider ? MonthCostUsd.ToString("C")  : "N/A";

    public string? CostUnavailableMessage { get; private set; }
    public string? TokenUnavailableMessage { get; private set; }

    partial void OnSelectedProviderChanged(ProviderFilter value)
    {
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(TodayCostDisplay));
        OnPropertyChanged(nameof(WeekCostDisplay));
        OnPropertyChanged(nameof(MonthCostDisplay));
        _ = LoadFromDbAsync();
    }

    partial void OnSelectedTimeRangeChanged(TokenTimeRange value)
    {
        OnPropertyChanged(nameof(IsTimeRange24h));
        OnPropertyChanged(nameof(IsTimeRange7d));
        OnPropertyChanged(nameof(IsTimeRange30d));
        _ = LoadTokenChartDataAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;

        ErrorMessage = "";
        HasError = false;
        IsRefreshing = true;
        try
        {
            await db.InitAsync();

            var key = await storage.GetAsync("admin_api_key");
            if (!string.IsNullOrEmpty(key))
            {
                // Respect 1-per-minute polling guideline
                var lastFetch = await db.GetLastFetchedAtAsync();
                if (!lastFetch.HasValue || (DateTime.UtcNow - lastFetch.Value).TotalSeconds >= 60)
                {
                    api.SetApiKey(key);

                    var to = DateTime.UtcNow.Date;
                    var from = to.AddDays(-31);

                    var usageRecords = await api.FetchUsageAsync(from, to);
                    var costRecords = await api.FetchCostsAsync(from, to);

                    await db.UpsertUsageRecordsAsync(usageRecords);
                    await db.UpsertCostRecordsAsync(costRecords);
                }
            }

            await LoadFromDbAsync();
        }
        catch (HttpRequestException ex)
        {
            ErrorMessage = $"Network error: {ex.Message}";
            HasError = true;
            try { await LoadFromDbAsync(); } catch { }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Refresh failed: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    public async Task RefreshQuotaAsync()
    {
        if (claudeAi == null) return;
        var record = await claudeAi.FetchQuotaAsync();
        if (record == null) return;
        await db.InitAsync();
        await db.UpsertQuotaRecordAsync(record);
        await LoadFromDbAsync();
    }

    [RelayCommand]
    public void SetTimeRange(string range)
    {
        if (!Enum.TryParse<TokenTimeRange>(range, out var tr)) return;
        SelectedTimeRange = tr;
        TimeRangeLabel = tr switch
        {
            TokenTimeRange.Past24Hours => "Past 24 Hours (Hourly)",
            TokenTimeRange.Past7Days => "Past 7 Days (Daily)",
            TokenTimeRange.Past30Days => "Past 30 Days (Daily)",
            _ => ""
        };
        // OnSelectedTimeRangeChanged partial fires LoadTokenChartDataAsync
    }

    private async Task LoadFromDbAsync()
    {
        var today = DateTime.UtcNow.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var from = today.AddDays(-31);

        var usageRecords = await db.GetUsageAsync(from, today.AddDays(1));
        var costRecords = await db.GetCostsAsync(from, today.AddDays(1));

        // Today stats
        var todayUsage = usageRecords.Where(r => r.BucketStart.Date == today).ToList();
        TodayTotalTokens = todayUsage.Sum(r => r.InputTokens + r.OutputTokens + r.CacheReadTokens + r.CacheCreationTokens);

        var todayCosts = costRecords.Where(r => r.BucketStart.Date == today).ToList();
        TodayCostUsd = todayCosts.Sum(r => r.CostUsd);

        // Month stats
        var monthCosts = costRecords.Where(r => r.BucketStart.Date >= monthStart).ToList();
        MonthCostUsd = monthCosts.Sum(r => r.CostUsd);

        // Weekly stats
        var daysFromMon = ((int)today.DayOfWeek + 6) % 7;
        var weekStart = today.AddDays(-daysFromMon);
        WeekCostUsd = costRecords.Where(r => r.BucketStart.Date >= weekStart).Sum(r => r.CostUsd);
        WeekTotalTokens = usageRecords.Where(r => r.BucketStart.Date >= weekStart)
            .Sum(r => r.InputTokens + r.OutputTokens + r.CacheReadTokens + r.CacheCreationTokens);
        HasAdminApiData = costRecords.Any();
        OnPropertyChanged(nameof(TodayCostDisplay));
        OnPropertyChanged(nameof(WeekCostDisplay));
        OnPropertyChanged(nameof(MonthCostDisplay));

        // Daily usage for chart (last 30 days) — only Anthropic has historical cost data
        DailyUsages.Clear();
        if (SelectedProvider == ProviderFilter.Anthropic)
        {
            for (int i = 30; i >= 0; i--)
            {
                var date = today.AddDays(-i);
                var dayUsage = usageRecords.Where(r => r.BucketStart.Date == date).ToList();
                var dayCost = costRecords.Where(r => r.BucketStart.Date == date).Sum(r => r.CostUsd);
                var dayTokens = dayUsage.Sum(r => r.InputTokens + r.OutputTokens);
                DailyUsages.Add(new DailyUsage(date, dayCost, dayTokens));
            }
            CostUnavailableMessage = HasAdminApiData ? null : "No cost data — add an Admin API key in Settings";
        }
        else
        {
            var providerDisplayName = PageTitle.Replace(" Usage Tracker", "");
            CostUnavailableMessage = $"{providerDisplayName} provides quota units, not USD cost history";
        }
        OnPropertyChanged(nameof(CostUnavailableMessage));

        // Top model (from Anthropic Admin API)
        var byModel = usageRecords
            .GroupBy(r => r.Model)
            .Select(g => new ModelUsage(
                g.Key,
                g.Sum(r => r.InputTokens + r.OutputTokens),
                costRecords.Where(c => c.Description.Contains(g.Key)).Sum(c => c.CostUsd)))
            .OrderByDescending(m => m.Tokens)
            .ToList();

        var top = byModel.FirstOrDefault();
        TopModelName = top?.Model ?? "";
        HasTopModel = top != null;

        // Last updated
        var lastFetch = await db.GetLastFetchedAtAsync();
        if (lastFetch.HasValue)
        {
            var ago = DateTime.UtcNow - lastFetch.Value;
            LastUpdated = ago.TotalMinutes < 1 ? "Just now"
                : ago.TotalHours < 1 ? $"{(int)ago.TotalMinutes} min ago"
                : $"{(int)ago.TotalHours}h ago";
        }

        // Quota (Claude Pro)
        var quota = await db.GetLatestQuotaAsync();
        HasQuota = quota != null;
        if (quota != null)
        {
            SessionUtilization = quota.FiveHourUtilization;
            SessionResetsAt = FormatResetsAt(quota.FiveHourResetsAt);
            WeeklyUtilization = quota.SevenDayUtilization;
            WeeklyResetsAt = FormatResetsAt(quota.SevenDayResetsAt);
        }

        // Load token chart data
        await LoadTokenChartDataAsync();
    }

    public async Task LoadTokenChartDataAsync()
    {
        TokenChartData.Clear();

        if (SelectedProvider == ProviderFilter.Anthropic)
        {
            var now = DateTime.UtcNow;
            var from = SelectedTimeRange switch
            {
                TokenTimeRange.Past24Hours => now.AddHours(-23),
                TokenTimeRange.Past7Days   => now.AddHours(-167),
                TokenTimeRange.Past30Days  => now.Date.AddDays(-29),
                _                          => now.AddHours(-23)
            };

            var records = await db.GetUsageAsync(from, now.AddHours(1));

            if (SelectedTimeRange == TokenTimeRange.Past24Hours || SelectedTimeRange == TokenTimeRange.Past7Days)
            {
                // Hourly slots: 24 slots for 24h, 168 slots for 7d
                int slots = SelectedTimeRange == TokenTimeRange.Past24Hours ? 24 : 168;
                var startHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc)
                    .AddHours(-(slots - 1));
                var byHour = records
                    .GroupBy(r => new DateTime(r.BucketStart.Year, r.BucketStart.Month, r.BucketStart.Day,
                                               r.BucketStart.Hour, 0, 0, DateTimeKind.Utc))
                    .ToDictionary(g => g.Key, g => g.Sum(r => r.InputTokens + r.OutputTokens));
                for (int i = 0; i < slots; i++)
                {
                    var slot = startHour.AddHours(i);
                    byHour.TryGetValue(slot, out long tokens);
                    TokenChartData.Add(new TokenUsage(slot, tokens));
                }
            }
            else
            {
                // Daily slots: 30 days
                var today = now.Date;
                var byDay = records
                    .GroupBy(r => r.BucketStart.Date)
                    .ToDictionary(g => g.Key, g => g.Sum(r => r.InputTokens + r.OutputTokens));
                for (int i = 29; i >= 0; i--)
                {
                    var date = today.AddDays(-i);
                    byDay.TryGetValue(date, out long tokens);
                    TokenChartData.Add(new TokenUsage(date, tokens));
                }
            }

            TokenUnavailableMessage = records.Count == 0
                ? "No token data — add an Admin API key in Settings"
                : null;
        }
        else
        {
            // MiniMaxi / GoogleAI — snapshot only (no historical token data)
            var allRecords = await db.GetAllProviderRecordsAsync();
            var recordName = SelectedProvider.ToString();
            var record = allRecords.FirstOrDefault(r => r.Provider == recordName);
            if (record != null && record.IntervalUsed > 0)
            {
                TokenChartData.Add(new TokenUsage(DateTime.UtcNow, record.IntervalUsed));
                TokenUnavailableMessage = null;
            }
            else
            {
                TokenUnavailableMessage = $"No token snapshot available for {recordName}";
            }
        }
        OnPropertyChanged(nameof(TokenUnavailableMessage));
    }

    private static string FormatResetsAt(DateTime utc)
    {
        if (utc == DateTime.MinValue) return "—";
        var diff = utc - DateTime.UtcNow;
        if (diff <= TimeSpan.Zero) return "Resetting\u2026";
        if (diff.TotalHours < 1) return $"Resets in {(int)diff.TotalMinutes} min";
        if (diff.TotalHours < 24) return $"Resets in {(int)diff.TotalHours} hr {diff.Minutes} min";
        return $"Resets {utc.ToLocalTime():ddd h:mm tt}";
    }
}

public enum ProviderFilter { Anthropic, MiniMaxi, GoogleAI }
public enum TokenTimeRange { Past24Hours, Past7Days, Past30Days }

public record DailyUsage(DateTime Date, decimal CostUsd, long Tokens);
public record ModelUsage(string Model, long Tokens, decimal CostUsd);
public record TokenUsage(DateTime Date, long Tokens);
