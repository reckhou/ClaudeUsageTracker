using System.Collections.ObjectModel;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public ObservableCollection<DailyUsage> DailyUsages { get; } = new();
    public ObservableCollection<ModelUsage> ModelBreakdown { get; } = new();

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

        // Daily usage for chart (last 30 days)
        DailyUsages.Clear();
        for (int i = 30; i >= 0; i--)
        {
            var date = today.AddDays(-i);
            var dayUsage = usageRecords.Where(r => r.BucketStart.Date == date).ToList();
            var dayCost = costRecords.Where(r => r.BucketStart.Date == date).Sum(r => r.CostUsd);
            var dayTokens = dayUsage.Sum(r => r.InputTokens + r.OutputTokens);
            DailyUsages.Add(new DailyUsage(date, dayCost, dayTokens));
        }

        // Model breakdown
        ModelBreakdown.Clear();
        var byModel = usageRecords
            .GroupBy(r => r.Model)
            .Select(g => new ModelUsage(
                g.Key,
                g.Sum(r => r.InputTokens + r.OutputTokens),
                costRecords.Where(c => c.Description.Contains(g.Key)).Sum(c => c.CostUsd)))
            .OrderByDescending(m => m.Tokens)
            .ToList();
        foreach (var m in byModel) ModelBreakdown.Add(m);

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
    }

    private static string FormatResetsAt(DateTime utc)
    {
        var diff = utc - DateTime.UtcNow;
        if (diff <= TimeSpan.Zero) return "Resetting\u2026";
        if (diff.TotalHours < 1) return $"Resets in {(int)diff.TotalMinutes} min";
        if (diff.TotalHours < 24) return $"Resets in {(int)diff.TotalHours} hr {diff.Minutes} min";
        return $"Resets {utc.ToLocalTime():ddd h:mm tt}";
    }
}

public record DailyUsage(DateTime Date, decimal CostUsd, long Tokens);
public record ModelUsage(string Model, long Tokens, decimal CostUsd);
