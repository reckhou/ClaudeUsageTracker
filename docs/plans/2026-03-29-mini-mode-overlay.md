# Mini Mode Overlay — Implementation Plan

**Goal:** Add a compact always-on-top floating window that shows live provider quota usage with a foldable settings panel (transparency, always-on-top, auto-refresh).

**Architecture:** A new `MiniModePage` binds to a `MiniModeViewModel` which holds the mini-mode-specific settings (`Opacity`, `IsAlwaysOnTop`, `IsSettingsExpanded`) and exposes the singleton `ProvidersDashboardViewModel` as a `Dashboard` property so the XAML can bind directly to providers and auto-refresh controls. A singleton `MiniModeWindowService` stores the HWND and `OverlappedPresenter` after initial window setup and exposes `SetOpacity()` / `SetAlwaysOnTop()` for on-demand updates. Window transparency is applied at the Win32 compositor level via `SetLayeredWindowAttributes(LWA_ALPHA)`.

**Tech Stack:** .NET MAUI + CommunityToolkit.Mvvm, WinUI3 `Microsoft.UI.Windowing.OverlappedPresenter`, Win32 P/Invoke (`SetLayeredWindowAttributes`, `SetWindowLong`), `#if WINDOWS` guards throughout the service.

---

## Progress

- [x] Task 1: MiniModePage + MiniModeViewModel + MiniModeWindowService
- [ ] Task 2: Launch button + DI wiring

---

## Files

- Create: `src/ClaudeUsageTracker.Maui/Views/MiniModePage.xaml` — compact provider list + foldable settings panel
- Create: `src/ClaudeUsageTracker.Maui/Views/MiniModePage.xaml.cs` — code-behind, triggers window config on appear
- Create: `src/ClaudeUsageTracker.Maui/ViewModels/MiniModeViewModel.cs` — settings state, exposes Dashboard VM for binding
- Create: `src/ClaudeUsageTracker.Maui/Services/MiniModeWindowService.cs` — HWND interop: configure, opacity, always-on-top
- Modify: `src/ClaudeUsageTracker.Maui/Views/ProvidersDashboardPage.xaml` — add Mini Mode toggle button in header
- Modify: `src/ClaudeUsageTracker.Maui/Views/ProvidersDashboardPage.xaml.cs` — open/close mini window, track window reference
- Modify: `src/ClaudeUsageTracker.Maui/MauiProgram.cs` — register `MiniModePage`, `MiniModeViewModel`, `MiniModeWindowService`

---

### Task 1: MiniModePage + MiniModeViewModel + MiniModeWindowService

**Files:**
- `src/ClaudeUsageTracker.Maui/ViewModels/MiniModeViewModel.cs`
- `src/ClaudeUsageTracker.Maui/Services/MiniModeWindowService.cs`
- `src/ClaudeUsageTracker.Maui/Views/MiniModePage.xaml`
- `src/ClaudeUsageTracker.Maui/Views/MiniModePage.xaml.cs`

---

#### MiniModeViewModel.cs

Owns the mini window's own settings. Exposes `Dashboard` so XAML can data-bind directly to providers and auto-refresh without needing proxy properties. When `Opacity` or `IsAlwaysOnTop` changes, calls the window service immediately.

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using ClaudeUsageTracker.Maui.Services;
using ClaudeUsageTracker.Maui.ViewModels;

namespace ClaudeUsageTracker.Maui.ViewModels;

public partial class MiniModeViewModel : ObservableObject
{
    private readonly MiniModeWindowService _windowService;

    // Exposes the singleton dashboard VM so XAML can bind to Providers,
    // AutoRefreshMinutes, IsAutoRefreshRunning, ToggleAutoRefreshCommand directly.
    public ProvidersDashboardViewModel Dashboard { get; }

    [ObservableProperty] private bool _isSettingsExpanded;

    // Opacity: 0.3 (30% visible) to 1.0 (fully opaque). Default 0.95.
    private double _opacity = 0.95;
    public double Opacity
    {
        get => _opacity;
        set
        {
            if (SetProperty(ref _opacity, Math.Clamp(value, 0.3, 1.0)))
            {
                OnPropertyChanged(nameof(OpacityPercent));
                _windowService.SetOpacity(_opacity);
            }
        }
    }

    // Display value for the label next to the slider, e.g. "95%"
    public string OpacityPercent => $"{(int)(_opacity * 100)}%";

    private bool _isAlwaysOnTop = true;
    public bool IsAlwaysOnTop
    {
        get => _isAlwaysOnTop;
        set
        {
            if (SetProperty(ref _isAlwaysOnTop, value))
                _windowService.SetAlwaysOnTop(value);
        }
    }

    public MiniModeViewModel(ProvidersDashboardViewModel dashboard, MiniModeWindowService windowService)
    {
        Dashboard = dashboard;
        _windowService = windowService;
    }
}
```

---

#### MiniModeWindowService.cs

Singleton service that stores HWND and `OverlappedPresenter` after `ConfigureWindow` is called, then allows on-demand opacity and always-on-top updates. All platform code is guarded with `#if WINDOWS`.

```csharp
namespace ClaudeUsageTracker.Maui.Services;

public class MiniModeWindowService
{
#if WINDOWS
    private IntPtr _hwnd;
    private Microsoft.UI.Windowing.OverlappedPresenter? _presenter;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    private const int GWL_EXSTYLE   = -20;
    private const int WS_EX_LAYERED = 0x00080000;
    private const uint LWA_ALPHA    = 0x00000002;
#endif

    public void ConfigureWindow(Window window, bool isAlwaysOnTop, double opacity)
    {
#if WINDOWS
        var nativeWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException("No native WinUI window.");

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

        // Initial size — resizable so user can grow it; no maximize button.
        appWindow.Resize(new Windows.Graphics.SizeInt32(460, 260));

        if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            _presenter = presenter;
            presenter.IsMaximizable = false;
            presenter.IsAlwaysOnTop = isAlwaysOnTop;
            // IsResizable stays true (default) — user can resize the window freely
        }

        // Enable layered window style so SetLayeredWindowAttributes works
        var exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED);

        // Apply initial opacity
        SetOpacity(opacity);
#endif
    }

    public void SetOpacity(double opacity)
    {
#if WINDOWS
        if (_hwnd == IntPtr.Zero) return;
        var alpha = (byte)(Math.Clamp(opacity, 0.3, 1.0) * 255);
        SetLayeredWindowAttributes(_hwnd, 0, alpha, LWA_ALPHA);
#endif
    }

    public void SetAlwaysOnTop(bool alwaysOnTop)
    {
#if WINDOWS
        if (_presenter is not null)
            _presenter.IsAlwaysOnTop = alwaysOnTop;
#endif
    }
}
```

---

#### MiniModePage.xaml

Readable font sizes (15px provider name, 13px labels), thick progress bars (HeightRequest=12). Settings panel is collapsed by default and expands inline below the provider list.

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:ClaudeUsageTracker.Maui.ViewModels"
             xmlns:controls="clr-namespace:ClaudeUsageTracker.Maui.Controls"
             x:Class="ClaudeUsageTracker.Maui.Views.MiniModePage"
             BackgroundColor="{AppThemeBinding Light={StaticResource White}, Dark={StaticResource Black}}">

    <ScrollView>
        <VerticalStackLayout Padding="14,10" Spacing="0">

            <!-- ═══ PROVIDER LIST ═══ -->

            <!-- Empty state -->
            <Label Text="No providers — use Refresh All in main window"
                   FontSize="13" HorizontalOptions="Center" Margin="0,16"
                   TextColor="{AppThemeBinding Light={StaticResource Gray500}, Dark={StaticResource Gray400}}"
                   IsVisible="{Binding Dashboard.Providers.Count, Converter={StaticResource CountToBoolConverter}}" />

            <!-- Provider rows -->
            <CollectionView ItemsSource="{Binding Dashboard.Providers}"
                            IsVisible="{Binding Dashboard.Providers.Count, Converter={StaticResource CountToBoolConverter}, ConverterParameter=inverse}">
                <CollectionView.ItemTemplate>
                    <DataTemplate x:DataType="vm:ProviderCardViewModel">
                        <VerticalStackLayout Spacing="6" Padding="0,8,0,8">

                            <!-- Provider name + spinner -->
                            <Grid ColumnDefinitions="*,Auto">
                                <Label Grid.Column="0"
                                       Text="{Binding ProviderName}"
                                       FontSize="15" FontAttributes="Bold"
                                       VerticalOptions="Center" />
                                <ActivityIndicator Grid.Column="1"
                                                   IsRunning="{Binding IsRefreshing}"
                                                   IsVisible="{Binding IsRefreshing}"
                                                   WidthRequest="16" HeightRequest="16" />
                            </Grid>

                            <!-- Session row -->
                            <Grid ColumnDefinitions="56,*,50" ColumnSpacing="8" VerticalOptions="Center">
                                <Label Grid.Column="0" Text="Session" FontSize="13"
                                       TextColor="{AppThemeBinding Light={StaticResource Gray600}, Dark={StaticResource Gray400}}"
                                       VerticalOptions="Center" />
                                <controls:ColorProgressBar Grid.Column="1"
                                                           ProgressPercent="{Binding IntervalUtilization}"
                                                           HeightRequest="12" />
                                <Label Grid.Column="2"
                                       Text="{Binding IntervalUtilization, StringFormat='{0}%'}"
                                       FontSize="13" FontAttributes="Bold"
                                       HorizontalTextAlignment="End" VerticalOptions="Center" />
                            </Grid>

                            <!-- Weekly row -->
                            <Grid ColumnDefinitions="56,*,50" ColumnSpacing="8" VerticalOptions="Center">
                                <Label Grid.Column="0" Text="Weekly" FontSize="13"
                                       TextColor="{AppThemeBinding Light={StaticResource Gray600}, Dark={StaticResource Gray400}}"
                                       VerticalOptions="Center" />
                                <controls:ColorProgressBar Grid.Column="1"
                                                           ProgressPercent="{Binding WeeklyUtilization}"
                                                           HeightRequest="12" />
                                <Label Grid.Column="2"
                                       Text="{Binding WeeklyUtilization, StringFormat='{0}%'}"
                                       FontSize="13" FontAttributes="Bold"
                                       HorizontalTextAlignment="End" VerticalOptions="Center" />
                            </Grid>

                            <!-- Resets-at hint -->
                            <Label Text="{Binding IntervalResetsAt}" FontSize="11"
                                   TextColor="{AppThemeBinding Light={StaticResource Gray500}, Dark={StaticResource Gray400}}"
                                   HorizontalOptions="End" />

                            <!-- Divider -->
                            <BoxView HeightRequest="1" Margin="0,4,0,0"
                                     Color="{AppThemeBinding Light={StaticResource Gray200}, Dark={StaticResource Gray700}}" />

                        </VerticalStackLayout>
                    </DataTemplate>
                </CollectionView.ItemTemplate>
            </CollectionView>

            <!-- ═══ SETTINGS TOGGLE ═══ -->
            <Button Text="{Binding IsSettingsExpanded, StringFormat={x:Null}}"
                    x:Name="SettingsToggleButton"
                    Clicked="OnSettingsToggleClicked"
                    FontSize="12"
                    BackgroundColor="Transparent"
                    TextColor="{AppThemeBinding Light={StaticResource Gray500}, Dark={StaticResource Gray400}}"
                    HorizontalOptions="Start"
                    Margin="0,6,0,0" />

            <!-- ═══ SETTINGS PANEL (foldable) ═══ -->
            <VerticalStackLayout IsVisible="{Binding IsSettingsExpanded}"
                                 Spacing="12" Padding="0,10,0,6">

                <!-- Separator -->
                <BoxView HeightRequest="1"
                         Color="{AppThemeBinding Light={StaticResource Gray200}, Dark={StaticResource Gray700}}" />

                <!-- Opacity -->
                <Grid ColumnDefinitions="100,*,40" ColumnSpacing="10" VerticalOptions="Center">
                    <Label Grid.Column="0" Text="Transparency" FontSize="13" VerticalOptions="Center" />
                    <Slider Grid.Column="1"
                            Minimum="0.3" Maximum="1.0"
                            Value="{Binding Opacity}"
                            VerticalOptions="Center" />
                    <Label Grid.Column="2"
                           Text="{Binding OpacityPercent}"
                           FontSize="13" HorizontalTextAlignment="End" VerticalOptions="Center" />
                </Grid>

                <!-- Always on Top -->
                <Grid ColumnDefinitions="*,Auto" VerticalOptions="Center">
                    <Label Grid.Column="0" Text="Always on Top" FontSize="13" VerticalOptions="Center" />
                    <Switch Grid.Column="1" IsToggled="{Binding IsAlwaysOnTop}" VerticalOptions="Center" />
                </Grid>

                <!-- Auto Refresh toggle -->
                <Grid ColumnDefinitions="*,Auto" VerticalOptions="Center">
                    <Label Grid.Column="0" Text="Auto Refresh" FontSize="13" VerticalOptions="Center" />
                    <Switch Grid.Column="1"
                            IsToggled="{Binding Dashboard.IsAutoRefreshRunning}"
                            IsEnabled="False"
                            VerticalOptions="Center" />
                </Grid>

                <!-- Auto Refresh interval + start/stop button -->
                <Grid ColumnDefinitions="Auto,60,Auto,*" ColumnSpacing="8" VerticalOptions="Center">
                    <Label Grid.Column="0" Text="Every" FontSize="13" VerticalOptions="Center" />
                    <Entry Grid.Column="1"
                           Text="{Binding Dashboard.AutoRefreshMinutes}"
                           Keyboard="Numeric" FontSize="13"
                           HorizontalTextAlignment="Center" VerticalOptions="Center"
                           IsEnabled="{Binding Dashboard.IsAutoRefreshRunning, Converter={StaticResource InvertedBoolConverter}}" />
                    <Label Grid.Column="2" Text="min" FontSize="13" VerticalOptions="Center" />
                    <Button Grid.Column="3"
                            Text="{Binding Dashboard.AutoRefreshToggleText}"
                            Command="{Binding Dashboard.ToggleAutoRefreshCommand}"
                            FontSize="13" HorizontalOptions="Start" />
                </Grid>

            </VerticalStackLayout>

        </VerticalStackLayout>
    </ScrollView>
</ContentPage>
```

**Note on the settings toggle button text:** The `StringFormat` trick won't work well for a boolean-to-string conversion. Instead, set the button text in code-behind from the `Clicked` handler (simpler than a converter for one label).

---

#### MiniModePage.xaml.cs

```csharp
using ClaudeUsageTracker.Maui.Services;
using ClaudeUsageTracker.Maui.ViewModels;

namespace ClaudeUsageTracker.Maui.Views;

public partial class MiniModePage : ContentPage
{
    private readonly MiniModeWindowService _windowService;
    private readonly MiniModeViewModel _vm;

    public MiniModePage(MiniModeViewModel vm, MiniModeWindowService windowService)
    {
        InitializeComponent();
        _vm = vm;
        _windowService = windowService;
        BindingContext = vm;
        UpdateSettingsButtonText();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Window handle is ready by OnAppearing — configure chrome and initial opacity
        _windowService.ConfigureWindow(Window, _vm.IsAlwaysOnTop, _vm.Opacity);
    }

    private void OnSettingsToggleClicked(object sender, EventArgs e)
    {
        _vm.IsSettingsExpanded = !_vm.IsSettingsExpanded;
        UpdateSettingsButtonText();
    }

    private void UpdateSettingsButtonText()
    {
        SettingsToggleButton.Text = _vm.IsSettingsExpanded ? "⚙ Settings ▲" : "⚙ Settings ▼";
    }
}
```

**Verify:** Build and run (Task 2 must be complete first). Mini window opens at ~460×260. Provider names are 15px bold. Progress bars are visually thick (12px). Clicking "⚙ Settings ▼" expands the settings panel; clicking again collapses it. Dragging the Transparency slider makes the entire window (content + chrome) fade. Toggling Always on Top switch makes the window go behind other windows when clicked away. Auto Refresh start/stop button in the settings panel works identically to the main window's control.

---

### Task 2: Launch button + DI wiring

**Files:**
- `src/ClaudeUsageTracker.Maui/Views/ProvidersDashboardPage.xaml`
- `src/ClaudeUsageTracker.Maui/Views/ProvidersDashboardPage.xaml.cs`
- `src/ClaudeUsageTracker.Maui/MauiProgram.cs`

**Depends on:** Task 1

---

#### ProvidersDashboardPage.xaml — add Mini button to header

Extend header `Grid` from `ColumnDefinitions="*,Auto"` to `ColumnDefinitions="*,Auto,Auto"`. Insert Mini button as Column 1, shift Refresh All to Column 2:

```xml
<!-- Header -->
<Grid Grid.Row="0" ColumnDefinitions="*,Auto,Auto" ColumnSpacing="4">
    <Label Grid.Column="0" Text="Plan Usage" FontSize="22" FontAttributes="Bold" VerticalOptions="Center" />

    <!-- Mini Mode toggle -->
    <Button Grid.Column="1"
            x:Name="MiniModeButton"
            Text="Mini"
            FontSize="13"
            Clicked="OnMiniModeClicked"
            BackgroundColor="Transparent"
            TextColor="{AppThemeBinding Light={StaticResource Gray500}, Dark={StaticResource Gray400}}" />

    <!-- Refresh All (unchanged, shifted to Column 2) -->
    <Grid Grid.Column="2">
        <Button Text="Refresh All" FontSize="13"
                Command="{Binding RefreshAllCommand}"
                IsVisible="{Binding ShowRefreshAllSpinner, Converter={StaticResource InvertedBoolConverter}}"
                BackgroundColor="Transparent"
                TextColor="{AppThemeBinding Light={StaticResource Gray500}, Dark={StaticResource Gray400}}" />
        <ActivityIndicator IsRunning="{Binding ShowRefreshAllSpinner}"
                           IsVisible="{Binding ShowRefreshAllSpinner}"
                           WidthRequest="22" HeightRequest="22"
                           HorizontalOptions="Center" VerticalOptions="Center" />
    </Grid>
</Grid>
```

---

#### ProvidersDashboardPage.xaml.cs — open/close logic

```csharp
using ClaudeUsageTracker.Maui.ViewModels;

namespace ClaudeUsageTracker.Maui.Views;

public partial class ProvidersDashboardPage : ContentPage
{
    private Window? _miniWindow;

    public ProvidersDashboardPage(ProvidersDashboardViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    private void OnMiniModeClicked(object sender, EventArgs e)
    {
        if (_miniWindow != null)
        {
            Application.Current!.CloseWindow(_miniWindow);
            // _miniWindow is cleared by the Destroying handler below
            return;
        }

        var miniPage = Handler!.MauiContext!.Services.GetRequiredService<MiniModePage>();
        _miniWindow = new Window(miniPage) { Title = "Claude Usage" };
        _miniWindow.Destroying += (_, _) =>
        {
            _miniWindow = null;
            MiniModeButton.Text = "Mini";
        };
        Application.Current!.OpenWindow(_miniWindow);
        MiniModeButton.Text = "✕ Mini";
    }
}
```

---

#### MauiProgram.cs — register new types

Add these three lines alongside the existing registrations:

```csharp
// Mini Mode
builder.Services.AddSingleton<MiniModeWindowService>();
builder.Services.AddTransient<MiniModeViewModel>();
builder.Services.AddTransient<MiniModePage>();
```

`MiniModeWindowService` is **singleton** — it must hold the HWND across the mini window's lifetime. `MiniModeViewModel` and `MiniModePage` are **transient** — a fresh instance is created each time the mini window is opened.

**Verify:** Click "Mini" in the dashboard header — second window opens at ~460×260 with readable 15px text and thick bars. Button changes to "✕ Mini". Open settings panel, drag Transparency slider from right to left — window fades progressively. Toggle Always on Top off — clicking away lets other windows cover the mini window. Start/stop Auto Refresh from the settings panel — the main window's auto-refresh indicator also responds (same singleton VM). Close mini window via title bar X — main window button resets to "Mini".
