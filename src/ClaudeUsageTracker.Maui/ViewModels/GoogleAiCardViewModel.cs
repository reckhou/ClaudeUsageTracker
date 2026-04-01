using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeUsageTracker.Core.Models;
using Microsoft.Maui.Storage;

namespace ClaudeUsageTracker.Maui.ViewModels;

public partial class GoogleAiCardViewModel : ObservableObject
{
    // Project dropdown
    [ObservableProperty] private ObservableCollection<string> _projects = ["All Projects"];
    [ObservableProperty] private string _selectedProject = "All Projects";

    // Summary display values
    [ObservableProperty] private string _cost24h = "—";
    [ObservableProperty] private string _tokens24h = "—";
    [ObservableProperty] private string _requests24h = "—";
    [ObservableProperty] private string _spendCapDisplay = "—";
    [ObservableProperty] private string _lastUpdated = "";
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _showInMiniMode = true;

    // Per-model breakdown for the dashboard card
    [ObservableProperty] private ObservableCollection<GoogleAiModelRow> _modelRows = [];

    private List<GoogleAiUsageRecord> _cachedRecords = [];

    // Mini mode exposes 24h cost and tokens
    public string MiniCost => Cost24h;
    public string MiniTokens => Tokens24h;

    partial void OnSelectedProjectChanged(string value) => RecomputeDisplayValues();

    partial void OnCost24hChanged(string value) => OnPropertyChanged(nameof(MiniCost));
    partial void OnTokens24hChanged(string value) => OnPropertyChanged(nameof(MiniTokens));

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

        // Keep selected project if still valid, otherwise reset to All
        if (!Projects.Contains(SelectedProject))
            SelectedProject = "All Projects";
        else
            RecomputeDisplayValues();
    }

    private void RecomputeDisplayValues()
    {
        var records = SelectedProject == "All Projects"
            ? _cachedRecords
            : _cachedRecords.Where(r => r.ProjectId == SelectedProject).ToList();

        var day1 = records.Where(r => r.TimeRange == "last-1-day").ToList();

        // Cost: sum of all unique projects (avoid double-counting per project by taking the
        // max cost per project, since cost is stored at the record level but applies to all models)
        var costByProject = day1.GroupBy(r => r.ProjectId)
            .Select(g => g.Max(r => r.Cost))
            .Sum();
        var totalTokens = day1.Sum(r => r.InputTokens);
        var totalRequests = day1.Sum(r => r.RequestCount);

        // Pick currency + spend cap from first record
        var first = day1.FirstOrDefault();
        var currency = first?.Currency ?? "";

        Cost24h = costByProject > 0 ? $"{currency}{costByProject:F2}" : "—";
        Tokens24h = totalTokens > 0 ? FormatTokens(totalTokens) : "—";
        Requests24h = totalRequests > 0 ? $"{totalRequests:N0}" : "—";

        // Spend cap: sum across projects
        var capUsed = day1.GroupBy(r => r.ProjectId).Select(g => g.Max(r => r.SpendCapUsed)).Sum();
        var capLimit = day1.GroupBy(r => r.ProjectId).Select(g => g.Max(r => r.SpendCapLimit)).Sum();
        SpendCapDisplay = (capUsed > 0 || capLimit > 0)
            ? $"{currency}{capUsed:F2} / {currency}{capLimit:F2}"
            : "—";

        // Per-model rows (aggregate across projects if "All Projects")
        var modelGroups = day1.GroupBy(r => r.ModelName)
            .Select(g => new GoogleAiModelRow
            {
                ModelName = g.Key,
                Requests = $"{g.Sum(r => r.RequestCount):N0}",
                InputTokens = FormatTokens(g.Sum(r => r.InputTokens))
            })
            .OrderByDescending(r => long.TryParse(r.Requests.Replace(",", ""), out var n) ? n : 0)
            .ToList();

        ModelRows = new ObservableCollection<GoogleAiModelRow>(modelGroups);

        if (day1.Count > 0)
            LastUpdated = day1.Max(r => r.FetchedAt).ToLocalTime().ToString("h:mm tt");
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

public class GoogleAiModelRow
{
    public string ModelName { get; set; } = "";
    public string Requests { get; set; } = "";
    public string InputTokens { get; set; } = "";
}
