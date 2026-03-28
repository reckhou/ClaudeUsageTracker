using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Core.Services;

namespace ClaudeUsageTracker.Maui.ViewModels;

public partial class ProvidersDashboardViewModel : ObservableObject
{
    private readonly UsageDataService _db;
    private readonly IEnumerable<IUsageProvider> _providers;
    private readonly ISecureStorageService _storage;

    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _hasError;

    public ObservableCollection<ProviderCardViewModel> Providers { get; } = [];

    public ProvidersDashboardViewModel(UsageDataService db, IEnumerable<IUsageProvider> providers, ISecureStorageService storage)
    {
        _db = db;
        _providers = providers;
        _storage = storage;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        await _db.InitAsync();
        var records = await _db.GetAllProviderRecordsAsync();
        var grouped = records
            .GroupBy(r => r.Provider)
            .Select(g => g.OrderByDescending(r => r.FetchedAt).First());
        foreach (var record in grouped)
            Providers.Add(ToCard(record));
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        HasError = false;
        ErrorMessage = "";
        try
        {
            foreach (var provider in _providers)
            {
                var apiKey = await GetApiKeyForProvider(provider.ProviderName);
                if (string.IsNullOrEmpty(apiKey)) continue;

                var record = await provider.FetchAsync(apiKey);
                if (record == null) continue;

                await _db.UpsertProviderRecordAsync(record);

                var existing = Providers.FirstOrDefault(p => p.ProviderName == record.Provider);
                if (existing != null)
                    Providers[Providers.IndexOf(existing)] = ToCard(record);
                else
                    Providers.Add(ToCard(record));
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            HasError = true;
        }
        finally
        {
            IsRefreshing = false;
        }
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

    private static ProviderCardViewModel ToCard(ProviderUsageRecord r) => new()
    {
        ProviderName = r.Provider,
        IntervalUtilization = r.IntervalUtilization,
        IntervalUsed = r.IntervalUsed,
        IntervalTotal = r.IntervalTotal,
        IntervalResetsAt = FormatResetsAt(r.IntervalResetsAt),
        WeeklyUtilization = r.WeeklyUtilization,
        WeeklyUsed = r.WeeklyUsed,
        WeeklyTotal = r.WeeklyTotal,
        WeeklyResetsAt = FormatResetsAt(r.WeeklyResetsAt),
        LastUpdated = r.FetchedAt.ToLocalTime().ToString("h:mm tt")
    };

    private static string FormatResetsAt(DateTime utc)
    {
        if (utc == DateTime.MinValue) return "—";
        var diff = utc - DateTime.UtcNow;
        if (diff <= TimeSpan.Zero) return "Resetting…";
        if (diff.TotalHours < 1) return $"Resets in {(int)diff.TotalMinutes} min";
        if (diff.TotalHours < 24) return $"Resets in {(int)diff.TotalHours} hr {diff.Minutes} min";
        return $"Resets {utc.ToLocalTime():ddd h:mm tt}";
    }
}

public partial class ProviderCardViewModel : ObservableObject
{
    public string ProviderName { get; set; } = "";
    public int IntervalUtilization { get; set; }
    public long IntervalUsed { get; set; }
    public long IntervalTotal { get; set; }
    public string IntervalResetsAt { get; set; } = "";
    public int WeeklyUtilization { get; set; }
    public long WeeklyUsed { get; set; }
    public long WeeklyTotal { get; set; }
    public string WeeklyResetsAt { get; set; } = "";
    public string LastUpdated { get; set; } = "";
}
