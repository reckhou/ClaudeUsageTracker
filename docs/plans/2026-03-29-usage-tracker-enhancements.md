# Usage Tracker Enhancements Implementation Plan

**Goal:** Implement 5 features: (1) Google AI usage support, (2) provider dropdown on usage tracker page, (3) remove model breakdown, (4) token usage graph with time-range switcher, (5) fix daily cost date display.

**Architecture:** Provider-agnostic architecture extended with Google AI. Dashboard gets a provider-selector Picker controlling which data source feeds stats + chart. Token graph reuses existing `GraphicsView` with a new `TokenBarChartDrawable`. Google AI usage stored in `ProviderUsageRecord` (same as MiniMaxi). Existing `UsageRecord` (Anthropic per-model) remains for Admin API data. No "All" option — dropdown selects one provider at a time.

---

## Progress

- [x] Task 1: Google AI usage provider
- [x] Task 2: Provider dropdown + token graph on Dashboard
- [x] Task 3: Date display fix for daily cost chart

---

## Files

- Create: `src/ClaudeUsageTracker.Maui/Services/GoogleAIUsageProvider.cs` — Google AI API implementation of `IUsageProvider`
- Create: `src/ClaudeUsageTracker.Maui/Views/TokenBarChartDrawable.cs` — token bar chart drawable
- Modify: `src/ClaudeUsageTracker.Maui/MauiProgram.cs` — register `GoogleAIUsageProvider`
- Modify: `src/ClaudeUsageTracker.Maui/Views/DashboardPage.xaml` — add provider Picker, replace model breakdown with token chart, fix date labels
- Modify: `src/ClaudeUsageTracker.Maui/Views/DashboardPage.xaml.cs` — wire token chart to `TokenBarChartDrawable`
- Modify: `src/ClaudeUsageTracker.Core/ViewModels/DashboardViewModel.cs` — add selected provider logic, token data per time range, time range enum+dropdown
- Modify: `src/ClaudeUsageTracker.Maui/Views/CostBarChartDrawable.cs` — add date labels

---

### Task 1: Google AI Usage Provider

**Files:** `GoogleAIUsageProvider.cs`, `MauiProgram.cs`
**Depends on:** None

Implement `IUsageProvider` for Google AI (Gemini). Google AI uses an API key as the `key` query parameter or `x-goog-api-key` header. The implementation confirms connectivity and returns a "connected" status record.

**`GoogleAIUsageProvider.cs`** (new file in `Maui/Services`):
```csharp
using System.Net.Http;
using System.Text.Json;
using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Core.Services;

namespace ClaudeUsageTracker.Maui.Services;

public class GoogleAIUsageProvider : IUsageProvider
{
    public string ProviderName => "GoogleAI";

    private static readonly HttpClient _http = new();

    public async Task<ProviderUsageRecord?> FetchAsync(string apiKey, CancellationToken ct = default)
    {
        // Google AI models list endpoint — confirms API key validity
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}");
        req.Headers.Add("x-goog-api-key", apiKey);

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var root = doc.RootElement;
        if (!root.TryGetProperty("models", out var models)) return null;
        if (models.GetArrayLength() == 0) return null;

        // Google AI quota is daily. Map interval=daily reset for display purposes.
        return new ProviderUsageRecord
        {
            Provider = ProviderName,
            IntervalUtilization = 0,
            IntervalUsed = 0,
            IntervalTotal = 0,
            IntervalResetsAt = DateTime.UtcNow.Date.AddDays(1),
            WeeklyUtilization = 0,
            WeeklyUsed = 0,
            WeeklyTotal = 0,
            WeeklyResetsAt = DateTime.UtcNow.Date.AddDays(7 - (int)DateTime.UtcNow.DayOfWeek),
            FetchedAt = DateTime.UtcNow
        };
    }
}
```

**`MauiProgram.cs` modifications:**
Add `GoogleAIUsageProvider` registration alongside existing providers:
```csharp
builder.Services.AddSingleton<IUsageProvider, MiniMaxiUsageProvider>();
builder.Services.AddSingleton<IUsageProvider, GoogleAIUsageProvider>(); // ADD
builder.Services.AddSingleton<IUsageProvider, ClaudeProUsageProvider>();
```

Also add secure storage key in `SetupPage.xaml.cs` — add a Google AI API key field (similar to MiniMaxi).

**Verify:** Run app, navigate to Providers dashboard, tap Refresh All — Google AI card appears with "Connected" status.

---

### Task 2: Provider Dropdown + Token Graph on Dashboard

**Files:** `DashboardPage.xaml`, `DashboardPage.xaml.cs`, `DashboardViewModel.cs`, `TokenBarChartDrawable.cs`
**Depends on:** Task 1

Replace the model breakdown section with a provider selector + token usage bar chart. Provider dropdown shows: Anthropic, MiniMaxi, GoogleAI. Time range buttons: 24h (hourly), 7d (daily), 30d (daily).

**`TokenBarChartDrawable.cs`** (new file in `Maui/Views`):
```csharp
using ClaudeUsageTracker.Core.ViewModels;

namespace ClaudeUsageTracker.Maui.Views;

public class TokenBarChartDrawable(IReadOnlyList<TokenUsage> data, string timeRangeLabel) : IDrawable
{
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (data.Count == 0) return;

        float w = dirtyRect.Width;
        float h = dirtyRect.Height;
        float barMargin = 2f;
        float barWidth = (w / data.Count) - barMargin;
        float maxTokens = (float)(data.Max(d => d.Tokens) == 0 ? 1 : (double)data.Max(d => d.Tokens));

        canvas.FillColor = Color.FromArgb("#34C759"); // Green

        for (int i = 0; i < data.Count; i++)
        {
            float barH = h * (float)(data[i].Tokens / (decimal)maxTokens);
            float x = i * (barWidth + barMargin);
            float y = h - barH;
            canvas.FillRectangle(x, y, barWidth, barH);

            // Date labels — hourly for 24h, daily for 7d/30d
            if (showDates && data.Count <= 24)
            {
                canvas.FontColor = Colors.Gray;
                canvas.FontSize = 9;
                var label = data[i].Date.ToLocalTime().ToString("htt"); // "6AM"
                canvas.DrawString(label, x + barWidth / 2, h + 12, HorizontalAlignment.Center);
            }
            else if (showDates && data.Count <= 7)
            {
                canvas.FontColor = Colors.Gray;
                canvas.FontSize = 9;
                var label = data[i].Date.ToLocalTime().ToString("ddd"); // "Mon"
                canvas.DrawString(label, x + barWidth / 2, h + 12, HorizontalAlignment.Center);
            }
            else if (showDates && i % 5 == 0)
            {
                canvas.FontColor = Colors.Gray;
                canvas.FontSize = 9;
                var label = data[i].Date.ToLocalTime().ToString("M/d"); // "3/15"
                canvas.DrawString(label, x + barWidth / 2, h + 12, HorizontalAlignment.Center);
            }
        }

        canvas.StrokeColor = Colors.Gray;
        canvas.StrokeSize = 1;
        canvas.DrawLine(0, h, w, h);
    }
}
```

**`DashboardViewModel.cs` additions:**
```csharp
public enum ProviderFilter { Anthropic, MiniMaxi, GoogleAI }
public enum TokenTimeRange { Past24Hours, Past7Days, Past30Days }

[ObservableProperty] private ProviderFilter _selectedProvider = ProviderFilter.Anthropic;
[ObservableProperty] private TokenTimeRange _selectedTimeRange = TokenTimeRange.Past24Hours;
[ObservableProperty] private string _timeRangeLabel = "Past 24 Hours";

public ObservableCollection<TokenUsage> TokenChartData { get; } = new();

public record TokenUsage(DateTime Date, long Tokens);

[RelayCommand]
public void SetTimeRange(string range)
{
    if (Enum.TryParse<TokenTimeRange>(range, out var tr))
    {
        SelectedTimeRange = tr;
        TimeRangeLabel = tr switch
        {
            TokenTimeRange.Past24Hours => "Past 24 Hours (Hourly)",
            TokenTimeRange.Past7Days => "Past 7 Days (Daily)",
            TokenTimeRange.Past30Days => "Past 30 Days (Daily)",
            _ => ""
        };
        _ = LoadTokenChartDataAsync();
    }
}
```

**`LoadTokenChartDataAsync()` logic:**
- **Anthropic** (SelectedProvider == Anthropic): Query `UsageRecord` from DB, group by hour (24h) or day (7d/30d), sum `InputTokens + OutputTokens` per bucket
- **MiniMaxi/GoogleAI**: These aggregate-only providers — show a single bar representing current `IntervalUsed`. If no record exists, show empty chart.

```csharp
private async Task LoadTokenChartDataAsync()
{
    TokenChartData.Clear();
    var today = DateTime.UtcNow.Date;

    if (SelectedProvider == ProviderFilter.Anthropic)
    {
        var from = SelectedTimeRange switch
        {
            TokenTimeRange.Past24Hours => today.AddHours(-24),
            TokenTimeRange.Past7Days => today.AddDays(-7),
            TokenTimeRange.Past30Days => today.AddDays(-30),
            _ => today.AddHours(-24)
        };
        var records = await db.GetUsageAsync(from, DateTime.UtcNow);

        var grouped = SelectedTimeRange == TokenTimeRange.Past24Hours
            ? records.GroupBy(r => r.BucketStart.Date.AddHours(r.BucketStart.Hour))
            : records.GroupBy(r => r.BucketStart.Date);

        foreach (var g in grouped.OrderBy(g => g.Key))
            TokenChartData.Add(new TokenUsage(g.Key, g.Sum(r => r.InputTokens + r.OutputTokens)));
    }
    else
    {
        // MiniMaxi / GoogleAI — aggregate only
        var allRecords = await db.GetAllProviderRecordsAsync();
        var record = allRecords.FirstOrDefault(r => r.Provider == SelectedProvider.ToString());
        if (record != null && record.IntervalUsed > 0)
            TokenChartData.Add(new TokenUsage(DateTime.UtcNow, record.IntervalUsed));
    }
}
```

**`DashboardPage.xaml` modifications:**

Replace Model Breakdown Grid (Grid.Row="3") with:
```xml
<!-- Provider Selector + Token Chart -->
<Grid Grid.Row="2" RowDefinitions="Auto,Auto,*">
    <Grid Grid.Row="0" ColumnDefinitions="*,Auto" Margin="0,0,0,8">
        <Label Text="Token Usage" FontAttributes="Bold" VerticalOptions="Center" />
        <Picker Grid.Column="1"
                x:Name="ProviderPicker"
                Title="Provider"
                SelectedIndexChanged="OnProviderChanged"
                WidthRequest="140">
            <Picker.ItemsSource>
                <x:Array Type="{x:Type x:String}">
                    <x:String>Anthropic</x:String>
                    <x:String>MiniMaxi</x:String>
                    <x:String>GoogleAI</x:String>
                </x:Array>
            </Picker.ItemsSource>
        </Picker>
    </Grid>

    <StackLayout Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,8">
        <Button Text="24h" Command="{Binding SetTimeRangeCommand}" CommandParameter="Past24Hours" Padding="8,4" />
        <Button Text="7d" Command="{Binding SetTimeRangeCommand}" CommandParameter="Past7Days" Padding="8,4" Margin="4,0" />
        <Button Text="30d" Command="{Binding SetTimeRangeCommand}" CommandParameter="Past30Days" Padding="8,4" />
    </StackLayout>

    <GraphicsView Grid.Row="2" x:Name="TokenChart" HeightRequest="200" />
</Grid>
```

Remove the Model Breakdown Grid entirely.

**`DashboardPage.xaml.cs` modifications:**
```csharp
protected override void OnAppearing()
{
    base.OnAppearing();
    _vm.RefreshCommand.Execute(null);
    ProviderPicker.SelectedIndex = 0; // Default to Anthropic
    SetupTokenChart();
}

private void SetupTokenChart()
{
    _vm.TokenChartData.CollectionChanged += (_, _) =>
    {
        TokenChart.Drawable = new TokenBarChartDrawable(
            _vm.TokenChartData.ToList(),
            _vm.TimeRangeLabel);
        TokenChart.Invalidate();
    };
}

private void OnProviderChanged(object sender, EventArgs e)
{
    if (ProviderPicker.SelectedIndex < 0) return;
    _vm.SelectedProvider = (ProviderFilter)ProviderPicker.SelectedIndex;
    _ = _vm.LoadTokenChartDataAsync();
}
```

**Verify:** Run app, open Usage Tracker page. Model breakdown is gone. Token Usage section appears with Anthropic selected by default. Switching provider to MiniMaxi shows aggregate usage. Switching time range to "7d" regroups data daily.

---

### Task 3: Date Display Fix for Daily Cost Chart

**Files:** `CostBarChartDrawable.cs`, `DashboardPage.xaml.cs`
**Depends on:** None

Add date labels below bars in `CostBarChartDrawable`. Show day-of-week for 7-bar weekly view, every-5-days for 30-day view.

**`CostBarChartDrawable.cs` modifications:**

```csharp
public class CostBarChartDrawable(IReadOnlyList<DailyUsage> data) : IDrawable
{
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (data.Count == 0) return;

        float w = dirtyRect.Width;
        float h = dirtyRect.Height;
        float barMargin = 2f;
        float barWidth = (w / data.Count) - barMargin;
        float maxCost = (float)(data.Max(d => d.CostUsd) == 0 ? 1 : (double)data.Max(d => d.CostUsd));

        canvas.FillColor = Color.FromArgb("#512BD4");

        for (int i = 0; i < data.Count; i++)
        {
            float barH = h * (float)(data[i].CostUsd / (decimal)maxCost);
            float x = i * (barWidth + barMargin);
            float y = h - barH;
            canvas.FillRectangle(x, y, barWidth, barH);

            // Date labels
            if (data.Count <= 7)
            {
                // Weekly — show day name
                canvas.FontColor = Colors.Gray;
                canvas.FontSize = 9;
                canvas.DrawString(data[i].Date.ToLocalTime().ToString("ddd"),
                    x + barWidth / 2, h + 14, HorizontalAlignment.Center);
            }
            else if (i % 5 == 0)
            {
                // 30-day — show month/day every 5 bars
                canvas.FontColor = Colors.Gray;
                canvas.FontSize = 9;
                canvas.DrawString(data[i].Date.ToLocalTime().ToString("M/d"),
                    x + barWidth / 2, h + 14, HorizontalAlignment.Center);
            }
        }

        canvas.StrokeColor = Colors.Gray;
        canvas.StrokeSize = 1;
        canvas.DrawLine(0, h, w, h);
    }
}
```

**`DashboardPage.xaml.cs`** — the call site needs no change since the constructor signature is unchanged (only the internal Draw logic changed).

**Verify:** Run app, open Usage Tracker page. The bar chart shows day labels (Mon, Tue...) for the current week's bars, and M/d labels (3/15...) for older bars in the 30-day view. Labels are readable and don't overlap.

---

## Notes

- **Google AI API**: Current implementation is a connectivity check. Real quota/fetch API should be added once the actual Google AI billing API endpoint is confirmed.
- **MiniMaxi/GoogleAI token chart**: These show aggregate current usage only (single bar at "now") since they don't expose per-model/per-day breakdowns.
