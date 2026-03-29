using ClaudeUsageTracker.Maui.Services;
using ClaudeUsageTracker.Maui.ViewModels;

namespace ClaudeUsageTracker.Maui.Views;

public partial class MiniModeSettingsPage : ContentPage
{
    private readonly MiniModeWindowService _windowService;

    public MiniModeSettingsPage(MiniModeViewModel vm, MiniModeWindowService windowService)
    {
        InitializeComponent();
        _windowService = windowService;
        BindingContext = vm; // shares the same MiniModeViewModel instance as MiniModePage
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (Window?.Handler?.PlatformView is not null)
            _windowService.ConfigureSettingsWindow(Window);
        else if (Window is not null)
            Window.HandlerChanged += OnWindowHandlerReady;
    }

    private void OnWindowHandlerReady(object? sender, EventArgs e)
    {
        if (Window?.Handler?.PlatformView is null) return;
        Window.HandlerChanged -= OnWindowHandlerReady;
        _windowService.ConfigureSettingsWindow(Window);
    }
}
