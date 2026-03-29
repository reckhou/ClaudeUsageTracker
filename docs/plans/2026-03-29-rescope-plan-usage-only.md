# Rescope: Plan Usage Only

**Goal:** Strip the app down to Claude Pro quota + MiniMaxi quota tracking only — remove the Anthropic Admin API usage dashboard and all Google AI support.

**Architecture:** Two quota providers (Claude Pro via WebView injection, MiniMaxi via API) feed into `ProvidersDashboardPage` through `ProvidersDashboardViewModel`. Setup connects providers and routes to the dashboard. `UsageDataService` retains only quota/provider-record persistence (no usage or cost tables). `SetupViewModel` loses its `AnthropicApiService` dependency entirely.

**Tech Stack:** .NET 9 MAUI, CommunityToolkit.Mvvm, sqlite-net-pcl

---

## Progress

- [x] Task 1: Delete dead files
- [x] Task 2: Strip Core library (IUsageDataService, UsageDataService, SetupViewModel)
- [x] Task 3: Strip MAUI app (SetupPage, AppShell, App.cs, MauiProgram)

---

## Files

### Deleted
- `src/ClaudeUsageTracker.Core/Services/AnthropicApiService.cs` — Admin API HTTP client, no longer needed
- `src/ClaudeUsageTracker.Core/Models/UsageRecord.cs` — token usage model, no longer needed
- `src/ClaudeUsageTracker.Core/Models/CostRecord.cs` — billing cost model, no longer needed
- `src/ClaudeUsageTracker.Core/Models/AppConfig.cs` — API key config holder, only used by DashboardViewModel
- `src/ClaudeUsageTracker.Core/ViewModels/DashboardViewModel.cs` — Anthropic usage dashboard VM, entire file removed
- `src/ClaudeUsageTracker.Maui/Services/GoogleAIUsageProvider.cs` — Google AI quota provider, removed
- `src/ClaudeUsageTracker.Maui/Views/DashboardPage.xaml` — Anthropic charts dashboard, removed
- `src/ClaudeUsageTracker.Maui/Views/DashboardPage.xaml.cs` — code-behind for above
- `src/ClaudeUsageTracker.Maui/Views/MobileDashboardPage.xaml` — mobile version of same, removed
- `src/ClaudeUsageTracker.Maui/Views/MobileDashboardPage.xaml.cs` — code-behind for above
- `src/ClaudeUsageTracker.Maui/Views/CostBarChartDrawable.cs` — cost chart renderer, removed
- `src/ClaudeUsageTracker.Maui/Views/TokenBarChartDrawable.cs` — token chart renderer, removed

### Modified
- `src/ClaudeUsageTracker.Core/Services/IUsageDataService.cs` — remove usage/cost CRUD methods
- `src/ClaudeUsageTracker.Core/Services/UsageDataService.cs` — remove UsageRecords/CostRecords table creation and methods
- `src/ClaudeUsageTracker.Core/ViewModels/SetupViewModel.cs` — remove Admin API + Google AI sections; drop `AnthropicApiService` constructor parameter
- `src/ClaudeUsageTracker.Maui/Views/SetupPage.xaml` — remove Admin API card + Google AI card
- `src/ClaudeUsageTracker.Maui/Views/SetupPage.xaml.cs` — remove `OnCopyApiErrorClicked`, update navigation to `//providers`
- `src/ClaudeUsageTracker.Maui/AppShell.xaml` — remove Dashboard shell content entry
- `src/ClaudeUsageTracker.Maui/App.xaml.cs` — update startup routing: check `claude_pro_connected` or `MiniMaxiApiKey`
- `src/ClaudeUsageTracker.Maui/MauiProgram.cs` — remove dead DI registrations

---

### Task 1: Delete dead files

**Files:** (see deleted list above — 12 files)

Delete the following files entirely. No code from them needs to be preserved:

```
src/ClaudeUsageTracker.Core/Services/AnthropicApiService.cs
src/ClaudeUsageTracker.Core/Models/UsageRecord.cs
src/ClaudeUsageTracker.Core/Models/CostRecord.cs
src/ClaudeUsageTracker.Core/Models/AppConfig.cs
src/ClaudeUsageTracker.Core/ViewModels/DashboardViewModel.cs
src/ClaudeUsageTracker.Maui/Services/GoogleAIUsageProvider.cs
src/ClaudeUsageTracker.Maui/Views/DashboardPage.xaml
src/ClaudeUsageTracker.Maui/Views/DashboardPage.xaml.cs
src/ClaudeUsageTracker.Maui/Views/MobileDashboardPage.xaml
src/ClaudeUsageTracker.Maui/Views/MobileDashboardPage.xaml.cs
src/ClaudeUsageTracker.Maui/Views/CostBarChartDrawable.cs
src/ClaudeUsageTracker.Maui/Views/TokenBarChartDrawable.cs
```

**Verify:** All 12 files no longer exist in the filesystem. The solution will not compile yet — that's expected.

---

### Task 2: Strip Core library

**Files:**
- `src/ClaudeUsageTracker.Core/Services/IUsageDataService.cs`
- `src/ClaudeUsageTracker.Core/Services/UsageDataService.cs`
- `src/ClaudeUsageTracker.Core/ViewModels/SetupViewModel.cs`

**Depends on:** Task 1

#### IUsageDataService.cs

Replace with quota-only interface (remove `UpsertUsageRecordsAsync`, `UpsertCostRecordsAsync`, `GetUsageAsync`, `GetCostsAsync`, `GetLastFetchedAtAsync`):

```csharp
using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Core.Services;

public interface IUsageDataService
{
    Task InitAsync();
    Task UpsertQuotaRecordAsync(QuotaRecord record);
    Task<QuotaRecord?> GetLatestQuotaAsync();
    Task UpsertProviderRecordAsync(ProviderUsageRecord record);
    Task<List<ProviderUsageRecord>> GetAllProviderRecordsAsync();
}
```

#### UsageDataService.cs

Read the file first, then remove:
- `using` imports for `UsageRecord`/`CostRecord` (if any)
- `CreateTableAsync<UsageRecord>()` and `CreateTableAsync<CostRecord>()` calls from `InitAsync`
- Methods: `UpsertUsageRecordsAsync`, `UpsertCostRecordsAsync`, `GetUsageAsync`, `GetCostsAsync`, `GetLastFetchedAtAsync`

Keep: `InitAsync` (minus the removed table creations), `UpsertQuotaRecordAsync`, `GetLatestQuotaAsync`, `UpsertProviderRecordAsync`, `GetAllProviderRecordsAsync`.

#### SetupViewModel.cs

Replace entire file — removes `AnthropicApiService` dependency, Admin API fields/commands, Google AI fields/commands:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeUsageTracker.Core.Services;

namespace ClaudeUsageTracker.Core.ViewModels;

public partial class SetupViewModel(
    ISecureStorageService storage) : ObservableObject
{
    // Claude Pro section
    [ObservableProperty] private bool _isClaudeProConnected;
    [ObservableProperty] private string _claudeProStatus = "Not connected";

    // MiniMaxi section
    [ObservableProperty] private bool _isMiniMaxiConnected;
    [ObservableProperty] private string _miniMaxiApiKey = "";
    [ObservableProperty] private bool _isValidatingMiniMaxi;

    public event Action? NavigateToDashboard;

    public async Task LoadAsync()
    {
        var proConnected = await storage.GetAsync("claude_pro_connected");
        IsClaudeProConnected = proConnected == "true";
        ClaudeProStatus = IsClaudeProConnected ? "Connected" : "Not connected";

        var miniKey = await storage.GetAsync("MiniMaxiApiKey");
        IsMiniMaxiConnected = !string.IsNullOrEmpty(miniKey);
        MiniMaxiApiKey = IsMiniMaxiConnected ? "••••••••••" : "";
    }

    [RelayCommand]
    public async Task DisconnectClaudeProAsync()
    {
        await storage.RemoveAsync("claude_pro_connected");
        IsClaudeProConnected = false;
        ClaudeProStatus = "Not connected";
    }

    [RelayCommand]
    public async Task SaveMiniMaxiApiKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(MiniMaxiApiKey) || MiniMaxiApiKey.StartsWith("•")) return;
        IsValidatingMiniMaxi = true;
        await storage.SetAsync("MiniMaxiApiKey", MiniMaxiApiKey);
        IsMiniMaxiConnected = true;
        MiniMaxiApiKey = "••••••••••";
        IsValidatingMiniMaxi = false;
    }

    [RelayCommand]
    public async Task DisconnectMiniMaxiAsync()
    {
        await storage.RemoveAsync("MiniMaxiApiKey");
        IsMiniMaxiConnected = false;
        MiniMaxiApiKey = "";
    }

    [RelayCommand]
    public void GoToDashboard() => NavigateToDashboard?.Invoke();
}
```

**Verify:** Core project compiles cleanly (`dotnet build src/ClaudeUsageTracker.Core`). No references to `AnthropicApiService`, `UsageRecord`, `CostRecord`, or `AppConfig` remain in Core.

---

### Task 3: Strip MAUI app

**Files:**
- `src/ClaudeUsageTracker.Maui/Views/SetupPage.xaml`
- `src/ClaudeUsageTracker.Maui/Views/SetupPage.xaml.cs`
- `src/ClaudeUsageTracker.Maui/AppShell.xaml`
- `src/ClaudeUsageTracker.Maui/App.xaml.cs`
- `src/ClaudeUsageTracker.Maui/MauiProgram.cs`

**Depends on:** Task 2

#### SetupPage.xaml

Replace entire file — remove Admin API card, Google AI card, update subtitle text:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="ClaudeUsageTracker.Maui.Views.SetupPage"
             Title="Setup"
             BackgroundColor="{AppThemeBinding Light={StaticResource White}, Dark={StaticResource Black}}">

    <ScrollView>
      <VerticalStackLayout Padding="24" Spacing="20">

        <Label Text="Plan Usage Tracker" FontSize="26" FontAttributes="Bold" HorizontalOptions="Center" />
        <Label Text="Connect one or both sources to see your quota usage."
               FontSize="13" HorizontalOptions="Center" HorizontalTextAlignment="Center"
               TextColor="{AppThemeBinding Light={StaticResource Gray500}, Dark={StaticResource Gray400}}" />

        <!-- Claude Pro Card -->
        <Frame CornerRadius="10" Padding="20"
               BackgroundColor="{AppThemeBinding Light={StaticResource Gray100}, Dark={StaticResource Gray900}}">
          <VerticalStackLayout Spacing="12">
            <Label Text="Claude Plan" FontSize="16" FontAttributes="Bold" />
            <Label Text="Shows current session and weekly quota usage for your Claude account."
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

        <!-- MiniMaxi Card -->
        <Frame CornerRadius="10" Padding="20"
               BackgroundColor="{AppThemeBinding Light={StaticResource Gray100}, Dark={StaticResource Gray900}}">
          <VerticalStackLayout Spacing="12">
            <Label Text="MiniMaxi" FontSize="16" FontAttributes="Bold" />
            <Label Text="Tracks coding plan usage for your MiniMaxi account."
                   FontSize="12" TextColor="{AppThemeBinding Light={StaticResource Gray500}, Dark={StaticResource Gray400}}" />

            <Grid IsVisible="{Binding IsMiniMaxiConnected, Converter={StaticResource InvertedBoolConverter}}"
                  ColumnDefinitions="*,Auto" ColumnSpacing="8">
              <Entry Grid.Column="0" Placeholder="Bearer token..." IsPassword="True"
                     Text="{Binding MiniMaxiApiKey}" />
              <Button Grid.Column="1" Text="Connect" Command="{Binding SaveMiniMaxiApiKeyCommand}"
                      IsEnabled="{Binding IsValidatingMiniMaxi, Converter={StaticResource InvertedBoolConverter}}" />
            </Grid>

            <Grid IsVisible="{Binding IsMiniMaxiConnected}" ColumnDefinitions="*,Auto">
              <Label Grid.Column="0" Text="✓ Connected" VerticalOptions="Center"
                     TextColor="{StaticResource Primary}" />
              <Button Grid.Column="1" Text="Disconnect" Command="{Binding DisconnectMiniMaxiCommand}"
                      BackgroundColor="Transparent"
                      TextColor="{AppThemeBinding Light={StaticResource Gray500}, Dark={StaticResource Gray400}}"
                      FontSize="12" />
            </Grid>

            <ActivityIndicator IsRunning="{Binding IsValidatingMiniMaxi}" IsVisible="{Binding IsValidatingMiniMaxi}" />
          </VerticalStackLayout>
        </Frame>

        <Button Text="Go to Dashboard" Command="{Binding GoToDashboardCommand}"
                HorizontalOptions="Fill" Margin="0,8,0,0" />

      </VerticalStackLayout>
    </ScrollView>
</ContentPage>
```

#### SetupPage.xaml.cs

Replace entire file — remove `OnCopyApiErrorClicked`, update navigation target to `//providers`:

```csharp
using ClaudeUsageTracker.Core.Services;
using ClaudeUsageTracker.Core.ViewModels;

namespace ClaudeUsageTracker.Maui.Views;

public partial class SetupPage : ContentPage
{
    private readonly SetupViewModel _vm;

    public SetupPage(SetupViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        vm.NavigateToDashboard += OnNavigateToDashboard;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }

    private async void OnNavigateToDashboard()
    {
        _vm.NavigateToDashboard -= OnNavigateToDashboard;
        await Shell.Current.GoToAsync("//providers");
    }

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
            _vm.IsClaudeProConnected = true;
            _vm.ClaudeProStatus = $"Connected · Session {record.FiveHourUtilization}% · Weekly {record.SevenDayUtilization}%";
        }
    }
}
```

#### AppShell.xaml

Remove the Dashboard shell content entry — keep only providers and setup:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<Shell
    x:Class="ClaudeUsageTracker.Maui.AppShell"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:views="clr-namespace:ClaudeUsageTracker.Maui.Views"
    Title="Plan Usage Tracker">

    <ShellContent
        Title="Providers"
        ContentTemplate="{DataTemplate views:ProvidersDashboardPage}"
        Route="providers" />

    <ShellContent
        Title="Setup"
        ContentTemplate="{DataTemplate views:SetupPage}"
        Route="setup" />

</Shell>
```

#### App.xaml.cs

Update startup routing — check `claude_pro_connected` or `MiniMaxiApiKey` (no longer checks `admin_api_key`):

```csharp
using ClaudeUsageTracker.Core.Services;

namespace ClaudeUsageTracker.Maui;

public partial class App : Application
{
    private readonly ISecureStorageService _storage;

    public App(ISecureStorageService storage)
    {
        InitializeComponent();
        _storage = storage;

        TaskScheduler.UnobservedTaskException += (_, args) => { args.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, _) => { };
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }

    protected override async void OnStart()
    {
        base.OnStart();
        try
        {
            var pro = await _storage.GetAsync("claude_pro_connected");
            var mini = await _storage.GetAsync("MiniMaxiApiKey");
            if (pro == "true" || !string.IsNullOrEmpty(mini))
                await Shell.Current.GoToAsync("//providers");
        }
        catch { /* stay on setup page */ }
    }
}
```

#### MauiProgram.cs

Remove: `AnthropicApiService`, `GoogleAIUsageProvider`, `DashboardViewModel`, `DashboardPage`, `MobileDashboardPage` registrations. Update `SetupViewModel` registration (no longer needs `AnthropicApiService`):

```csharp
using CommunityToolkit.Maui;
using ClaudeUsageTracker.Core.Services;
using ClaudeUsageTracker.Core.ViewModels;
using ClaudeUsageTracker.Maui.Services;
using ClaudeUsageTracker.Maui.Views;
using ClaudeUsageTracker.Maui.ViewModels;
using Microsoft.Extensions.Logging;

namespace ClaudeUsageTracker.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<ISecureStorageService, MauiSecureStorageService>();
        builder.Services.AddSingleton<HttpClient>();
        builder.Services.AddSingleton<IUsageDataService>(_ =>
        {
            var path = Path.Combine(FileSystem.AppDataDirectory, "usage.db");
            return new UsageDataService(path);
        });
        builder.Services.AddSingleton<IClaudeAiUsageService, ClaudeAiUsageService>();
        builder.Services.AddSingleton<IUsageProvider, MiniMaxiUsageProvider>();
        builder.Services.AddSingleton<IUsageProvider, ClaudeProUsageProvider>();
        builder.Services.AddTransient<SetupViewModel>();
        builder.Services.AddSingleton<ProvidersDashboardViewModel>(sp =>
            new ProvidersDashboardViewModel(
                sp.GetRequiredService<IUsageDataService>() as UsageDataService
                    ?? throw new InvalidOperationException("UsageDataService must be UsageDataService"),
                sp.GetRequiredService<IEnumerable<IUsageProvider>>(),
                sp.GetRequiredService<ISecureStorageService>()));
        builder.Services.AddTransient<SetupPage>();
        builder.Services.AddTransient<ProvidersDashboardPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
```

**Verify:** `dotnet build src/ClaudeUsageTracker.Maui` produces zero errors. App launches, shows Setup page with only Claude Pro and MiniMaxi cards. Connecting either routes to `//providers`. No Dashboard tab appears in the shell.
