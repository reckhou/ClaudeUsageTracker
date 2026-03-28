# Multi-Provider Usage Dashboard Implementation Plan

**Goal:** Add a new Providers dashboard page that tracks plan quota usage from multiple API providers (Claude Pro + MiniMaxi), replacing the plan quota section on the main dashboard.

**Architecture:** Provider-agnostic architecture using `IUsageProvider` interface. Each provider implements `FetchAsync()` → `ProviderUsageRecord`. A `ProvidersDashboardViewModel` queries all registered providers and displays aggregated cards. `UsageDataService` stores `ProviderUsageRecord` in SQLite (one record per provider, per fetch). MiniMaxi aggregates all model usage but reports `MiniMax-M*` as the primary coding indicator.

**Tech Stack:** C# + MAUI + CommunityToolkit.Mvvm + SQLite (same stack as existing)

---

## Progress

- [x] Task 1: Core provider infrastructure (interface, model, service storage)
- [x] Task 2: MiniMaxiUsageProvider implementation
- [x] Task 3: Providers dashboard page with MiniMaxi integration

---

## Files

- Create: `src/ClaudeUsageTracker.Core/Models/ProviderUsageRecord.cs` — shared model for all provider quota records
- Create: `src/ClaudeUsageTracker.Core/Services/IUsageProvider.cs` — interface for quota providers
- Create: `src/ClaudeUsageTracker.Maui/Services/MiniMaxiUsageProvider.cs` — MiniMaxi API implementation
- Create: `src/ClaudeUsageTracker.Maui/ViewModels/ProvidersDashboardViewModel.cs` — dashboard VM for the new page
- Create: `src/ClaudeUsageTracker.Maui/Views/ProvidersDashboardPage.xaml(.cs)` — new MAUI page
- Modify: `src/ClaudeUsageTracker.Core/Services/UsageDataService.cs` — add `ProviderUsageRecord` table + CRUD
- Modify: `src/ClaudeUsageTracker.Maui/Views/SetupPage.xaml.cs` — add MiniMaxi API key configuration
- Modify: `src/ClaudeUsageTracker.Maui/Views/DashboardPage.xaml.cs` — remove plan quota section (moved to new page)

---

### Task 1: Core Provider Infrastructure

**Files:** `ProviderUsageRecord.cs`, `IUsageProvider.cs`, `UsageDataService.cs`

Define the provider-agnostic model and storage:

**`ProviderUsageRecord.cs`** (new file in `Core/Models`):
```csharp
using SQLite;

namespace ClaudeUsageTracker.Core.Models;

[Table("ProviderUsageRecords")]
public class ProviderUsageRecord
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string Provider { get; set; }           // "ClaudePro", "MiniMaxi", etc.
    public int IntervalUtilization { get; set; }    // 0-100, current interval %
    public long IntervalUsed { get; set; }          // absolute units used
    public long IntervalTotal { get; set; }         // absolute units total
    public DateTime IntervalResetsAt { get; set; }  // when interval resets (UTC)
    public int WeeklyUtilization { get; set; }      // 0-100, weekly %
    public long WeeklyUsed { get; set; }
    public long WeeklyTotal { get; set; }
    public DateTime WeeklyResetsAt { get; set; }
    public DateTime FetchedAt { get; set; }         // when this record was fetched
}
```

**`IUsageProvider.cs`** (new file in `Core/Services`):
```csharp
namespace ClaudeUsageTracker.Core.Services;

public interface IUsageProvider
{
    string ProviderName { get; }   // e.g. "MiniMaxi", "ClaudePro"
    Task<ProviderUsageRecord?> FetchAsync(string apiKey, CancellationToken ct = default);
}
```

**`UsageDataService.cs` modifications:**
- Add `ProviderUsageRecord` table creation in `InitAsync`
- Add `UpsertProviderRecordAsync(ProviderUsageRecord record)` — deletes all records for that provider then inserts
- Add `GetLatestProviderRecordAsync(string provider)` — gets most recent record per provider
- Add `GetAllProviderRecordsAsync()` — gets all provider records

---

### Task 2: MiniMaxiUsageProvider Implementation

**Files:** `MiniMaxiUsageProvider.cs`

**`MiniMaxiUsageProvider.cs`** (new file in `Maui/Services`):
```csharp
using System.Net.Http;
using System.Text.Json;
using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Core.Services;

namespace ClaudeUsageTracker.Maui.Services;

public class MiniMaxiUsageProvider : IUsageProvider
{
    public string ProviderName => "MiniMaxi";

    private static readonly HttpClient _http = new();

    public async Task<ProviderUsageRecord?> FetchAsync(string apiKey, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get,
            "https://www.minimaxi.com/v1/api/openplatform/coding_plan/remains");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var root = doc.RootElement;
        if (!root.TryGetProperty("base_resp", out var baseResp)) return null;
        var statusCode = baseResp.GetProperty("status_code").GetInt32();
        if (statusCode != 0) return null;

        var modelRemains = root.GetProperty("model_remains");
        long totalIntervalUsed = 0, totalIntervalTotal = 0;
        long totalWeeklyUsed = 0, totalWeeklyTotal = 0;
        DateTime? earliestIntervalReset = null;
        DateTime? earliestWeeklyReset = null;
        string? primaryModel = null;
        long primaryIntervalUsed = 0, primaryIntervalTotal = 0;

        foreach (var model in modelRemains.EnumerateArray())
        {
            var intervalUsed = model.GetProperty("current_interval_usage_count").GetInt64();
            var intervalTotal = model.GetProperty("current_interval_total_count").GetInt64();
            var weeklyUsed = model.GetProperty("current_weekly_usage_count").GetInt64();
            var weeklyTotal = model.GetProperty("current_weekly_total_count").GetInt64();

            totalIntervalUsed += intervalUsed;
            totalIntervalTotal += intervalTotal;
            totalWeeklyUsed += weeklyUsed;
            totalWeeklyTotal += weeklyTotal;

            // Track primary coding model (MiniMax-M*)
            var modelName = model.GetProperty("model_name").GetString();
            if (modelName == "MiniMax-M*" && intervalTotal > 0)
            {
                primaryModel = modelName;
                primaryIntervalUsed = intervalUsed;
                primaryIntervalTotal = intervalTotal;
                earliestIntervalReset = UnixMsToDateTime(model.GetProperty("end_time").GetInt64());
                earliestWeeklyReset = UnixMsToDateTime(model.GetProperty("weekly_end_time").GetInt64());
            }
            else if (intervalTotal > 0 && earliestIntervalReset == null)
            {
                var intervalReset = UnixMsToDateTime(model.GetProperty("end_time").GetInt64());
                var weeklyReset = UnixMsToDateTime(model.GetProperty("weekly_end_time").GetInt64());
                if (earliestIntervalReset == null || intervalReset < earliestIntervalReset)
                    earliestIntervalReset = intervalReset;
                if (earliestWeeklyReset == null || weeklyReset < earliestWeeklyReset)
                    earliestWeeklyReset = weeklyReset;
            }
        }

        // Use primary model (MiniMax-M*) for display, fall back to aggregate
        var displayUsed = primaryIntervalTotal > 0 ? primaryIntervalUsed : totalIntervalUsed;
        var displayTotal = primaryIntervalTotal > 0 ? primaryIntervalTotal : totalIntervalTotal;
        var displayWeeklyUsed = primaryIntervalTotal > 0
            ? modelRemains.EnumerateArray().First(m => m.GetProperty("model_name").GetString() == "MiniMax-M*")
                .GetProperty("current_weekly_usage_count").GetInt64()
            : totalWeeklyUsed;
        var displayWeeklyTotal = primaryIntervalTotal > 0
            ? modelRemains.EnumerateArray().First(m => m.GetProperty("model_name").GetString() == "MiniMax-M*")
                .GetProperty("current_weekly_total_count").GetInt64()
            : totalWeeklyTotal;

        return new ProviderUsageRecord
        {
            Provider = ProviderName,
            IntervalUtilization = displayTotal > 0 ? (int)(displayUsed * 100 / displayTotal) : 0,
            IntervalUsed = displayUsed,
            IntervalTotal = displayTotal,
            IntervalResetsAt = earliestIntervalReset ?? DateTime.UtcNow.AddHours(5),
            WeeklyUtilization = displayWeeklyTotal > 0 ? (int)(displayWeeklyUsed * 100 / displayWeeklyTotal) : 0,
            WeeklyUsed = displayWeeklyUsed,
            WeeklyTotal = displayWeeklyTotal,
            WeeklyResetsAt = earliestWeeklyReset ?? DateTime.UtcNow.AddDays(7),
            FetchedAt = DateTime.UtcNow
        };
    }

    private static DateTime UnixMsToDateTime(long ms) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
}
```

---

### Task 3: Providers Dashboard Page + Integration

**Files:** `ProvidersDashboardViewModel.cs`, `ProvidersDashboardPage.xaml`, `SetupPage.xaml.cs`

**`ProvidersDashboardViewModel.cs`** (new file in `Maui/ViewModels`):
```csharp
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
        var records = await _db.GetAllProviderRecordsAsync();
        var grouped = records.GroupBy(r => r.Provider).Select(g => g.OrderByDescending(r => r.FetchedAt).First());
        foreach (var record in grouped)
            Providers.Add(ToCard(record));
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
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
```

**`ProvidersDashboardPage.xaml`** (new file in `Maui/Views`) — vertical scroll of `ProviderCardView` controls (reuse existing card style from Dashboard), one card per provider, plus a refresh button in the shell bar.

**`SetupPage.xaml.cs`** — add a new "MiniMaxi" section in setup with an API key entry field and save button, persisting to secure storage under key `"MiniMaxiApiKey"`.

**Navigation:** The existing dashboard's plan quota section (SessionUtilization, WeeklyUtilization cards) should be removed. The new `/providers` route becomes the dedicated plan quota page.

**Verify:** Run the app, navigate to the new Providers dashboard page, tap refresh — MiniMaxi card shows utilization, used/total counts, and next reset time. MiniMaxi API key can be configured on the Setup page and persists across sessions.
