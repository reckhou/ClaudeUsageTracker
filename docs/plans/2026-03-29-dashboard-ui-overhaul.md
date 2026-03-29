# Dashboard UI Overhaul Implementation Plan

**Goal:** Redesign DashboardPage and ProvidersDashboardPage with dynamic title, animated refresh icons, provider-scoped charts, chart axis redesign, button visual states, and proper unavailability messaging.

**Architecture:** All visual changes stay within the MAUI layer. DashboardViewModel gains a computed `PageTitle` property and bool helper properties for time-range selected state. The two chart drawables are rewritten to support Y-axis gridlines, value labels, and an optional "unavailable" message string. ProvidersDashboardViewModel gains an `IsAnyRefreshing` computed property driven by per-card `IsRefreshing` changes. Core logic (data loading, provider fetch) is unchanged.

**Tech Stack:** C# + .NET MAUI, CommunityToolkit.Mvvm, IDrawable (MAUI Graphics)

---

## Progress

- [x] Task 1: Button visual states and named styles
- [x] Task 2: DashboardPage header, title, and provider-scoped charts
- [x] Task 3: Chart drawable redesign (Daily Cost + Token Usage)
- [x] Task 4: ProvidersDashboard animated refresh

---

## Files

- Modify: `src/ClaudeUsageTracker.Maui/Resources/Styles/Styles.xaml` — add PointerOver/Pressed visual states to Button; add `TimeRangeButton` and `TimeRangeButtonSelected` named styles
- Modify: `src/ClaudeUsageTracker.Core/ViewModels/DashboardViewModel.cs` — add `PageTitle`, `IsTimeRange24h/7d/30d`, `CostUnavailableMessage`, `TokenUnavailableMessage`; wire provider change to cost chart
- Modify: `src/ClaudeUsageTracker.Maui/Views/DashboardPage.xaml` — bind title, remove Settings button, animated refresh, DataTriggers for time-range selection, pass unavailable messages to charts
- Modify: `src/ClaudeUsageTracker.Maui/Views/DashboardPage.xaml.cs` — pass unavailable messages when rebuilding chart drawables
- Modify: `src/ClaudeUsageTracker.Maui/Views/CostBarChartDrawable.cs` — full redesign with Y-axis, value labels, unavailable message
- Modify: `src/ClaudeUsageTracker.Maui/Views/TokenBarChartDrawable.cs` — full redesign consistent with cost chart
- Modify: `src/ClaudeUsageTracker.Maui/ViewModels/ProvidersDashboardViewModel.cs` — add `IsAnyRefreshing`; notify when any card changes
- Modify: `src/ClaudeUsageTracker.Maui/Views/ProvidersDashboardPage.xaml` — replace "Refresh All" button with button/ActivityIndicator pair

---

### Task 1: Button visual states and named styles

**Files:** `src/ClaudeUsageTracker.Maui/Resources/Styles/Styles.xaml`

Add `PointerOver` and `Pressed` visual states to the global Button style, and add two named styles for the time-range toggle buttons.

**Global Button style** — extend the existing `VisualStateGroup` inside the `<Style TargetType="Button">`:

```xml
<VisualState x:Name="PointerOver">
    <VisualState.Setters>
        <Setter Property="BackgroundColor"
                Value="{AppThemeBinding Light={StaticResource Tertiary}, Dark={StaticResource SecondaryDarkText}}" />
        <Setter Property="Opacity" Value="0.92" />
    </VisualState.Setters>
</VisualState>
<VisualState x:Name="Pressed">
    <VisualState.Setters>
        <Setter Property="BackgroundColor"
                Value="{AppThemeBinding Light={StaticResource Tertiary}, Dark={StaticResource SecondaryDarkText}}" />
        <Setter Property="Opacity" Value="0.75" />
        <Setter Property="Scale" Value="0.97" />
    </VisualState.Setters>
</VisualState>
```

**Named styles** — add after the global Button style block:

```xml
<!-- Time-range toggle button: unselected -->
<Style x:Key="TimeRangeButton" TargetType="Button">
    <Setter Property="FontFamily" Value="OpenSansRegular" />
    <Setter Property="FontSize" Value="13" />
    <Setter Property="Padding" Value="8,4" />
    <Setter Property="CornerRadius" Value="6" />
    <Setter Property="BorderWidth" Value="0" />
    <Setter Property="BackgroundColor"
            Value="{AppThemeBinding Light={StaticResource Gray200}, Dark={StaticResource Gray600}}" />
    <Setter Property="TextColor"
            Value="{AppThemeBinding Light={StaticResource Gray600}, Dark={StaticResource Gray300}}" />
    <Setter Property="VisualStateManager.VisualStateGroups">
        <VisualStateGroupList>
            <VisualStateGroup x:Name="CommonStates">
                <VisualState x:Name="Normal" />
                <VisualState x:Name="Disabled">
                    <VisualState.Setters>
                        <Setter Property="TextColor"
                                Value="{AppThemeBinding Light={StaticResource Gray300}, Dark={StaticResource Gray600}}" />
                        <Setter Property="BackgroundColor"
                                Value="{AppThemeBinding Light={StaticResource Gray100}, Dark={StaticResource Gray900}}" />
                    </VisualState.Setters>
                </VisualState>
                <VisualState x:Name="PointerOver">
                    <VisualState.Setters>
                        <Setter Property="BackgroundColor"
                                Value="{AppThemeBinding Light={StaticResource Gray300}, Dark={StaticResource Gray500}}" />
                    </VisualState.Setters>
                </VisualState>
                <VisualState x:Name="Pressed">
                    <VisualState.Setters>
                        <Setter Property="Opacity" Value="0.75" />
                        <Setter Property="Scale" Value="0.97" />
                    </VisualState.Setters>
                </VisualState>
            </VisualStateGroup>
        </VisualStateGroupList>
    </Setter>
</Style>

<!-- Time-range toggle button: selected -->
<Style x:Key="TimeRangeButtonSelected" TargetType="Button" BasedOn="{StaticResource TimeRangeButton}">
    <Setter Property="BackgroundColor"
            Value="{AppThemeBinding Light={StaticResource Primary}, Dark={StaticResource PrimaryDark}}" />
    <Setter Property="TextColor"
            Value="{AppThemeBinding Light={StaticResource White}, Dark={StaticResource PrimaryDarkText}}" />
</Style>
```

**Verify:** Run the app. Hover over a standard button — background darkens. Click and hold — slight scale-down. On DashboardPage, the time-range buttons will use these named styles after Task 2.

---

### Task 2: DashboardPage header, title, and provider-scoped charts

**Files:** `src/ClaudeUsageTracker.Core/ViewModels/DashboardViewModel.cs`, `src/ClaudeUsageTracker.Maui/Views/DashboardPage.xaml`, `src/ClaudeUsageTracker.Maui/Views/DashboardPage.xaml.cs`

#### DashboardViewModel.cs changes

Add computed properties after the existing `[ObservableProperty]` declarations:

```csharp
// Computed properties — call OnPropertyChanged when SelectedProvider or SelectedTimeRange changes

public string PageTitle => SelectedProvider switch
{
    ProviderFilter.Anthropic => "Anthropic Usage Tracker",
    ProviderFilter.MiniMaxi  => "MiniMaxi Usage Tracker",
    ProviderFilter.GoogleAI  => "Google AI Usage Tracker",
    _                        => "Usage Tracker"
};

public bool IsTimeRange24h  => SelectedTimeRange == TokenTimeRange.Past24Hours;
public bool IsTimeRange7d   => SelectedTimeRange == TokenTimeRange.Past7Days;
public bool IsTimeRange30d  => SelectedTimeRange == TokenTimeRange.Past30Days;

// Null = data available (chart renders), non-null = show this message instead
public string? CostUnavailableMessage { get; private set; }
public string? TokenUnavailableMessage { get; private set; }
```

Fire change notifications — update the two existing partial methods (or add them if missing):

```csharp
partial void OnSelectedProviderChanged(ProviderFilter value)
{
    OnPropertyChanged(nameof(PageTitle));
    _ = LoadFromDbAsync(); // Reloads both cost and token charts
}

partial void OnSelectedTimeRangeChanged(TokenTimeRange value)
{
    OnPropertyChanged(nameof(IsTimeRange24h));
    OnPropertyChanged(nameof(IsTimeRange7d));
    OnPropertyChanged(nameof(IsTimeRange30d));
    _ = LoadTokenChartDataAsync();
}
```

Update `LoadFromDbAsync` to set `CostUnavailableMessage` at the end of the method, before the token chart call:

```csharp
// After populating DailyUsages:
if (SelectedProvider != ProviderFilter.Anthropic)
{
    DailyUsages.Clear(); // Cost history only available for Anthropic
    CostUnavailableMessage = $"Daily cost history is not available for {PageTitle.Replace(" Usage Tracker", "")}";
}
else
{
    CostUnavailableMessage = HasAdminApiData ? null : "No cost data — add an Admin API key in Settings";
}
OnPropertyChanged(nameof(CostUnavailableMessage));
```

Update `LoadTokenChartDataAsync` to set `TokenUnavailableMessage`:

```csharp
public async Task LoadTokenChartDataAsync()
{
    TokenChartData.Clear();

    if (SelectedProvider == ProviderFilter.Anthropic)
    {
        // ... existing Anthropic logic unchanged ...
        TokenUnavailableMessage = TokenChartData.Count == 0
            ? "No token data — add an Admin API key in Settings"
            : null;
    }
    else
    {
        var allRecords = await db.GetAllProviderRecordsAsync();
        var recordName = SelectedProvider.ToString();
        var record = allRecords.FirstOrDefault(r => r.Provider == recordName);
        if (record != null && record.IntervalUsed > 0)
        {
            TokenChartData.Add(new TokenUsage(DateTime.UtcNow, record.IntervalUsed));
            TokenUnavailableMessage = null;
        }
        else
        {
            TokenUnavailableMessage = $"No token snapshot available for {recordName}";
        }
    }
    OnPropertyChanged(nameof(TokenUnavailableMessage));
}
```

Also remove `SetTimeRange` RelayCommand's inline call to `LoadTokenChartDataAsync()` since the partial method now handles it:

```csharp
[RelayCommand]
public void SetTimeRange(string range)
{
    if (!Enum.TryParse<TokenTimeRange>(range, out var tr)) return;
    SelectedTimeRange = tr;
    TimeRangeLabel = tr switch
    {
        TokenTimeRange.Past24Hours => "Past 24 Hours (Hourly)",
        TokenTimeRange.Past7Days   => "Past 7 Days (Daily)",
        TokenTimeRange.Past30Days  => "Past 30 Days (Daily)",
        _                          => ""
    };
    // OnSelectedTimeRangeChanged partial method fires LoadTokenChartDataAsync
}
```

#### DashboardPage.xaml changes

**Header row** — remove Settings button, bind title, replace Refresh with button+ActivityIndicator pair:

```xml
<!-- Header -->
<Grid Grid.Row="0" ColumnDefinitions="*,Auto">
    <Label Text="{Binding PageTitle}" FontSize="22" FontAttributes="Bold" VerticalOptions="Center" />
    <Grid Grid.Column="1" WidthRequest="44" HeightRequest="44">
        <Button Text="↻" FontSize="18"
                Command="{Binding RefreshCommand}"
                IsVisible="{Binding IsRefreshing, Converter={StaticResource InvertedBoolConverter}}"
                BackgroundColor="Transparent"
                TextColor="{AppThemeBinding Light={StaticResource Gray500}, Dark={StaticResource Gray400}}" />
        <ActivityIndicator IsRunning="{Binding IsRefreshing}"
                           IsVisible="{Binding IsRefreshing}"
                           WidthRequest="22" HeightRequest="22"
                           HorizontalOptions="Center" VerticalOptions="Center" />
    </Grid>
</Grid>
```

**Time-range buttons** — use named styles with DataTriggers for selected state:

```xml
<StackLayout Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,8">
    <Button Text="24h"
            Command="{Binding SetTimeRangeCommand}" CommandParameter="Past24Hours"
            Style="{StaticResource TimeRangeButton}">
        <Button.Triggers>
            <DataTrigger TargetType="Button" Binding="{Binding IsTimeRange24h}" Value="True">
                <Setter Property="Style" Value="{StaticResource TimeRangeButtonSelected}" />
            </DataTrigger>
        </Button.Triggers>
    </Button>
    <Button Text="7d" Margin="4,0"
            Command="{Binding SetTimeRangeCommand}" CommandParameter="Past7Days"
            Style="{StaticResource TimeRangeButton}">
        <Button.Triggers>
            <DataTrigger TargetType="Button" Binding="{Binding IsTimeRange7d}" Value="True">
                <Setter Property="Style" Value="{StaticResource TimeRangeButtonSelected}" />
            </DataTrigger>
        </Button.Triggers>
    </Button>
    <Button Text="30d"
            Command="{Binding SetTimeRangeCommand}" CommandParameter="Past30Days"
            Style="{StaticResource TimeRangeButton}">
        <Button.Triggers>
            <DataTrigger TargetType="Button" Binding="{Binding IsTimeRange30d}" Value="True">
                <Setter Property="Style" Value="{StaticResource TimeRangeButtonSelected}" />
            </DataTrigger>
        </Button.Triggers>
    </Button>
</StackLayout>
```

Also bind ContentPage Title: `Title="{Binding PageTitle}"` on the `<ContentPage>` element.

#### DashboardPage.xaml.cs changes

Pass unavailable messages when rebuilding drawables:

```csharp
private void OnDailyUsagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
{
    CostChart.Drawable = new CostBarChartDrawable(_vm.DailyUsages, _vm.CostUnavailableMessage);
    CostChart.Invalidate();
}

private void OnTokenChartDataChanged(object? sender, NotifyCollectionChangedEventArgs e)
{
    TokenChart.Drawable = new TokenBarChartDrawable(
        _vm.TokenChartData.ToList(),
        _vm.TimeRangeLabel,
        _vm.TokenUnavailableMessage);
    TokenChart.Invalidate();
}
```

Also subscribe to `PropertyChanged` so unavailable messages trigger chart redraws even when the collection doesn't change:

```csharp
public DashboardPage(DashboardViewModel vm)
{
    InitializeComponent();
    _vm = vm;
    BindingContext = vm;
    vm.DailyUsages.CollectionChanged += OnDailyUsagesChanged;
    vm.TokenChartData.CollectionChanged += OnTokenChartDataChanged;
    vm.PropertyChanged += OnVmPropertyChanged;
}

private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName is nameof(DashboardViewModel.CostUnavailableMessage))
    {
        CostChart.Drawable = new CostBarChartDrawable(_vm.DailyUsages, _vm.CostUnavailableMessage);
        CostChart.Invalidate();
    }
    else if (e.PropertyName is nameof(DashboardViewModel.TokenUnavailableMessage))
    {
        TokenChart.Drawable = new TokenBarChartDrawable(
            _vm.TokenChartData.ToList(), _vm.TimeRangeLabel, _vm.TokenUnavailableMessage);
        TokenChart.Invalidate();
    }
}
```

**Verify:** Run the app. Header shows "Anthropic Usage Tracker" with animated spinner on refresh (↻ disappears, ActivityIndicator appears). Switching provider to MiniMaxi changes title to "MiniMaxi Usage Tracker". Daily Cost chart shows unavailability message. Selected time-range button is highlighted with Primary color.

---

### Task 3: Chart drawable redesign

**Files:** `src/ClaudeUsageTracker.Maui/Views/CostBarChartDrawable.cs`, `src/ClaudeUsageTracker.Maui/Views/TokenBarChartDrawable.cs`

Both drawables share the same layout model: left Y-axis with gridlines and labels, bottom X-axis with date labels, bars drawn in the chart area, value labels on each bar, centered message when unavailable or empty.

#### CostBarChartDrawable.cs — full replacement:

```csharp
using ClaudeUsageTracker.Core.ViewModels;

namespace ClaudeUsageTracker.Maui.Views;

public class CostBarChartDrawable(
    IReadOnlyList<DailyUsage> data,
    string? unavailableMessage = null) : IDrawable
{
    private const float LeftPad   = 52f;
    private const float BottomPad = 28f;
    private const float TopPad    = 20f;
    private const float RightPad  = 8f;
    private const int   YDivisions = 4;

    public void Draw(ICanvas canvas, RectF rect)
    {
        float w = rect.Width;
        float h = rect.Height;
        float chartW = w - LeftPad - RightPad;
        float chartH = h - TopPad - BottomPad;

        // Unavailable / empty state
        if (unavailableMessage != null || data.Count == 0)
        {
            string msg = unavailableMessage ?? "No data available";
            canvas.FontColor   = Color.FromArgb("#6E6E6E");
            canvas.FontSize    = 13;
            canvas.DrawString(msg, rect.Left, rect.Top, w, h, HorizontalAlignment.Center, VerticalAlignment.Center);
            // Draw a faint baseline
            canvas.StrokeColor = Color.FromArgb("#C8C8C8");
            canvas.StrokeSize  = 1;
            canvas.DrawLine(LeftPad, h - BottomPad, w - RightPad, h - BottomPad);
            return;
        }

        float maxCost = (float)(data.Max(d => d.CostUsd) == 0 ? 1m : data.Max(d => d.CostUsd));

        // Y-axis gridlines + labels
        for (int j = 0; j <= YDivisions; j++)
        {
            float frac  = j / (float)YDivisions;
            float y     = TopPad + chartH * (1f - frac);
            float value = maxCost * frac;

            // Grid line
            canvas.StrokeColor = Color.FromArgb("#E1E1E1");
            canvas.StrokeSize  = 1;
            canvas.DrawLine(LeftPad, y, w - RightPad, y);

            // Y label
            string label = value < 0.01m ? "$0" : value >= 1m ? $"${value:F2}" : $"${value:F3}";
            canvas.FontColor = Color.FromArgb("#6E6E6E");
            canvas.FontSize  = 9;
            canvas.DrawString(label, 0, y - 6, LeftPad - 4, 14, HorizontalAlignment.Right, VerticalAlignment.Top);
        }

        // Bars
        float totalBarArea = chartW;
        float barWidth     = Math.Max(1, totalBarArea / data.Count - 2f);
        float barSpacing   = totalBarArea / data.Count;

        for (int i = 0; i < data.Count; i++)
        {
            float x    = LeftPad + i * barSpacing + (barSpacing - barWidth) / 2f;
            float frac = (float)(data[i].CostUsd / (decimal)maxCost);
            float barH = chartH * frac;
            float y    = TopPad + chartH - barH;

            // Bar
            canvas.FillColor = Color.FromArgb("#512BD4");
            if (barH > 0) canvas.FillRectangle(x, y, barWidth, barH);

            // Value label on bar (only if bar is tall enough, else above)
            if (data[i].CostUsd > 0)
            {
                string valLabel = data[i].CostUsd < 0.001m ? "<$0.001"
                    : data[i].CostUsd < 0.01m ? $"${data[i].CostUsd:F3}"
                    : $"${data[i].CostUsd:F2}";
                canvas.FontColor = Color.FromArgb("#512BD4");
                canvas.FontSize  = 8;
                float labelY = barH >= 16 ? y + 2 : y - 11;
                canvas.DrawString(valLabel, x, labelY, barWidth, 10, HorizontalAlignment.Center, VerticalAlignment.Top);
            }

            // X-axis date label
            bool showDate = data.Count <= 7
                || (data.Count <= 31 && i % 5 == 0)
                || i == data.Count - 1;
            if (showDate)
            {
                canvas.FontColor = Color.FromArgb("#6E6E6E");
                canvas.FontSize  = 9;
                string dateLabel = data.Count <= 7
                    ? data[i].Date.ToLocalTime().ToString("ddd")
                    : data[i].Date.ToLocalTime().ToString("M/d");
                canvas.DrawString(dateLabel, x, h - BottomPad + 4, barWidth, BottomPad - 4,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
            }
        }

        // Axis line
        canvas.StrokeColor = Color.FromArgb("#ACACAC");
        canvas.StrokeSize  = 1;
        canvas.DrawLine(LeftPad, h - BottomPad, w - RightPad, h - BottomPad);
        canvas.DrawLine(LeftPad, TopPad, LeftPad, h - BottomPad);
    }
}
```

#### TokenBarChartDrawable.cs — full replacement:

```csharp
using ClaudeUsageTracker.Core.ViewModels;

namespace ClaudeUsageTracker.Maui.Views;

public class TokenBarChartDrawable(
    IReadOnlyList<TokenUsage> data,
    string timeRangeLabel,
    string? unavailableMessage = null) : IDrawable
{
    private const float LeftPad    = 60f;
    private const float BottomPad  = 28f;
    private const float TopPad     = 20f;
    private const float RightPad   = 8f;
    private const int   YDivisions = 4;

    public void Draw(ICanvas canvas, RectF rect)
    {
        float w = rect.Width;
        float h = rect.Height;
        float chartW = w - LeftPad - RightPad;
        float chartH = h - TopPad - BottomPad;

        // Unavailable / empty state
        if (unavailableMessage != null || data.Count == 0)
        {
            string msg = unavailableMessage ?? "No token data available";
            canvas.FontColor = Color.FromArgb("#6E6E6E");
            canvas.FontSize  = 13;
            canvas.DrawString(msg, rect.Left, rect.Top, w, h, HorizontalAlignment.Center, VerticalAlignment.Center);
            canvas.StrokeColor = Color.FromArgb("#C8C8C8");
            canvas.StrokeSize  = 1;
            canvas.DrawLine(LeftPad, h - BottomPad, w - RightPad, h - BottomPad);
            return;
        }

        long maxTokens = data.Max(d => d.Tokens) == 0 ? 1 : data.Max(d => d.Tokens);

        // Y-axis gridlines + labels
        for (int j = 0; j <= YDivisions; j++)
        {
            float frac  = j / (float)YDivisions;
            float y     = TopPad + chartH * (1f - frac);
            long  value = (long)(maxTokens * frac);

            canvas.StrokeColor = Color.FromArgb("#E1E1E1");
            canvas.StrokeSize  = 1;
            canvas.DrawLine(LeftPad, y, w - RightPad, y);

            string label = FormatTokens(value);
            canvas.FontColor = Color.FromArgb("#6E6E6E");
            canvas.FontSize  = 9;
            canvas.DrawString(label, 0, y - 6, LeftPad - 4, 14, HorizontalAlignment.Right, VerticalAlignment.Top);
        }

        // Bars
        float barSpacing = chartW / data.Count;
        float barWidth   = Math.Max(1, barSpacing - 2f);

        for (int i = 0; i < data.Count; i++)
        {
            float x    = LeftPad + i * barSpacing + (barSpacing - barWidth) / 2f;
            float frac = (float)data[i].Tokens / maxTokens;
            float barH = chartH * frac;
            float y    = TopPad + chartH - barH;

            canvas.FillColor = Color.FromArgb("#34C759");
            if (barH > 0) canvas.FillRectangle(x, y, barWidth, barH);

            // Value label
            if (data[i].Tokens > 0)
            {
                string valLabel = FormatTokens(data[i].Tokens);
                canvas.FontColor = Color.FromArgb("#2A9E47");
                canvas.FontSize  = 8;
                float labelY = barH >= 16 ? y + 2 : y - 11;
                canvas.DrawString(valLabel, x, labelY, barWidth, 10, HorizontalAlignment.Center, VerticalAlignment.Top);
            }

            // X-axis labels
            bool isHourly = data.Count > 7;
            bool showLabel = data.Count <= 7
                || (isHourly && data.Count <= 24 && i % 3 == 0)
                || (!isHourly && i % 5 == 0)
                || i == data.Count - 1;
            if (showLabel)
            {
                canvas.FontColor = Color.FromArgb("#6E6E6E");
                canvas.FontSize  = 9;
                string dateLabel = data.Count <= 24
                    ? data[i].Date.ToLocalTime().ToString("htt").ToLower()
                    : data.Count <= 7
                        ? data[i].Date.ToLocalTime().ToString("ddd")
                        : data[i].Date.ToLocalTime().ToString("M/d");
                canvas.DrawString(dateLabel, x, h - BottomPad + 4, barWidth, BottomPad - 4,
                    HorizontalAlignment.Center, VerticalAlignment.Top);
            }
        }

        // Axes
        canvas.StrokeColor = Color.FromArgb("#ACACAC");
        canvas.StrokeSize  = 1;
        canvas.DrawLine(LeftPad, h - BottomPad, w - RightPad, h - BottomPad);
        canvas.DrawLine(LeftPad, TopPad, LeftPad, h - BottomPad);
    }

    private static string FormatTokens(long tokens) => tokens switch
    {
        >= 1_000_000 => $"{tokens / 1_000_000.0:F1}M",
        >= 1_000     => $"{tokens / 1_000.0:F0}K",
        _            => tokens.ToString()
    };
}
```

**Verify:** Run the app. Daily Cost chart shows Y-axis with dollar gridlines, X-axis with M/d date labels, dollar values above each bar. Token chart is visually consistent (same layout, green bars, token count labels). Selecting MiniMaxi shows centered message in both chart areas instead of empty space.

---

### Task 4: ProvidersDashboard animated refresh

**Files:** `src/ClaudeUsageTracker.Maui/ViewModels/ProvidersDashboardViewModel.cs`, `src/ClaudeUsageTracker.Maui/Views/ProvidersDashboardPage.xaml`

#### ProvidersDashboardViewModel.cs changes

Add `IsAnyRefreshing` computed property and notification wiring:

```csharp
public bool IsAnyRefreshing => Providers.Any(p => p.IsRefreshing);
```

Subscribe to per-card property changes when cards are added. Replace the card insertion logic in `RefreshProviderAsync`:

```csharp
// After creating card:
card = new ProviderCardViewModel { ProviderName = provider.ProviderName };
card.PropertyChanged += (_, e) =>
{
    if (e.PropertyName == nameof(ProviderCardViewModel.IsRefreshing))
        OnPropertyChanged(nameof(IsAnyRefreshing));
};
```

Also notify `IsAnyRefreshing` at the start/end of `RefreshAllAsync`:

```csharp
[RelayCommand]
public async Task RefreshAllAsync()
{
    HasError = false;
    ErrorMessage = "";
    var tasks = _providers.Select(p => RefreshProviderAsync(p)).ToList();
    await Task.WhenAll(tasks);
    OnPropertyChanged(nameof(IsAnyRefreshing)); // ensure final state notified
}
```

#### ProvidersDashboardPage.xaml changes

Replace the header "Refresh All" button with a button/ActivityIndicator toggle (mirroring the per-card pattern):

```xml
<!-- Header -->
<Grid Grid.Row="0" ColumnDefinitions="*,Auto">
    <Label Grid.Column="0" Text="Plan Usage" FontSize="22" FontAttributes="Bold" VerticalOptions="Center" />
    <Grid Grid.Column="1" WidthRequest="44" HeightRequest="44">
        <Button Text="↻" FontSize="18"
                Command="{Binding RefreshAllCommand}"
                IsVisible="{Binding IsAnyRefreshing, Converter={StaticResource InvertedBoolConverter}}"
                BackgroundColor="Transparent"
                TextColor="{AppThemeBinding Light={StaticResource Gray500}, Dark={StaticResource Gray400}}" />
        <ActivityIndicator IsRunning="{Binding IsAnyRefreshing}"
                           IsVisible="{Binding IsAnyRefreshing}"
                           WidthRequest="22" HeightRequest="22"
                           HorizontalOptions="Center" VerticalOptions="Center" />
    </Grid>
</Grid>
```

**Verify:** Run the app. Tap "Refresh All" (the ↻ header icon) — it disappears and ActivityIndicator spins for the duration of the refresh. Individual provider card refresh icons independently show/hide ActivityIndicator — tapping one card's ↻ does not affect others.
