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
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // OnAppearing fires during Application.AddWindow, before the WinUI platform
        // window exists. Window.Handler is null at this point in the activation sequence.
        // Defer window configuration to Window.HandlerChanged, which fires after
        // ConnectHandler wires up MauiWinUIWindow as the PlatformView (HWND ready).
        if (Window?.Handler?.PlatformView is not null)
        {
            _windowService.ConfigureWindow(Window, _vm.IsAlwaysOnTop, _vm.Opacity);
        }
        else if (Window is not null)
        {
            Window.HandlerChanged += OnWindowHandlerReady;
        }
    }

    private void OnWindowHandlerReady(object? sender, EventArgs e)
    {
        if (Window?.Handler?.PlatformView is null) return;
        Window.HandlerChanged -= OnWindowHandlerReady;
        _windowService.ConfigureWindow(Window, _vm.IsAlwaysOnTop, _vm.Opacity);
    }

    private void OnSettingsToggleClicked(object sender, EventArgs e)
    {
        _vm.IsSettingsExpanded = !_vm.IsSettingsExpanded;
        SettingsToggleButton.Text = _vm.IsSettingsExpanded ? "⚙ Settings ▲" : "⚙ Settings ▼";
    }
}
