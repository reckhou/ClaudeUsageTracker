# Claude Usage Tracker — Implementation Plan

**Goal:** Build a cross-platform Claude API usage tracker with local persistence, Admin API key auth, and tailored UIs for Windows/Browser (full dashboard) and Android/iOS (compact cards).

**Architecture:**
- `ClaudeUsageTracker.Core` — shared class library: models, ViewModels, Anthropic API client, SQLite data service. All business logic lives here, no UI dependencies.
- `ClaudeUsageTracker.Maui` — .NET MAUI app targeting Windows, Android, iOS. Uses `SecureStorage.Default` (MAUI built-in) to store API key per platform.
- `ClaudeUsageTracker.Web` — Blazor WASM app targeting Browser. Uses JS interop to encrypted localStorage for API key storage.
- Data flow: `API Key → AnthropicApiService → UsageDataService (SQLite cache) → DashboardViewModel → Platform Views`
- ViewModels depend only on injected interfaces — headless testable without any renderer.

**Tech Stack:** C# 12 / .NET 9 · .NET MAUI 9 · Blazor WASM · sqlite-net-pcl · System.Net.Http · CommunityToolkit.Mvvm

**Anthropic API:**
- Usage: `GET /v1/organizations/usage_report/messages` — token counts by model/bucket
- Cost: `GET /v1/organizations/cost_report` — USD cost by workspace/description
- Auth header: `x-api-key: sk-ant-admin...`
- Version header: `anthropic-version: 2023-06-01`
- Data freshness: ~5 min lag; poll at most once per minute

---

## Progress

- [ ] Task 1: Solution scaffolding
- [ ] Task 2: Core models & service interfaces
- [ ] Task 3: Anthropic API service
- [ ] Task 4: SQLite data service
- [ ] Task 5: MAUI project wiring (SecureStorage + DI)
- [ ] Task 6: First-run setup flow — ViewModel + MAUI view
- [ ] Task 7: Dashboard ViewModel
- [ ] Task 8: MAUI Windows full dashboard view
- [ ] Task 9: MAUI Android/iOS compact mobile view
- [ ] Task 10: Blazor WASM project — storage + setup page
- [ ] Task 11: Blazor WASM dashboard page

---

## Files

### Core library
- Create: `src/ClaudeUsageTracker.Core/ClaudeUsageTracker.Core.csproj`
- Create: `src/ClaudeUsageTracker.Core/Models/UsageRecord.cs` — token data per bucket/model
- Create: `src/ClaudeUsageTracker.Core/Models/CostRecord.cs` — USD cost per bucket
- Create: `src/ClaudeUsageTracker.Core/Models/AppConfig.cs` — API key + user prefs
- Create: `src/ClaudeUsageTracker.Core/Services/ISecureStorageService.cs` — interface abstracting key store
- Create: `src/ClaudeUsageTracker.Core/Services/IUsageDataService.cs` — interface for local SQLite cache
- Create: `src/ClaudeUsageTracker.Core/Services/AnthropicApiService.cs` — HTTP client wrapping usage + cost endpoints
- Create: `src/ClaudeUsageTracker.Core/Services/UsageDataService.cs` — sqlite-net-pcl implementation
- Create: `src/ClaudeUsageTracker.Core/ViewModels/SetupViewModel.cs` — first-run API key entry + validation
- Create: `src/ClaudeUsageTracker.Core/ViewModels/DashboardViewModel.cs` — stats aggregation, refresh logic

### MAUI app
- Create: `src/ClaudeUsageTracker.Maui/ClaudeUsageTracker.Maui.csproj`
- Create: `src/ClaudeUsageTracker.Maui/Services/MauiSecureStorageService.cs` — wraps `SecureStorage.Default`
- Create: `src/ClaudeUsageTracker.Maui/Views/SetupPage.xaml(.cs)` — API key entry screen
- Create: `src/ClaudeUsageTracker.Maui/Views/DashboardPage.xaml(.cs)` — Windows full dashboard
- Create: `src/ClaudeUsageTracker.Maui/Views/MobileDashboardPage.xaml(.cs)` — Android/iOS compact cards
- Modify: `src/ClaudeUsageTracker.Maui/MauiProgram.cs` — DI registration

### Blazor WASM
- Create: `src/ClaudeUsageTracker.Web/ClaudeUsageTracker.Web.csproj`
- Create: `src/ClaudeUsageTracker.Web/Services/BrowserSecureStorageService.cs` — JS interop to localStorage
- Create: `src/ClaudeUsageTracker.Web/wwwroot/storage.js` — JS helper for encrypted localStorage
- Create: `src/ClaudeUsageTracker.Web/Pages/Setup.razor` — API key entry page
- Create: `src/ClaudeUsageTracker.Web/Pages/Dashboard.razor` — full dashboard
- Modify: `src/ClaudeUsageTracker.Web/Program.cs` — DI registration

---

### Task 1: Solution scaffolding

**Files:** `ClaudeUsageTracker.sln`, `src/*/**.csproj`

Create a solution with three projects:
```
ClaudeUsageTracker.sln
src/
  ClaudeUsageTracker.Core/    ← netstandard2.1 or net9.0 class library
  ClaudeUsageTracker.Maui/    ← net9.0-windows/android/ios MAUI app
  ClaudeUsageTracker.Web/     ← net9.0 Blazor WASM standalone app
```

**NuGet packages:**
- Core: `sqlite-net-pcl`, `SQLitePCLRaw.bundle_green`, `CommunityToolkit.Mvvm`, `System.Text.Json`
- Maui: `CommunityToolkit.Maui` (for converters/behaviors)
- Web: `Microsoft.AspNetCore.Components.WebAssembly`

Both Maui and Web reference Core.

Use `dotnet new` commands:
```bash
dotnet new sln -n ClaudeUsageTracker
dotnet new classlib -n ClaudeUsageTracker.Core -f net9.0 -o src/ClaudeUsageTracker.Core
dotnet new maui -n ClaudeUsageTracker.Maui -o src/ClaudeUsageTracker.Maui
dotnet new blazorwasm -n ClaudeUsageTracker.Web -o src/ClaudeUsageTracker.Web
dotnet sln add src/ClaudeUsageTracker.Core src/ClaudeUsageTracker.Maui src/ClaudeUsageTracker.Web
```

**Verify:** `dotnet build ClaudeUsageTracker.sln` succeeds with 0 errors.

---

### Task 2: Core models & service interfaces

**Files:** `Models/*.cs`, `Services/I*.cs`

```csharp
// Models/UsageRecord.cs
[Table("UsageRecords")]
public class UsageRecord
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public DateTime BucketStart { get; set; }
    public string Model { get; set; } = "";
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheCreationTokens { get; set; }
    public DateTime FetchedAt { get; set; }
}

// Models/CostRecord.cs
[Table("CostRecords")]
public class CostRecord
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public DateTime BucketStart { get; set; }
    public string Description { get; set; } = "";
    public decimal CostUsd { get; set; }    // stored in cents as integer, display as decimal
    public DateTime FetchedAt { get; set; }
}

// Models/AppConfig.cs — not persisted in SQLite, stored via ISecureStorageService
public class AppConfig
{
    public string AdminApiKey { get; set; } = "";
    public bool IsConfigured => !string.IsNullOrEmpty(AdminApiKey);
}
```

```csharp
// Services/ISecureStorageService.cs
public interface ISecureStorageService
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    Task RemoveAsync(string key);
}

// Services/IUsageDataService.cs
public interface IUsageDataService
{
    Task InitAsync();
    Task UpsertUsageRecordsAsync(IEnumerable<UsageRecord> records);
    Task UpsertCostRecordsAsync(IEnumerable<CostRecord> records);
    Task<List<UsageRecord>> GetUsageAsync(DateTime from, DateTime to);
    Task<List<CostRecord>> GetCostsAsync(DateTime from, DateTime to);
    Task<DateTime?> GetLastFetchedAtAsync();
}
```

**Verify:** Project compiles. No logic yet, just types and interfaces.

---

### Task 3: Anthropic API service

**Files:** `src/ClaudeUsageTracker.Core/Services/AnthropicApiService.cs`

HTTP client that calls the two Admin API endpoints and maps responses to Core models.

```csharp
public class AnthropicApiService(HttpClient http)
{
    private const string BaseUrl = "https://api.anthropic.com";
    private const string ApiVersion = "2023-06-01";

    public void SetApiKey(string adminApiKey)
    {
        http.DefaultRequestHeaders.Remove("x-api-key");
        http.DefaultRequestHeaders.Add("x-api-key", adminApiKey);
        http.DefaultRequestHeaders.Remove("anthropic-version");
        http.DefaultRequestHeaders.Add("anthropic-version", ApiVersion);
    }

    // Returns paged usage records, bucket_width=1d, group_by[]=model
    public async Task<List<UsageRecord>> FetchUsageAsync(DateTime from, DateTime to);

    // Returns cost records for the date range
    public async Task<List<CostRecord>> FetchCostsAsync(DateTime from, DateTime to);

    // Validates key by making a small 1-day request
    public async Task<bool> ValidateApiKeyAsync(string key);
}
```

Implement pagination: follow `has_more` / `next_page` until complete. Parse JSON with `System.Text.Json`. Map API fields:
- `input_tokens` → `InputTokens`
- `cache_read_input_tokens` → `CacheReadTokens`
- `cache_creation_input_tokens` → `CacheCreationTokens`
- `output_tokens` → `OutputTokens`
- Cost API: `amount` field (decimal string in cents) → `CostUsd`

**Verify:** In a scratch test or console app, call `ValidateApiKeyAsync` with a real key and confirm it returns `true`.

---

### Task 4: SQLite data service

**Files:** `src/ClaudeUsageTracker.Core/Services/UsageDataService.cs`

sqlite-net-pcl implementation of `IUsageDataService`. The DB file path is passed in via constructor (so each platform can specify its own app data directory).

```csharp
public class UsageDataService(string dbPath) : IUsageDataService
{
    private SQLiteAsyncConnection? _db;

    public async Task InitAsync()
    {
        _db = new SQLiteAsyncConnection(dbPath);
        await _db.CreateTableAsync<UsageRecord>();
        await _db.CreateTableAsync<CostRecord>();
    }

    // UpsertUsageRecords: delete matching (BucketStart, Model) then insert fresh
    // UpsertCostRecords: delete matching (BucketStart, Description) then insert fresh
    // GetUsageAsync: query WHERE BucketStart >= from AND BucketStart <= to
    // GetCostsAsync: same pattern
    // GetLastFetchedAtAsync: MAX(FetchedAt) across UsageRecords
}
```

DB path conventions per platform:
- MAUI: `FileSystem.AppDataDirectory` + `/usage.db`
- Blazor WASM: `"/data/usage.db"` (OPFS via sqlite-wasm, or use in-memory for now)

**Verify:** Unit test (or console app) that creates the DB, inserts 3 records, queries them back, confirms count = 3.

---

### Task 5: MAUI project wiring (SecureStorage + DI)

**Files:** `src/ClaudeUsageTracker.Maui/Services/MauiSecureStorageService.cs`, `MauiProgram.cs`

```csharp
// MauiSecureStorageService.cs — thin wrapper around MAUI's built-in
public class MauiSecureStorageService : ISecureStorageService
{
    public Task<string?> GetAsync(string key) => SecureStorage.Default.GetAsync(key);
    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(key, value);
    public Task RemoveAsync(string key) { SecureStorage.Default.Remove(key); return Task.CompletedTask; }
}
```

Register in `MauiProgram.cs`:
```csharp
builder.Services.AddSingleton<ISecureStorageService, MauiSecureStorageService>();
builder.Services.AddSingleton<HttpClient>();
builder.Services.AddSingleton<AnthropicApiService>();
builder.Services.AddSingleton<IUsageDataService>(_ =>
{
    var path = Path.Combine(FileSystem.AppDataDirectory, "usage.db");
    return new UsageDataService(path);
});
builder.Services.AddTransient<SetupViewModel>();
builder.Services.AddTransient<DashboardViewModel>();
```

**Verify:** App launches on Windows without DI exceptions.

---

### Task 6: First-run setup flow — ViewModel + MAUI view

**Files:** `src/ClaudeUsageTracker.Core/ViewModels/SetupViewModel.cs`, `src/ClaudeUsageTracker.Maui/Views/SetupPage.xaml(.cs)`

```csharp
// SetupViewModel.cs
public partial class SetupViewModel(
    ISecureStorageService storage,
    AnthropicApiService api,
    IUsageDataService db) : ObservableObject
{
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private bool _isValidating;
    [ObservableProperty] private string _errorMessage = "";

    [RelayCommand]
    public async Task SaveAsync()
    {
        IsValidating = true;
        ErrorMessage = "";
        bool valid = await api.ValidateApiKeyAsync(ApiKey);
        if (!valid) { ErrorMessage = "Invalid Admin API key."; IsValidating = false; return; }
        await storage.SetAsync("admin_api_key", ApiKey);
        await db.InitAsync();
        // Navigate to Dashboard
    }
}
```

`SetupPage.xaml`: entry field for API key, "Connect" button bound to `SaveAsync`, error label, brief instructions ("Enter your Anthropic Admin API key — starts with sk-ant-admin").

App startup check: on `App.xaml.cs` load, read stored key. If present → navigate to Dashboard. If not → navigate to Setup.

**Verify:** Launch app on Windows, enter invalid key → error shown. Enter valid key → navigates to Dashboard shell.

---

### Task 7: Dashboard ViewModel

**Files:** `src/ClaudeUsageTracker.Core/ViewModels/DashboardViewModel.cs`

Core stats the ViewModel exposes:
```csharp
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

    // For charts: list of (Date, Cost) per day for last 30 days
    public ObservableCollection<DailyUsage> DailyUsages { get; } = new();

    // For model breakdown: (Model, TokenCount) sorted desc
    public ObservableCollection<ModelUsage> ModelBreakdown { get; } = new();

    [RelayCommand]
    public async Task RefreshAsync()
    {
        // 1. Load API key from storage, call api.SetApiKey()
        // 2. Determine fetch range: last 31 days
        // 3. Fetch from API (usage + cost)
        // 4. Upsert into local DB
        // 5. Query DB for aggregated stats
        // 6. Populate observable collections
    }
}

public record DailyUsage(DateTime Date, decimal CostUsd, long Tokens);
public record ModelUsage(string Model, long Tokens, decimal CostUsd);
```

Refresh guard: skip API call if `GetLastFetchedAtAsync()` < 1 minute ago (respect API polling guidance).

**Verify:** In a headless unit test (mock `ISecureStorageService` + `IUsageDataService`, stub `AnthropicApiService`), call `RefreshAsync` and assert `TodayCostUsd` and `DailyUsages.Count` are populated correctly.

---

### Task 8: MAUI Windows full dashboard view

**Files:** `src/ClaudeUsageTracker.Maui/Views/DashboardPage.xaml(.cs)`

Layout (Windows desktop, shown when `DeviceInfo.Idiom == DeviceIdiom.Desktop`):

```
┌─────────────────────────────────────────────┐
│  Claude Usage Tracker          [Refresh]     │
├──────────────┬──────────────┬────────────────┤
│ Today        │ This Month   │ Last Updated   │
│ $X.XX        │ $XX.XX       │ 2 min ago      │
├──────────────┴──────────────┴────────────────┤
│  [Bar chart — daily cost last 30 days]       │
├─────────────────────────────────────────────┤
│  Model Breakdown                             │
│  claude-opus-4-6     ████████  42%          │
│  claude-sonnet-4-6   █████     28%          │
│  ...                                         │
└─────────────────────────────────────────────┘
```

Use `CollectionView` for model breakdown. For chart, use a simple `GraphicsView` + `IDrawable` rendering bars — no charting library needed for this scope.

Bind `BindingContext` to `DashboardViewModel`, call `RefreshCommand` on `OnAppearing`.

**Verify:** App on Windows shows populated cards and bar chart after refresh (with real API key).

---

### Task 9: MAUI Android/iOS compact mobile view

**Files:** `src/ClaudeUsageTracker.Maui/Views/MobileDashboardPage.xaml(.cs)`

Layout (shown when `DeviceInfo.Idiom == DeviceIdiom.Phone`):

```
┌─────────────────────┐
│  Claude Usage        │
│  Today: $X.XX        │
│  This month: $XX.XX  │
│  ─────────────────── │
│  Top model:          │
│  claude-opus-4-6     │
│  ─────────────────── │
│  [Refresh]           │
└─────────────────────┘
```

Reuses the same `DashboardViewModel`. Route decision in `AppShell.xaml.cs` or `App.xaml.cs`:
```csharp
MainPage = DeviceInfo.Idiom == DeviceIdiom.Phone
    ? new MobileDashboardPage(vm)
    : new DashboardPage(vm);
```

**Verify:** Run on Android emulator — compact card layout appears, values match Windows dashboard.

---

### Task 10: Blazor WASM — storage + setup page

**Files:** `src/ClaudeUsageTracker.Web/Services/BrowserSecureStorageService.cs`, `wwwroot/storage.js`, `Pages/Setup.razor`, `Program.cs`

JS helper (`storage.js`):
```js
window.secureStorage = {
    set: (key, value) => localStorage.setItem(key, value),
    get: (key) => localStorage.getItem(key),
    remove: (key) => localStorage.removeItem(key)
};
```

`BrowserSecureStorageService.cs`:
```csharp
public class BrowserSecureStorageService(IJSRuntime js) : ISecureStorageService
{
    public async Task<string?> GetAsync(string key) =>
        await js.InvokeAsync<string?>("secureStorage.get", key);
    public async Task SetAsync(string key, string value) =>
        await js.InvokeVoidAsync("secureStorage.set", key, value);
    public async Task RemoveAsync(string key) =>
        await js.InvokeVoidAsync("secureStorage.remove", key);
}
```

`Setup.razor`: same UX as MAUI SetupPage, binds to `SetupViewModel`. On save, navigate to `/dashboard`.

`Program.cs` — register same services as MAUI but with `BrowserSecureStorageService` and an in-memory or OPFS SQLite path.

**Verify:** Browser app loads, API key entry form appears, valid key accepted → navigates to `/dashboard` URL.

---

### Task 11: Blazor WASM dashboard page

**Files:** `src/ClaudeUsageTracker.Web/Pages/Dashboard.razor`

Same layout as MAUI Windows dashboard, using standard Blazor HTML/CSS:
```html
<div class="stats-row">
    <div class="card">Today: @vm.TodayCostUsd.ToString("C")</div>
    <div class="card">This Month: @vm.MonthCostUsd.ToString("C")</div>
</div>
<div class="chart">
    <!-- SVG bar chart rendered from vm.DailyUsages -->
</div>
<div class="model-breakdown">
    @foreach (var m in vm.ModelBreakdown)
    {
        <div>@m.Model — @m.Tokens.ToString("N0") tokens — @m.CostUsd.ToString("C")</div>
    }
</div>
```

Render bar chart as inline SVG from `DailyUsages` — no external charting package needed.

**Verify:** Browser dashboard displays stats matching the MAUI desktop version when using the same API key.

---

## Notes

- Admin API key is required — standard API keys will not work. Users must have Organization Admin role in the Anthropic Console.
- No auto-refresh/polling in background — manual refresh only. Respect the 1-per-minute API guideline.
- SQLite in Blazor WASM is limited — for this first version, use in-memory storage and refetch each session. Can be upgraded to OPFS-backed sqlite-wasm later without changing the interface.
- The `cost_report` endpoint does not include Priority Tier costs — document this clearly in the UI.
