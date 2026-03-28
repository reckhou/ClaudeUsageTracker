# Weekly Stats, Claude Pro Quota & Setup Revamp — Implementation Plan

**Goal:** Add weekly billing stats + Claude Pro plan quota display; revamp setup so Admin API and Claude Pro are independent optional connections; remove Blazor WASM; document the claude.ai internal API discovery.

**Architecture:**
- Both data sources (Admin API key, Claude Pro session) are fully independent and optional. Dashboard adapts to show only what is connected.
- Setup page becomes a persistent "connections" screen with two independent sections — each can be connected/disconnected without affecting the other.
- App startup: go to Dashboard if either source is connected; go to Setup only if neither is configured.
- Claude Pro quota is fetched via a hidden WebView in MAUI that navigates to `claude.ai`, injects JS to call `GET /api/organizations/{uuid}/usage` using the browser session, and returns clean JSON — no DOM scraping required.
- Blazor WASM is removed entirely (CORS blocks claude.ai session cookies; Admin API-only use case no longer justifies maintaining it).

**Tech Stack:** C# 12 / .NET 9 · .NET MAUI · sqlite-net-pcl · CommunityToolkit.Mvvm

---

## Progress

- [x] Task 12: Remove Blazor WASM project from solution
- [x] Task 13: Revamp setup — dual independent optional sources
- [x] Task 14: QuotaRecord model + storage + DashboardViewModel updates (weekly + quota)
- [x] Task 15: MAUI Claude Pro WebView connection + dashboard quota cards
- [ ] Task 16: Document claude.ai internal usage API

---

## Files

### Remove
- Delete: `src/ClaudeUsageTracker.Web/` — entire Blazor WASM project
- Modify: `ClaudeUsageTracker.sln` — remove Web project reference

### Modify (Core)
- Modify: `src/ClaudeUsageTracker.Core/Models/AppConfig.cs` — add `ClaudeProConnected` flag if needed
- Create: `src/ClaudeUsageTracker.Core/Models/QuotaRecord.cs` — SQLite model for claude.ai quota
- Modify: `src/ClaudeUsageTracker.Core/Services/IUsageDataService.cs` — add quota CRUD methods
- Modify: `src/ClaudeUsageTracker.Core/Services/UsageDataService.cs` — implement quota methods
- Create: `src/ClaudeUsageTracker.Core/Services/IClaudeAiUsageService.cs` — interface for quota fetching
- Modify: `src/ClaudeUsageTracker.Core/ViewModels/SetupViewModel.cs` — dual independent connections
- Modify: `src/ClaudeUsageTracker.Core/ViewModels/DashboardViewModel.cs` — weekly stats + quota properties, both optional

### Modify (MAUI)
- Modify: `src/ClaudeUsageTracker.Maui/Views/SetupPage.xaml(.cs)` — dual-source layout
- Modify: `src/ClaudeUsageTracker.Maui/Views/DashboardPage.xaml` — weekly card + quota cards (conditional)
- Modify: `src/ClaudeUsageTracker.Maui/Views/MobileDashboardPage.xaml` — same
- Create: `src/ClaudeUsageTracker.Maui/Services/ClaudeAiUsageService.cs` — WebView-based quota fetcher
- Modify: `src/ClaudeUsageTracker.Maui/App.xaml.cs` — updated startup routing
- Modify: `src/ClaudeUsageTracker.Maui/AppShell.xaml` — add Settings route
- Modify: `src/ClaudeUsageTracker.Maui/MauiProgram.cs` — register new services

### Create (Docs)
- Create: `docs/claude-ai-usage-api.md` — internal API discovery reference

---

### Task 12: Remove Blazor WASM

**Files:** `ClaudeUsageTracker.sln`, `src/ClaudeUsageTracker.Web/` (delete)

Remove the Web project from the solution:
```bash
dotnet sln remove src/ClaudeUsageTracker.Web/ClaudeUsageTracker.Web.csproj
```
Then delete the `src/ClaudeUsageTracker.Web/` directory entirely.

**Verify:** `dotnet build ClaudeUsageTracker.sln` succeeds with no Web project references.

---

### Task 13: Revamp setup — dual independent optional sources

**Files:** `SetupViewModel.cs`, `SetupPage.xaml`, `SetupPage.xaml.cs`, `App.xaml.cs`, `AppShell.xaml`

**`SetupViewModel`** — two independent sections, each with own state:
```csharp
public partial class SetupViewModel(
    ISecureStorageService storage,
    AnthropicApiService api,
    IUsageDataService db) : ObservableObject
{
    // Admin API section
    [ObservableProperty] private string _adminApiKey = "";
    [ObservableProperty] private bool _isValidatingApi;
    [ObservableProperty] private string _apiError = "";
    [ObservableProperty] private bool _hasApiError;
    [ObservableProperty] private bool _isApiConnected;

    // Claude Pro section
    [ObservableProperty] private bool _isClaudeProConnected;
    [ObservableProperty] private string _claudeProStatus = "Not connected";

    public event Action? NavigateToDashboard;

    public async Task LoadAsync()
    {
        var key = await storage.GetAsync("admin_api_key");
        IsApiConnected = !string.IsNullOrEmpty(key);
        AdminApiKey = IsApiConnected ? "••••••••••••" : "";

        var proConnected = await storage.GetAsync("claude_pro_connected");
        IsClaudeProConnected = proConnected == "true";
        ClaudeProStatus = IsClaudeProConnected ? "Connected" : "Not connected";
    }

    [RelayCommand]
    public async Task SaveApiKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(AdminApiKey) || AdminApiKey.StartsWith("•")) return;
        IsValidatingApi = true; ApiError = ""; HasApiError = false;
        var (valid, error) = await api.ValidateApiKeyAsync(AdminApiKey);
        if (!valid) { ApiError = error ?? "Unknown error"; HasApiError = true; IsValidatingApi = false; return; }
        await storage.SetAsync("admin_api_key", AdminApiKey);
        await db.InitAsync();
        IsApiConnected = true;
        IsValidatingApi = false;
    }

    [RelayCommand]
    public async Task DisconnectApiAsync()
    {
        await storage.RemoveAsync("admin_api_key");
        IsApiConnected = false;
        AdminApiKey = "";
    }

    [RelayCommand]
    public void GoToDashboard() => NavigateToDashboard?.Invoke();
}
```

Note: `ISecureStorageService` needs a `RemoveAsync` method added (see below).

Add to `ISecureStorageService`:
```csharp
Task RemoveAsync(string key);
```
Implement in `MauiSecureStorageService`:
```csharp
public Task RemoveAsync(string key) { SecureStorage.Default.Remove(key); return Task.CompletedTask; }
```

**`SetupPage.xaml`** — two visually separated cards, each independently actionable. "Go to Dashboard" button always visible at the bottom:
```xml
<ScrollView>
  <VerticalStackLayout Padding="24" Spacing="20">

    <Label Text="Claude Usage Tracker" FontSize="26" FontAttributes="Bold" HorizontalOptions="Center" />
    <Label Text="Connect one or both sources. Both are optional."
           FontSize="13" HorizontalOptions="Center" HorizontalTextAlignment="Center"
           TextColor="{AppThemeBinding Light={StaticResource Gray500}, Dark={StaticResource Gray400}}" />

    <!-- Admin API Card -->
    <Frame CornerRadius="10" Padding="20"
           BackgroundColor="{AppThemeBinding Light={StaticResource Gray100}, Dark={StaticResource Gray900}}">
      <VerticalStackLayout Spacing="12">
        <Label Text="Anthropic Admin API" FontSize="16" FontAttributes="Bold" />
        <Label Text="Tracks API billing costs and token usage across your organisation."
               FontSize="12" TextColor="{AppThemeBinding Light={StaticResource Gray500}, Dark={StaticResource Gray400}}" />

        <Grid IsVisible="{Binding IsApiConnected, Converter={StaticResource InvertedBoolConverter}}"
              ColumnDefinitions="*,Auto" ColumnSpacing="8">
          <Entry Grid.Column="0" Placeholder="sk-ant-admin..." IsPassword="True"
                 Text="{Binding AdminApiKey}" ReturnCommand="{Binding SaveApiKeyCommand}" />
          <Button Grid.Column="1" Text="Connect" Command="{Binding SaveApiKeyCommand}"
                  IsEnabled="{Binding IsValidatingApi, Converter={StaticResource InvertedBoolConverter}}" />
        </Grid>

        <Grid IsVisible="{Binding IsApiConnected}" ColumnDefinitions="*,Auto">
          <Label Grid.Column="0" Text="✓ Connected" VerticalOptions="Center"
                 TextColor="{StaticResource Primary}" />
          <Button Grid.Column="1" Text="Disconnect" Command="{Binding DisconnectApiCommand}"
                  BackgroundColor="Transparent"
                  TextColor="{AppThemeBinding Light={StaticResource Gray500}, Dark={StaticResource Gray400}}"
                  FontSize="12" />
        </Grid>

        <Grid IsVisible="{Binding HasApiError}" ColumnDefinitions="*,Auto">
          <Label Grid.Column="0" Text="{Binding ApiError}" TextColor="Red" FontSize="12" />
          <Button Grid.Column="1" Text="Copy" FontSize="11" Padding="8,4"
                  Clicked="OnCopyApiErrorClicked" BackgroundColor="Transparent" TextColor="Red" />
        </Grid>
        <ActivityIndicator IsRunning="{Binding IsValidatingApi}" IsVisible="{Binding IsValidatingApi}" />
      </VerticalStackLayout>
    </Frame>

    <!-- Claude Pro Card -->
    <Frame CornerRadius="10" Padding="20"
           BackgroundColor="{AppThemeBinding Light={StaticResource Gray100}, Dark={StaticResource Gray900}}">
      <VerticalStackLayout Spacing="12">
        <Label Text="Claude Pro Plan" FontSize="16" FontAttributes="Bold" />
        <Label Text="Shows current session and weekly quota usage for your Claude.ai account."
               FontSize="12" TextColor="{AppThemeBinding Light={StaticResource Gray500}, Dark={StaticResource Gray400}}" />
        <Label Text="{Binding ClaudeProStatus}" FontSize="13" />
        <Button x:Name="ConnectClaudeProButton" Text="Connect Claude Pro"
                Clicked="OnConnectClaudeProClicked"
                IsVisible="{Binding IsClaudeProConnected, Converter={StaticResource InvertedBoolConverter}}" />
        <Button Text="Disconnect" Command="{Binding DisconnectClaudeProCommand}"
                IsVisible="{Binding IsClaudeProConnected}"
                BackgroundColor="Transparent"
                TextColor="{AppThemeBinding Light={StaticResource Gray500}, Dark={StaticResource Gray400}}"
                FontSize="12" />
      </VerticalStackLayout>
    </Frame>

    <Button Text="Go to Dashboard" Command="{Binding GoToDashboardCommand}"
            HorizontalOptions="Fill" Margin="0,8,0,0" />

  </VerticalStackLayout>
</ScrollView>
```

The "Connect Claude Pro" button is handled in `SetupPage.xaml.cs` codebehind — it calls the `ClaudeAiUsageService` which manages the WebView modal (implemented in Task 15).

**`App.xaml.cs`** — go to dashboard if either source connected:
```csharp
protected override async void OnStart()
{
    base.OnStart();
    try
    {
        var key = await _storage.GetAsync("admin_api_key");
        var pro = await _storage.GetAsync("claude_pro_connected");
        if (!string.IsNullOrEmpty(key) || pro == "true")
            await Shell.Current.GoToAsync("//dashboard");
    }
    catch { }
}
```

**`AppShell.xaml`** — add a settings route so the dashboard can navigate back:
```xml
<ShellContent Title="Setup" ContentTemplate="{DataTemplate views:SetupPage}" Route="setup" />
<ShellContent Title="Dashboard" ContentTemplate="{DataTemplate views:DashboardPage}" Route="dashboard" />
```

**Verify:** Fresh install → Setup shows both cards, both disconnected. Connect Admin API only → "Go to Dashboard" works, dashboard loads. Kill and reopen → goes directly to dashboard. Connect neither → stays on Setup.

---

### Task 14: QuotaRecord model + storage + DashboardViewModel updates

**Files:** `QuotaRecord.cs` (new), `IUsageDataService.cs`, `UsageDataService.cs`, `DashboardViewModel.cs`
**Depends on:** Task 13

**`QuotaRecord.cs`** — matches the `GET /api/organizations/{uuid}/usage` response:
```csharp
[Table("QuotaRecords")]
public class QuotaRecord
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int FiveHourUtilization { get; set; }    // current session %, 0–100
    public DateTime FiveHourResetsAt { get; set; }
    public int SevenDayUtilization { get; set; }    // weekly %, 0–100
    public DateTime SevenDayResetsAt { get; set; }
    public bool ExtraUsageEnabled { get; set; }
    public int ExtraUsageUtilization { get; set; }  // 0 if not enabled
    public DateTime FetchedAt { get; set; }
}
```

Add to `IUsageDataService`:
```csharp
Task UpsertQuotaRecordAsync(QuotaRecord record);
Task<QuotaRecord?> GetLatestQuotaAsync();
```

Implement in `UsageDataService`:
- `InitAsync`: add `await _db.CreateTableAsync<QuotaRecord>()`
- `UpsertQuotaRecordAsync`: `await _db.ExecuteAsync("DELETE FROM QuotaRecords")` then `await _db.InsertAsync(record)` (keep only latest)
- `GetLatestQuotaAsync`: `return await _db.Table<QuotaRecord>().OrderByDescending(r => r.FetchedAt).FirstOrDefaultAsync()`

**`DashboardViewModel`** — add weekly (Admin API) + quota (Claude Pro) as optional independent properties:

New constructor parameter (optional):
```csharp
public partial class DashboardViewModel(
    ISecureStorageService storage,
    AnthropicApiService api,
    IUsageDataService db,
    IClaudeAiUsageService? claudeAi = null) : ObservableObject
```

New properties:
```csharp
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
```

In `RefreshAsync`, wrap Admin API fetch in a check — only attempt if key exists. In `LoadFromDbAsync`:

Weekly billing calc (Admin API):
```csharp
var daysFromMon = ((int)today.DayOfWeek + 6) % 7;
var weekStart = today.AddDays(-daysFromMon);
WeekCostUsd = costRecords.Where(r => r.BucketStart.Date >= weekStart).Sum(r => r.CostUsd);
WeekTotalTokens = usageRecords.Where(r => r.BucketStart.Date >= weekStart)
    .Sum(r => r.InputTokens + r.OutputTokens + r.CacheReadTokens + r.CacheCreationTokens);
HasAdminApiData = costRecords.Any();
```

Quota load from DB:
```csharp
var quota = await db.GetLatestQuotaAsync();
HasQuota = quota != null;
if (quota != null)
{
    SessionUtilization = quota.FiveHourUtilization;
    SessionResetsAt = FormatResetsAt(quota.FiveHourResetsAt);
    WeeklyUtilization = quota.SevenDayUtilization;
    WeeklyResetsAt = FormatResetsAt(quota.SevenDayResetsAt);
}
```

```csharp
private static string FormatResetsAt(DateTime utc)
{
    var diff = utc - DateTime.UtcNow;
    if (diff <= TimeSpan.Zero) return "Resetting…";
    if (diff.TotalHours < 1) return $"Resets in {(int)diff.TotalMinutes} min";
    if (diff.TotalHours < 24) return $"Resets in {(int)diff.TotalHours} hr {diff.Minutes} min";
    return $"Resets {utc.ToLocalTime():ddd h:mm tt}";
}
```

Add a `RefreshQuotaCommand` for the dashboard's "refresh quota" button:
```csharp
[RelayCommand]
public async Task RefreshQuotaAsync()
{
    if (_claudeAi == null) return;
    var record = await _claudeAi.FetchQuotaAsync();
    if (record == null) return;
    await db.InitAsync();
    await db.UpsertQuotaRecordAsync(record);
    await LoadFromDbAsync();
}
```

**Verify:** App builds. With no quota data in DB, `HasQuota = false` and quota cards are hidden. Weekly calc is correct (≥ today, ≤ month).

---

### Task 15: MAUI Claude Pro WebView connection + dashboard quota cards

**Files:** `IClaudeAiUsageService.cs` (new in Core), `ClaudeAiUsageService.cs` (new in MAUI/Services), `SetupPage.xaml.cs`, `SetupViewModel.cs` (minor additions), `DashboardPage.xaml`, `MobileDashboardPage.xaml`, `MauiProgram.cs`
**Depends on:** Task 14

**`IClaudeAiUsageService.cs`** (Core):
```csharp
public interface IClaudeAiUsageService
{
    /// <summary>Show WebView auth flow and fetch quota. Returns null if auth fails or cancelled.</summary>
    Task<QuotaRecord?> ConnectAndFetchAsync();

    /// <summary>Silently re-fetch quota using existing session. Returns null if session expired.</summary>
    Task<QuotaRecord?> FetchQuotaAsync();
}
```

**`ClaudeAiUsageService.cs`** (MAUI) — creates a transient WebView on a modal page:

```csharp
public class ClaudeAiUsageService(IUsageDataService db) : IClaudeAiUsageService
{
    public async Task<QuotaRecord?> ConnectAndFetchAsync()
    {
        var page = new ClaudeProWebViewPage();
        await Application.Current!.MainPage!.Navigation.PushModalAsync(page);
        var record = await page.WaitForResultAsync();
        await Application.Current.MainPage.Navigation.PopModalAsync();
        return record;
    }

    public async Task<QuotaRecord?> FetchQuotaAsync()
    {
        // Reuse a non-visible WebView for silent refresh
        var tcs = new TaskCompletionSource<QuotaRecord?>();
        var page = new ClaudeProWebViewPage(silent: true);
        // Push invisibly — user sees a brief loading overlay at most
        await Application.Current!.MainPage!.Navigation.PushModalAsync(page, animated: false);
        var record = await page.WaitForResultAsync(timeoutMs: 10_000);
        await Application.Current.MainPage.Navigation.PopModalAsync(animated: false);
        return record;
    }
}
```

Create **`ClaudeProWebViewPage.cs`** (code-only page, no XAML needed):

```csharp
public class ClaudeProWebViewPage : ContentPage
{
    private readonly WebView _webView;
    private readonly Label _statusLabel;
    private readonly TaskCompletionSource<QuotaRecord?> _tcs = new();
    private readonly bool _silent;
    private bool _extracted;

    public ClaudeProWebViewPage(bool silent = false)
    {
        _silent = silent;
        _webView = new WebView { Source = new UrlWebViewSource { Url = "https://claude.ai/settings/usage" } };
        _webView.Navigated += OnNavigated;

        _statusLabel = new Label
        {
            Text = "Loading claude.ai…",
            Padding = new Thickness(16, 8),
            FontSize = 12
        };

        if (silent)
        {
            // Minimal UI — user should barely notice
            Content = new Grid { Children = { _webView, _statusLabel }, IsVisible = false };
        }
        else
        {
            // Full UI — user sees the page and can log in if needed
            Title = "Connect Claude Pro";
            var closeBtn = new Button { Text = "Cancel", HorizontalOptions = LayoutOptions.End };
            closeBtn.Clicked += (_, _) => _tcs.TrySetResult(null);
            Content = new Grid
            {
                RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
                Children =
                {
                    _webView.Row(0),
                    _statusLabel.Row(1),
                    closeBtn.Row(2)
                }
            };
        }
    }

    public Task<QuotaRecord?> WaitForResultAsync(int timeoutMs = 60_000)
    {
        // Auto-cancel on timeout
        _ = Task.Delay(timeoutMs).ContinueWith(_ => _tcs.TrySetResult(null));
        return _tcs.Task;
    }

    private async void OnNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result != WebNavigationResult.Success) return;
        if (!e.Url.StartsWith("https://claude.ai")) return;
        if (_extracted) return;

        _statusLabel.Text = "Fetching quota data…";
        await Task.Delay(1500); // wait for React hydration

        // JS that calls the internal API using the existing session cookies
        const string js = """
            (async () => {
                try {
                    const orgsResp = await fetch('/api/organizations', { credentials: 'include' });
                    if (!orgsResp.ok) return JSON.stringify({ error: 'orgs:' + orgsResp.status });
                    const orgs = await orgsResp.json();
                    const uuid = orgs[0]?.uuid;
                    if (!uuid) return JSON.stringify({ error: 'no uuid' });
                    const usageResp = await fetch(`/api/organizations/${uuid}/usage`, { credentials: 'include' });
                    if (!usageResp.ok) return JSON.stringify({ error: 'usage:' + usageResp.status });
                    const data = await usageResp.json();
                    return JSON.stringify({ ok: true, data });
                } catch (ex) {
                    return JSON.stringify({ error: ex.message });
                }
            })()
            """;

        var raw = await _webView.EvaluateJavaScriptAsync(js);
        if (string.IsNullOrEmpty(raw)) { _tcs.TrySetResult(null); return; }

        // MAUI returns JS strings with escape sequences — unescape
        var json = System.Text.RegularExpressions.Regex.Unescape(raw.Trim('"'));
        var record = ParseUsageResponse(json);

        if (record != null)
        {
            _extracted = true;
            _statusLabel.Text = $"Session: {record.FiveHourUtilization}% · Weekly: {record.SevenDayUtilization}%";
            _tcs.TrySetResult(record);
        }
        else
        {
            _statusLabel.Text = $"Could not parse response. Try logging in at claude.ai first.";
            if (_silent) _tcs.TrySetResult(null);
        }
    }

    internal static QuotaRecord? ParseUsageResponse(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out _)) return null;
            if (!root.TryGetProperty("data", out var data)) return null;

            var record = new QuotaRecord { FetchedAt = DateTime.UtcNow };

            if (data.TryGetProperty("five_hour", out var fh) && fh.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                record.FiveHourUtilization = fh.GetProperty("utilization").GetInt32();
                record.FiveHourResetsAt = fh.GetProperty("resets_at").GetDateTime().ToUniversalTime();
            }
            if (data.TryGetProperty("seven_day", out var sd) && sd.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                record.SevenDayUtilization = sd.GetProperty("utilization").GetInt32();
                record.SevenDayResetsAt = sd.GetProperty("resets_at").GetDateTime().ToUniversalTime();
            }
            if (data.TryGetProperty("extra_usage", out var eu) && eu.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                record.ExtraUsageEnabled = eu.TryGetProperty("is_enabled", out var ie) && ie.ValueKind == System.Text.Json.JsonValueKind.True;
                if (eu.TryGetProperty("utilization", out var u) && u.ValueKind == System.Text.Json.JsonValueKind.Number)
                    record.ExtraUsageUtilization = u.GetInt32();
            }
            return record;
        }
        catch { return null; }
    }
}
```

**`SetupPage.xaml.cs`** — wire up the Connect Claude Pro button:
```csharp
private async void OnConnectClaudeProClicked(object sender, EventArgs e)
{
    var service = Handler?.MauiContext?.Services.GetService<IClaudeAiUsageService>();
    if (service == null) return;
    var record = await service.ConnectAndFetchAsync();
    if (record != null)
    {
        var storage = Handler?.MauiContext?.Services.GetService<ISecureStorageService>();
        if (storage != null) await storage.SetAsync("claude_pro_connected", "true");
        var db = Handler?.MauiContext?.Services.GetService<IUsageDataService>();
        if (db != null) { await db.InitAsync(); await db.UpsertQuotaRecordAsync(record); }
        ViewModel.IsClaudeProConnected = true;
        ViewModel.ClaudeProStatus = $"Connected · Session {record.FiveHourUtilization}% · Weekly {record.SevenDayUtilization}%";
    }
}
```

Add `DisconnectClaudeProCommand` to `SetupViewModel`:
```csharp
[RelayCommand]
public async Task DisconnectClaudeProAsync()
{
    await storage.RemoveAsync("claude_pro_connected");
    IsClaudeProConnected = false;
    ClaudeProStatus = "Not connected";
}
```

**`DashboardPage.xaml`** — add weekly card (Admin API) and quota cards (Claude Pro), both conditional:

Expand stats row to 4 columns and add Week card. Update Row numbering to make room for quota row.

Weekly card (visible when `HasAdminApiData`):
```xml
<Frame Grid.Column="1" IsVisible="{Binding HasAdminApiData}" CornerRadius="8" Padding="16"
       BackgroundColor="{AppThemeBinding Light={StaticResource Gray100}, Dark={StaticResource Gray900}}">
    <VerticalStackLayout>
        <Label Text="This Week" FontSize="12"
               TextColor="{AppThemeBinding Light={StaticResource Gray600}, Dark={StaticResource Gray400}}" />
        <Label Text="{Binding WeekCostUsd, StringFormat='{0:C}'}" FontSize="24" FontAttributes="Bold" />
        <Label Text="{Binding WeekTotalTokens, StringFormat='{0:N0} tok'}" FontSize="11"
               TextColor="{AppThemeBinding Light={StaticResource Gray600}, Dark={StaticResource Gray400}}" />
    </VerticalStackLayout>
</Frame>
```

Claude Pro quota row (visible when `HasQuota`):
```xml
<Grid Grid.Row="2" ColumnDefinitions="*,*" ColumnSpacing="12" IsVisible="{Binding HasQuota}">
    <Frame Grid.Column="0" CornerRadius="8" Padding="16"
           BackgroundColor="{AppThemeBinding Light={StaticResource Gray100}, Dark={StaticResource Gray900}}">
        <VerticalStackLayout>
            <Label Text="Current Session" FontSize="12"
                   TextColor="{AppThemeBinding Light={StaticResource Gray600}, Dark={StaticResource Gray400}}" />
            <Label Text="{Binding SessionUtilization, StringFormat='{0}% used'}"
                   FontSize="22" FontAttributes="Bold" />
            <Label Text="{Binding SessionResetsAt}" FontSize="11"
                   TextColor="{AppThemeBinding Light={StaticResource Gray600}, Dark={StaticResource Gray400}}" />
        </VerticalStackLayout>
    </Frame>
    <Frame Grid.Column="1" CornerRadius="8" Padding="16"
           BackgroundColor="{AppThemeBinding Light={StaticResource Gray100}, Dark={StaticResource Gray900}}">
        <VerticalStackLayout>
            <Label Text="Weekly Limit" FontSize="12"
                   TextColor="{AppThemeBinding Light={StaticResource Gray600}, Dark={StaticResource Gray400}}" />
            <Label Text="{Binding WeeklyUtilization, StringFormat='{0}% used'}"
                   FontSize="22" FontAttributes="Bold" />
            <Label Text="{Binding WeeklyResetsAt}" FontSize="11"
                   TextColor="{AppThemeBinding Light={StaticResource Gray600}, Dark={StaticResource Gray400}}" />
        </VerticalStackLayout>
    </Frame>
</Grid>
```

Add a "Settings" button to the dashboard header so users can reconnect/disconnect sources.

Apply the same quota cards to `MobileDashboardPage.xaml`.

**`MauiProgram.cs`** — register new services:
```csharp
builder.Services.AddSingleton<IClaudeAiUsageService, ClaudeAiUsageService>();
builder.Services.AddTransient<DashboardViewModel>(sp => new DashboardViewModel(
    sp.GetRequiredService<ISecureStorageService>(),
    sp.GetRequiredService<AnthropicApiService>(),
    sp.GetRequiredService<IUsageDataService>(),
    sp.GetRequiredService<IClaudeAiUsageService>()));
```

**Verify:**
1. Connect only Claude Pro → dashboard shows quota cards, no billing cards
2. Connect only Admin API → dashboard shows billing + weekly cards, no quota cards
3. Connect both → all cards visible
4. Tap "Settings" from dashboard → setup page opens showing both connected states
5. Disconnect Claude Pro → quota cards disappear from dashboard after next load

---

### Task 16: Document claude.ai internal usage API

**Files:** `docs/claude-ai-usage-api.md` (new)

Create a reference document capturing exactly how the quota API was discovered and how it works, so the integration can be repaired if Anthropic changes the page.

The document should cover:
- **How it was discovered**: navigated to `claude.ai/settings/usage` using the `claude-in-chrome` browser tool, read network requests with `urlPattern: "claude.ai"` to find the API calls firing on page load, then used `EvaluateJavaScriptAsync`-equivalent JS in the browser context to call the endpoints directly and inspect the response.
- **Endpoints used**: `GET /api/organizations` (org UUID discovery) and `GET /api/organizations/{uuid}/usage` (quota data)
- **Full response shape** with field descriptions
- **Auth mechanism**: session cookies (`credentials: 'include'`) — no API key
- **Known nullable fields**: `seven_day_oauth_apps`, `seven_day_opus`, `seven_day_sonnet`, `seven_day_cowork`, `iguana_necktie` (all null on personal Pro plan)
- **How to re-discover** if the page changes: open DevTools Network tab on `claude.ai/settings/usage`, filter XHR/Fetch, reload page, look for calls returning JSON with `utilization` fields
- **Fragility warning**: this is an undocumented internal API — field names or endpoint paths may change without notice

**Verify:** File exists at `docs/claude-ai-usage-api.md` with all sections filled in.
