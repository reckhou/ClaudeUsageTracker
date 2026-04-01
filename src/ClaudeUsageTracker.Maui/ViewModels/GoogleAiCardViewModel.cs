using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ClaudeUsageTracker.Core.Models;
using Microsoft.Maui.Storage;

namespace ClaudeUsageTracker.Maui.ViewModels;

public partial class GoogleAiCardViewModel : ObservableObject
{
    // Project dropdown
    [ObservableProperty] private ObservableCollection<string> _projects = ["All Projects"];
    [ObservableProperty] private string _selectedProject = "All Projects";

    // Model filter dropdown
    [ObservableProperty] private ObservableCollection<string> _models = ["All Models"];
    [ObservableProperty] private string _selectedModel = "All Models";

    // Time range toggle: "24h" or "7 days"
    [ObservableProperty] private ObservableCollection<string> _timeRanges = ["24h", "7 days"];
    [ObservableProperty] private string _selectedTimeRange = "24h";

    // Summary display values
    [ObservableProperty] private string _costDisplay = "—";
    [ObservableProperty] private string _costLabel = "Cost (24h)";
    [ObservableProperty] private string _tokensDisplay = "—";
    [ObservableProperty] private string _tokensLabel = "Tokens (24h)";
    [ObservableProperty] private string _requestsDisplay = "—";
    [ObservableProperty] private string _requestsLabel = "Requests (24h)";
    [ObservableProperty] private string _spendCapDisplay = "—";
    [ObservableProperty] private string _lastUpdated = "";
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _showInMiniMode = true;

    private List<GoogleAiUsageRecord> _cachedRecords = [];

    // Mini mode exposes cost and tokens for the selected time range
    public string MiniCost => CostDisplay;
    public string MiniTokens => TokensDisplay;

    partial void OnSelectedProjectChanged(string value) => RecomputeDisplayValues();
    partial void OnSelectedModelChanged(string value) => RecomputeDisplayValues();
    partial void OnSelectedTimeRangeChanged(string value) => RecomputeDisplayValues();

    partial void OnCostDisplayChanged(string value) => OnPropertyChanged(nameof(MiniCost));
    partial void OnTokensDisplayChanged(string value) => OnPropertyChanged(nameof(MiniTokens));

    private const string ShowInMiniModePrefKey = "mini_visible_GoogleAI";

    public GoogleAiCardViewModel()
    {
        ShowInMiniMode = Preferences.Get(ShowInMiniModePrefKey, true);
    }

    partial void OnShowInMiniModeChanged(bool value)
    {
        Preferences.Set(ShowInMiniModePrefKey, value);
    }

    /// <summary>Updates the cached records and recomputes display values.</summary>
    public void UpdateRecords(List<GoogleAiUsageRecord> records, IEnumerable<string> projectIds)
    {
        _cachedRecords = records;

        // Rebuild project list: "All Projects" + each unique project ID
        var ids = projectIds.ToList();
        var newProjects = new ObservableCollection<string> { "All Projects" };
        foreach (var id in ids)
            newProjects.Add(id);
        Projects = newProjects;

        // Rebuild model list from all records
        var modelNames = records.Select(r => r.ModelName).Where(m => !string.IsNullOrEmpty(m)).Distinct().OrderBy(m => m).ToList();
        var newModels = new ObservableCollection<string> { "All Models" };
        foreach (var m in modelNames)
            newModels.Add(m);
        Models = newModels;

        // Keep selected values if still valid, otherwise reset
        if (!Projects.Contains(SelectedProject))
            SelectedProject = "All Projects";
        if (!Models.Contains(SelectedModel))
            SelectedModel = "All Models";

        RecomputeDisplayValues();
    }

    private void RecomputeDisplayValues()
    {
        // Map UI time range to SQLite TimeRange value
        var dbTimeRange = SelectedTimeRange == "7 days" ? "last-7-days" : "last-1-day";
        var is24h = SelectedTimeRange == "24h";

        // Update labels
        var rangeLabel = is24h ? "24h" : "7 days";
        CostLabel = $"Cost ({rangeLabel})";
        TokensLabel = $"Tokens ({rangeLabel})";
        RequestsLabel = $"Requests ({rangeLabel})";

        // Filter by project
        var records = SelectedProject == "All Projects"
            ? _cachedRecords
            : _cachedRecords.Where(r => r.ProjectId == SelectedProject).ToList();

        // Filter by time range
        var rangeRecords = records.Where(r => r.TimeRange == dbTimeRange).ToList();

        // Filter by model for tokens/requests display
        var modelRecords = SelectedModel == "All Models"
            ? rangeRecords
            : rangeRecords.Where(r => r.ModelName == SelectedModel).ToList();

        var totalTokens = modelRecords.Sum(r => r.InputTokens);
        var totalRequests = modelRecords.Sum(r => r.RequestCount);

        TokensDisplay = totalTokens > 0 ? FormatTokens(totalTokens) : "—";
        RequestsDisplay = totalRequests > 0 ? $"{totalRequests:N0}" : "—";

        // Cost: for 24h use SpendCapUsed as rough proxy; for 7 days use Cost from spend page
        var first = rangeRecords.FirstOrDefault();
        var currency = first?.Currency ?? "";

        if (is24h)
        {
            // Use spend cap usage as the 24h cost proxy
            var capUsed = rangeRecords.GroupBy(r => r.ProjectId)
                .Select(g => g.Max(r => r.SpendCapUsed))
                .Sum();
            CostDisplay = capUsed > 0 ? $"{currency}{capUsed:F2}" : "—";
        }
        else
        {
            // Use actual cost from spend page for 7 days
            var cost = rangeRecords.GroupBy(r => r.ProjectId)
                .Select(g => g.Max(r => r.Cost))
                .Sum();
            CostDisplay = cost > 0 ? $"{currency}{cost:F2}" : "—";
        }

        // Spend cap display (use any records — spend cap is the same across time ranges)
        var capUsedTotal = records.GroupBy(r => r.ProjectId).Select(g => g.Max(r => r.SpendCapUsed)).Sum();
        var capLimitTotal = records.GroupBy(r => r.ProjectId).Select(g => g.Max(r => r.SpendCapLimit)).Sum();
        var capCurrency = records.FirstOrDefault()?.Currency ?? currency;
        SpendCapDisplay = (capUsedTotal > 0 || capLimitTotal > 0)
            ? $"{capCurrency}{capUsedTotal:F2} / {capCurrency}{capLimitTotal:F2}"
            : "—";

        if (rangeRecords.Count > 0)
            LastUpdated = rangeRecords.Max(r => r.FetchedAt).ToLocalTime().ToString("h:mm tt");
    }

    private static string FormatTokens(long tokens)
    {
        if (tokens == 0) return "0";
        if (tokens >= 1_000_000)
            return $"{tokens / 1_000_000.0:F2}M";
        if (tokens >= 1_000)
            return $"{tokens / 1_000.0:F1}K";
        return tokens.ToString("N0");
    }
}
