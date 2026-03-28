using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeUsageTracker.Core.Services;

namespace ClaudeUsageTracker.Core.ViewModels;

public partial class DashboardViewModel(
    ISecureStorageService storage,
    AnthropicApiService api,
    IUsageDataService db) : ObservableObject
{
    [ObservableProperty] private decimal _todayCostUsd;
    [ObservableProperty] private decimal _monthCostUsd;
    [ObservableProperty] private long _todayTotalTokens;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private string _lastUpdated = "";

    public ObservableCollection<DailyUsage> DailyUsages { get; } = new();
    public ObservableCollection<ModelUsage> ModelBreakdown { get; } = new();

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;

        // Respect 1-per-minute polling guideline
        var lastFetch = await db.GetLastFetchedAtAsync();
        if (lastFetch.HasValue && (DateTime.UtcNow - lastFetch.Value).TotalSeconds < 60)
        {
            await LoadFromDbAsync();
            return;
        }

        IsRefreshing = true;
        try
        {
            var key = await storage.GetAsync("admin_api_key");
            if (string.IsNullOrEmpty(key)) return;

            api.SetApiKey(key);

            var to = DateTime.UtcNow.Date;
            var from = to.AddDays(-31);

            var usageRecords = await api.FetchUsageAsync(from, to);
            var costRecords = await api.FetchCostsAsync(from, to);

            await db.UpsertUsageRecordsAsync(usageRecords);
            await db.UpsertCostRecordsAsync(costRecords);

            await LoadFromDbAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
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

        // Last updated
        var lastFetch = await db.GetLastFetchedAtAsync();
        if (lastFetch.HasValue)
        {
            var ago = DateTime.UtcNow - lastFetch.Value;
            LastUpdated = ago.TotalMinutes < 1 ? "Just now"
                : ago.TotalHours < 1 ? $"{(int)ago.TotalMinutes} min ago"
                : $"{(int)ago.TotalHours}h ago";
        }
    }
}

public record DailyUsage(DateTime Date, decimal CostUsd, long Tokens);
public record ModelUsage(string Model, long Tokens, decimal CostUsd);
