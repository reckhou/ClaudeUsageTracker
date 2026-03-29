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
    public async Task RefreshAllAsync()
    {
        HasError = false;
        ErrorMessage = "";
        var tasks = _providers.Select(p => RefreshProviderAsync(p)).ToList();
        await Task.WhenAll(tasks);
    }

    private async Task RefreshProviderAsync(IUsageProvider provider)
    {
        var apiKey = await GetApiKeyForProvider(provider.ProviderName);
        if (string.IsNullOrEmpty(apiKey)) return;

        var card = Providers.FirstOrDefault(p => p.ProviderName == provider.ProviderName);
        if (card == null)
        {
            card = new ProviderCardViewModel { ProviderName = provider.ProviderName };
            // Must add to ObservableCollection on main thread to avoid UI blackouts
            await MainThread.InvokeOnMainThreadAsync(() => Providers.Add(card));
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
            WeeklyResetsAt = FormatResetsAt(record.WeeklyResetsAt);
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
}
