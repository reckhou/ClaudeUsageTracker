# Google AI Studio Usage Tracking — Implementation Plan

**Goal:** Add Google AI (Gemini) API usage tracking to the MAUI app, showing per-model cost, request counts, and token usage via WebView DOM scraping, with multi-project support and a dropdown to switch between per-project and total views.

**Architecture:** A new `GoogleAiWebViewPage` navigates to `aistudio.google.com/usage` and `/spend`, injects JS to click "Populate data" buttons and extract accessibility table data + spend text. A new `GoogleAiUsageRecord` model stores per-model breakdowns (requests, input tokens, cost) in SQLite. A dedicated `GoogleAiCardViewModel` renders as a distinct card type on the dashboard (not reusing the quota progress-bar card). The mini mode shows only 24h cost and 24h token totals. Refresh is hardcoded to 30 minutes for Google AI (separate from the existing auto-refresh interval), with manual refresh still available.

**Tech Stack:** C# / .NET MAUI, WebView2 (DOM scraping via `EvaluateJavaScriptAsync`), SQLite (sqlite-net-pcl), CommunityToolkit.Mvvm

---

## Progress

- [x] Task 1: Data model + SQLite storage + WebView scraping
- [x] Task 2: Dashboard card, mini mode, setup UI, and DI wiring

---

## Files

- **Create:** `src/ClaudeUsageTracker.Core/Models/GoogleAiUsageRecord.cs` — per-model usage snapshot (requests, tokens, cost) stored in SQLite
- **Create:** `src/ClaudeUsageTracker.Maui/Views/GoogleAiWebViewPage.cs` — WebView page that navigates to aistudio.google.com, injects JS, extracts data from Usage + Spend pages
- **Create:** `src/ClaudeUsageTracker.Maui/ViewModels/GoogleAiCardViewModel.cs` — ViewModel for the Google AI card on the dashboard and mini mode
- **Modify:** `src/ClaudeUsageTracker.Core/Services/UsageDataService.cs` — add table creation + CRUD for `GoogleAiUsageRecord`
- **Modify:** `src/ClaudeUsageTracker.Core/Services/IUsageDataService.cs` — add interface methods for Google AI records
- **Modify:** `src/ClaudeUsageTracker.Maui/ViewModels/ProvidersDashboardViewModel.cs` — add Google AI card management, 30-min auto-refresh timer, project dropdown state
- **Modify:** `src/ClaudeUsageTracker.Maui/Views/ProvidersDashboardPage.xaml` — add Google AI card template + hidden WebView
- **Modify:** `src/ClaudeUsageTracker.Maui/Views/ProvidersDashboardPage.xaml.cs` — expose WebView for silent refresh
- **Modify:** `src/ClaudeUsageTracker.Maui/Views/MiniModePage.xaml` — add Google AI compact display (24h cost + 24h tokens)
- **Modify:** `src/ClaudeUsageTracker.Maui/ViewModels/MiniModeViewModel.cs` — expose Google AI mini data
- **Modify:** `src/ClaudeUsageTracker.Maui/Views/SetupPage.xaml` — add Google AI Studio connection card
- **Modify:** `src/ClaudeUsageTracker.Core/ViewModels/SetupViewModel.cs` — add Google AI connection state + project list management
- **Modify:** `src/ClaudeUsageTracker.Maui/MauiProgram.cs` — DI registration for Google AI services

---

### Task 1: Data Model + SQLite Storage + WebView Scraping

**Files:** `GoogleAiUsageRecord.cs`, `UsageDataService.cs`, `IUsageDataService.cs`, `GoogleAiWebViewPage.cs`

#### 1a. Data Model

`GoogleAiUsageRecord` stores one row per model per project per fetch. Unlike `ProviderUsageRecord` (which is a flat quota gauge), this captures per-model API metrics.

```csharp
// src/ClaudeUsageTracker.Core/Models/GoogleAiUsageRecord.cs
using SQLite;

namespace ClaudeUsageTracker.Core.Models;

[Table("GoogleAiUsageRecords")]
public class GoogleAiUsageRecord
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string ProjectId { get; set; } = "";        // Google Cloud project ID
    public string ModelName { get; set; } = "";         // e.g. "gemini-2.5-flash-lite"
    public string TimeRange { get; set; } = "";         // "last-1-day", "last-7-days", "last-28-days"
    public long RequestCount { get; set; }              // total requests in time range
    public long InputTokens { get; set; }               // total input tokens in time range
    public decimal Cost { get; set; }                   // cost in account currency (from spend page)
    public decimal SpendCapUsed { get; set; }           // current spend cap usage (e.g. 0.03)
    public decimal SpendCapLimit { get; set; }          // spend cap limit (e.g. 4.00)
    public string Currency { get; set; } = "";          // "£", "$", etc.
    public DateTime FetchedAt { get; set; }             // when this snapshot was taken (UTC)
}
```

#### 1b. SQLite Storage

Add to `IUsageDataService`:

```csharp
Task UpsertGoogleAiRecordsAsync(string projectId, string timeRange, List<GoogleAiUsageRecord> records);
Task<List<GoogleAiUsageRecord>> GetGoogleAiRecordsAsync(string? projectId = null, string? timeRange = null);
Task DeleteGoogleAiRecordsAsync(string projectId);
```

Add to `UsageDataService`:

- `InitAsync` — add `await _db.CreateTableAsync<GoogleAiUsageRecord>();`
- `UpsertGoogleAiRecordsAsync` — delete all records matching projectId + timeRange, then bulk insert the new ones
- `GetGoogleAiRecordsAsync` — query with optional projectId and timeRange filters. When projectId is null, return all records (for "total" view). When timeRange is null, return all time ranges.
- `DeleteGoogleAiRecordsAsync` — delete all records for a project (used on disconnect)

#### 1c. WebView Scraping Page

`GoogleAiWebViewPage` follows the same two-mode pattern as `ClaudeProWebViewPage`:
- **Interactive mode:** Modal page for initial connection (user logs in via Google if needed)
- **Silent mode:** Hidden WebView embedded in the dashboard page for background refresh

The scraping flow:

1. Navigate to `https://aistudio.google.com/usage?timeRange=last-1-day&project={projectId}`
2. Wait for SPA render (detect via polling for `document.readyState === 'complete'` + presence of chart tables)
3. Inject JS to click all "Populate data" buttons, wait 500ms
4. Extract table data from T3 (Input Tokens per model) and T4 (Requests per model):
   - Row 0 = header (dates in cells[2+])
   - Rows 1+ = data (model name in cells[0], values in cells[2+])
   - Token values need suffix parsing: "1.836M" → 1836000, "597K" → 597000, "23.18k" → 23180
5. Navigate to `https://aistudio.google.com/spend?project={projectId}` with `timeRange=last-7-days`
6. Extract spend summary from DOM text:
   - Cost: regex `Cost\s+([\£\$\€])([\d.]+)` → currency + amount
   - Spend cap: regex `([\£\$\€])([\d.]+)\s*\/\s*[\£\$\€]([\d.]+)` → used / limit
7. Return structured data as a `GoogleAiScrapedData` result object

**JavaScript extraction (injected via `EvaluateJavaScriptAsync`):**

```javascript
// Store in window._googleAiResult to use the two-step pattern (same as ClaudeProWebViewPage)
(async () => {
    try {
        // Click all "Populate data" buttons
        const buttons = [...document.querySelectorAll('button')]
            .filter(b => b.textContent.includes('Populate data'));
        buttons.forEach(b => b.click());
        
        // Wait for tables to populate
        await new Promise(r => setTimeout(r, 500));
        
        const tables = document.querySelectorAll('table');
        const result = { models: [] };
        
        // T3 = Input Tokens, T4 = Requests
        const extractTable = (table) => {
            const rows = [...table.querySelectorAll('tr')];
            if (rows.length < 2) return [];
            const headers = [...rows[0].querySelectorAll('td, th')];
            const dates = headers.slice(2).map(c => c.textContent.trim());
            return rows.slice(1).map(row => {
                const cells = [...row.querySelectorAll('td, th')];
                const label = cells[0]?.textContent.trim().split(' Play')[0];
                const values = cells.slice(2).map(c => c.textContent.trim());
                return { label, dates, values };
            });
        };
        
        if (tables.length > 4) {
            result.inputTokens = extractTable(tables[3]);
            result.requests = extractTable(tables[4]);
        }
        
        window._googleAiResult = JSON.stringify({ ok: true, data: result });
    } catch (ex) {
        window._googleAiResult = JSON.stringify({ error: ex.message });
    }
})();
'started';
```

For the spend page, a separate simpler JS:

```javascript
(async () => {
    try {
        const text = document.body.innerText;
        const costMatch = text.match(/Cost\s+([\£\$\€])([\d.]+)/);
        const capMatch = text.match(/([\£\$\€])([\d.]+)\s*\/\s*[\£\$\€]([\d.]+)/);
        window._googleAiSpendResult = JSON.stringify({
            ok: true,
            data: {
                currency: costMatch?.[1] || '',
                cost: costMatch?.[2] || '0',
                capUsed: capMatch?.[2] || '0',
                capLimit: capMatch?.[3] || '0'
            }
        });
    } catch (ex) {
        window._googleAiSpendResult = JSON.stringify({ error: ex.message });
    }
})();
'started';
```

The page needs to handle the two-step navigation (usage page first, then spend page) within a single `FetchAsync` call. Use `TaskCompletionSource` for the Navigated event, same as `ClaudeProWebViewPage`.

**Token suffix parsing utility** (in the page or a static helper):

```csharp
static long ParseTokenValue(string text)
{
    text = text.Trim();
    if (string.IsNullOrEmpty(text) || text == "0") return 0;
    
    var multiplier = 1.0;
    if (text.EndsWith("M", StringComparison.OrdinalIgnoreCase))
    {
        multiplier = 1_000_000;
        text = text[..^1];
    }
    else if (text.EndsWith("K", StringComparison.OrdinalIgnoreCase))
    {
        multiplier = 1_000;
        text = text[..^1];
    }
    
    return double.TryParse(text, out var val) ? (long)(val * multiplier) : 0;
}
```

**Multi-project support:** The WebView page accepts a list of project IDs. For each project, it navigates to the usage page (with `?project={id}`), scrapes, then navigates to the spend page. Results are returned as `List<GoogleAiUsageRecord>` covering all projects.

**Verify:** Create the `GoogleAiWebViewPage`, navigate to `aistudio.google.com/usage` in interactive mode, confirm the JS extraction returns valid model data from the tables. Build succeeds with no errors.

---

### Task 2: Dashboard Card, Mini Mode, Setup UI, and DI Wiring

**Files:** `GoogleAiCardViewModel.cs`, `ProvidersDashboardViewModel.cs`, `ProvidersDashboardPage.xaml`, `ProvidersDashboardPage.xaml.cs`, `MiniModePage.xaml`, `MiniModeViewModel.cs`, `SetupPage.xaml`, `SetupViewModel.cs`, `MauiProgram.cs`
**Depends on:** Task 1

#### 2a. GoogleAiCardViewModel

A separate ViewModel from `ProviderCardViewModel` — it doesn't use interval/weekly progress bars.

```csharp
// src/ClaudeUsageTracker.Maui/ViewModels/GoogleAiCardViewModel.cs
public partial class GoogleAiCardViewModel : ObservableObject
{
    // Project dropdown
    [ObservableProperty] private ObservableCollection<string> _projects = new();
    [ObservableProperty] private string _selectedProject = "All Projects";  // "All Projects" or specific project ID
    
    // Computed display values (aggregated across models or per selected project)
    [ObservableProperty] private string _cost24h = "—";           // e.g. "£0.12"
    [ObservableProperty] private string _tokens24h = "—";         // e.g. "2.4M"
    [ObservableProperty] private string _requests24h = "—";       // e.g. "1,234"
    [ObservableProperty] private string _spendCapDisplay = "—";   // e.g. "£0.03 / £4.00"
    [ObservableProperty] private string _lastUpdated = "";
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _showInMiniMode = true;
    
    // Per-model breakdown for the dashboard card (not shown in mini mode)
    [ObservableProperty] private ObservableCollection<GoogleAiModelRow> _modelRows = new();
    
    // These are what mini mode binds to (24h only)
    public string MiniCost => Cost24h;
    public string MiniTokens => Tokens24h;
}

public class GoogleAiModelRow
{
    public string ModelName { get; set; } = "";
    public string Requests { get; set; } = "";     // formatted, e.g. "591"
    public string InputTokens { get; set; } = "";  // formatted, e.g. "1.84M"
}
```

When `SelectedProject` changes, re-filter the stored records from SQLite and recompute all display values. "All Projects" sums across all project IDs.

#### 2b. Dashboard Integration

Add to `ProvidersDashboardViewModel`:

```csharp
// New property for Google AI card (singleton, not in the Providers collection)
public GoogleAiCardViewModel GoogleAiCard { get; } = new();

// 30-minute auto-refresh timer (separate from existing plan-provider timer)
private System.Timers.Timer? _googleAiRefreshTimer;
private const int GoogleAiRefreshMinutes = 30;
```

In `RefreshAllAsync`, also trigger Google AI refresh. The Google AI refresh:
1. Gets the list of configured project IDs from SecureStorage
2. For each project, calls `GoogleAiWebViewPage` (silent mode) to scrape usage + spend
3. Upserts records into SQLite
4. Updates `GoogleAiCardViewModel` display values

Start the 30-minute timer when the dashboard loads (if Google AI is connected). The timer runs independently of the existing plan-provider auto-refresh.

#### 2c. Dashboard XAML

Add a new card section in `ProvidersDashboardPage.xaml` **above** the existing provider cards CollectionView. This is a static card (not in the `Providers` collection) that shows when `GoogleAiCard.IsConnected` is true.

```xml
<!-- Google AI Studio Card -->
<Frame CornerRadius="10" Padding="20"
       IsVisible="{Binding GoogleAiCard.IsConnected}"
       BackgroundColor="{AppThemeBinding Light={StaticResource Gray100}, Dark={StaticResource Gray900}}">
    <VerticalStackLayout Spacing="12">
        <!-- Header: title + project dropdown + refresh -->
        <Grid ColumnDefinitions="Auto,*,Auto">
            <Label Grid.Column="0" Text="Google AI Studio" FontSize="18" FontAttributes="Bold" VerticalOptions="Center" />
            <Picker Grid.Column="1" 
                    ItemsSource="{Binding GoogleAiCard.Projects}"
                    SelectedItem="{Binding GoogleAiCard.SelectedProject}"
                    HorizontalOptions="End" VerticalOptions="Center" />
            <Grid Grid.Column="2" WidthRequest="36" HeightRequest="36">
                <!-- refresh button + spinner, same pattern as existing cards -->
            </Grid>
        </Grid>
        
        <!-- Summary row: Cost | Tokens | Requests (24h) -->
        <Grid ColumnDefinitions="*,*,*" ColumnSpacing="12">
            <VerticalStackLayout>
                <Label Text="Cost (24h)" FontSize="11" TextColor="..." />
                <Label Text="{Binding GoogleAiCard.Cost24h}" FontSize="16" FontAttributes="Bold" />
            </VerticalStackLayout>
            <VerticalStackLayout>
                <Label Text="Tokens (24h)" FontSize="11" TextColor="..." />
                <Label Text="{Binding GoogleAiCard.Tokens24h}" FontSize="16" FontAttributes="Bold" />
            </VerticalStackLayout>
            <VerticalStackLayout>
                <Label Text="Requests (24h)" FontSize="11" TextColor="..." />
                <Label Text="{Binding GoogleAiCard.Requests24h}" FontSize="16" FontAttributes="Bold" />
            </VerticalStackLayout>
        </Grid>
        
        <!-- Spend cap bar -->
        <Label Text="{Binding GoogleAiCard.SpendCapDisplay}" FontSize="12" />
        
        <!-- Per-model breakdown table -->
        <VerticalStackLayout BindableLayout.ItemsSource="{Binding GoogleAiCard.ModelRows}">
            <BindableLayout.ItemTemplate>
                <DataTemplate x:DataType="vm:GoogleAiModelRow">
                    <Grid ColumnDefinitions="*,Auto,Auto" ColumnSpacing="12" Padding="0,2">
                        <Label Text="{Binding ModelName}" FontSize="12" />
                        <Label Text="{Binding Requests}" FontSize="12" HorizontalTextAlignment="End" />
                        <Label Text="{Binding InputTokens}" FontSize="12" HorizontalTextAlignment="End" />
                    </Grid>
                </DataTemplate>
            </BindableLayout.ItemTemplate>
        </VerticalStackLayout>
        
        <!-- Error + last updated -->
        <Label Text="{Binding GoogleAiCard.ErrorMessage}" TextColor="Red" FontSize="11"
               IsVisible="{Binding GoogleAiCard.HasError}" />
        <Label Text="{Binding GoogleAiCard.LastUpdated, StringFormat='Updated {0}'}" FontSize="10" />
    </VerticalStackLayout>
</Frame>
```

Add a hidden WebView for silent Google AI refresh (same pattern as the existing `ClaudeSilentWebView`):

```xml
<WebView x:Name="GoogleAiSilentWebView" Grid.Row="0" Grid.RowSpan="4"
         IsVisible="False" />
```

#### 2d. Mini Mode

Add a Google AI section to `MiniModePage.xaml` that shows when the Google AI card is visible in mini mode. Keep it compact — just two lines:

```xml
<!-- Google AI Studio mini row -->
<VerticalStackLayout IsVisible="{Binding GoogleAiCard.ShowInMiniMode}" Spacing="4" Padding="0,8">
    <Grid ColumnDefinitions="*,Auto">
        <Label Text="Google AI" FontSize="15" FontAttributes="Bold" VerticalOptions="Center" />
        <!-- refresh button -->
    </Grid>
    <Grid ColumnDefinitions="*,*" ColumnSpacing="12">
        <Label FontSize="13">
            <Label.FormattedText>
                <FormattedString>
                    <Span Text="Cost 24h: " />
                    <Span Text="{Binding GoogleAiCard.MiniCost}" FontAttributes="Bold" />
                </FormattedString>
            </Label.FormattedText>
        </Label>
        <Label FontSize="13">
            <Label.FormattedText>
                <FormattedString>
                    <Span Text="Tokens 24h: " />
                    <Span Text="{Binding GoogleAiCard.MiniTokens}" FontAttributes="Bold" />
                </FormattedString>
            </Label.FormattedText>
        </Label>
    </Grid>
    <!-- Project dropdown in mini mode too -->
    <Picker ItemsSource="{Binding GoogleAiCard.Projects}"
            SelectedItem="{Binding GoogleAiCard.SelectedProject}"
            FontSize="12" />
    <BoxView HeightRequest="1" Margin="0,2,0,0" Color="..." />
</VerticalStackLayout>
```

Update `MiniModeViewModel` to expose the `GoogleAiCard` from the dashboard VM and handle `ShowInMiniMode` persistence for it.

#### 2e. Setup Page

Add a "Google AI Studio" card to `SetupPage.xaml` between MiniMaxi and the "Go to Dashboard" button:

```xml
<!-- Google AI Studio Card -->
<Frame CornerRadius="10" Padding="20" BackgroundColor="...">
    <VerticalStackLayout Spacing="12">
        <Label Text="Google AI Studio" FontSize="16" FontAttributes="Bold" />
        <Label Text="Tracks API usage, token counts, and cost for your Google AI Studio projects."
               FontSize="12" TextColor="..." />
        
        <!-- Not connected state: Connect button + project ID entry -->
        <VerticalStackLayout IsVisible="{Binding IsGoogleAiConnected, Converter={StaticResource InvertedBoolConverter}}"
                             Spacing="8">
            <Entry Placeholder="Project ID (e.g. my-project-123)"
                   Text="{Binding GoogleAiProjectId}" />
            <Button Text="Connect Google AI Studio"
                    Clicked="OnConnectGoogleAiClicked" />
        </VerticalStackLayout>
        
        <!-- Connected state: project list + add/remove + disconnect -->
        <VerticalStackLayout IsVisible="{Binding IsGoogleAiConnected}" Spacing="8">
            <Label Text="✓ Connected" TextColor="{StaticResource Primary}" />
            <!-- List of connected projects -->
            <VerticalStackLayout BindableLayout.ItemsSource="{Binding GoogleAiProjects}">
                <BindableLayout.ItemTemplate>
                    <DataTemplate>
                        <Grid ColumnDefinitions="*,Auto">
                            <Label Text="{Binding .}" FontSize="13" VerticalOptions="Center" />
                            <Button Text="✕" FontSize="11" BackgroundColor="Transparent"
                                    CommandParameter="{Binding .}"
                                    Command="{Binding Source={RelativeSource AncestorType={x:Type vm:SetupViewModel}}, Path=RemoveGoogleAiProjectCommand}" />
                        </Grid>
                    </DataTemplate>
                </BindableLayout.ItemTemplate>
            </VerticalStackLayout>
            <!-- Add another project -->
            <Grid ColumnDefinitions="*,Auto" ColumnSpacing="8">
                <Entry Placeholder="Add another project ID..." Text="{Binding GoogleAiProjectId}" />
                <Button Text="Add" Command="{Binding AddGoogleAiProjectCommand}" />
            </Grid>
            <Button Text="Disconnect All" Command="{Binding DisconnectGoogleAiCommand}"
                    BackgroundColor="Transparent" TextColor="..." FontSize="12" />
        </VerticalStackLayout>
    </VerticalStackLayout>
</Frame>
```

Add to `SetupViewModel`:
- `IsGoogleAiConnected` (bool)
- `GoogleAiProjectId` (string, entry binding)
- `GoogleAiProjects` (ObservableCollection<string>, persisted in SecureStorage as comma-separated)
- `AddGoogleAiProjectCommand` — adds project to list, saves to storage
- `RemoveGoogleAiProjectCommand` — removes project from list
- `DisconnectGoogleAiCommand` — clears all projects, deletes records from SQLite

The "Connect Google AI Studio" button (`OnConnectGoogleAiClicked` in code-behind) opens `GoogleAiWebViewPage` in interactive mode (modal), similar to `OnConnectClaudeProClicked`. If the scrape succeeds, the project is saved and the user returns to setup.

#### 2f. DI Wiring

In `MauiProgram.cs`:
- No new `IUsageProvider` registration for Google AI (it doesn't implement that interface — different data model)
- Pass `GoogleAiCardViewModel` into `ProvidersDashboardViewModel` constructor (or let the VM create it internally)
- Register `GoogleAiWebViewPage` as transient

Update `ProvidersDashboardViewModel` constructor to accept `GoogleAiCardViewModel`.

Update `GetApiKeyForProvider` — not needed for Google AI since it uses cookie-based auth, not API keys. The project list is retrieved from SecureStorage separately.

#### 2g. Auto-Refresh (30 minutes, hardcoded)

In `ProvidersDashboardViewModel`, when Google AI is connected:

```csharp
private void StartGoogleAiAutoRefresh()
{
    _googleAiRefreshTimer?.Dispose();
    _googleAiRefreshTimer = new System.Timers.Timer(TimeSpan.FromMinutes(30).TotalMilliseconds);
    _googleAiRefreshTimer.Elapsed += async (_, _) => await RefreshGoogleAiAsync();
    _googleAiRefreshTimer.AutoReset = true;
    _googleAiRefreshTimer.Start();
}
```

Manual refresh via the card's refresh button calls `RefreshGoogleAiAsync()` directly. "Refresh All" on the dashboard triggers both plan-provider refresh and Google AI refresh.

**Verify:** Build and run the app. Setup page shows Google AI Studio card. Enter a project ID, connect (Google login flow in WebView). Dashboard shows Google AI card with cost, tokens, requests, and per-model breakdown. Project dropdown switches between individual project and "All Projects" totals. Mini mode shows 24h cost and 24h tokens. Auto-refresh fires every 30 minutes. Manual refresh button works.
