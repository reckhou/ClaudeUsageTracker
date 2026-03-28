using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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
}

public record DailyUsage(DateTime Date, decimal CostUsd, long Tokens);
public record ModelUsage(string Model, long Tokens, decimal CostUsd);
