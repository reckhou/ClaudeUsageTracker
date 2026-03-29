using ClaudeUsageTracker.Maui.Services;
using ClaudeUsageTracker.Maui.ViewModels;

namespace ClaudeUsageTracker.Maui.Views;

public partial class MiniModePage : ContentPage
{
    private readonly MiniModeWindowService _windowService;
    private readonly MiniModeViewModel _vm;
    private Window? _settingsWindow;

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
        _vm.Dashboard.Providers.CollectionChanged += OnProvidersChanged;

        if (Window?.Handler?.PlatformView is not null)
            ConfigureAndResize();
        else if (Window is not null)
            Window.HandlerChanged += OnWindowHandlerReady;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.Dashboard.Providers.CollectionChanged -= OnProvidersChanged;
    }

    private void OnWindowHandlerReady(object? sender, EventArgs e)
    {
        if (Window?.Handler?.PlatformView is null) return;
        Window.HandlerChanged -= OnWindowHandlerReady;
        ConfigureAndResize();
    }

    private void ConfigureAndResize()
    {
        _windowService.ConfigureWindow(Window!, _vm.IsAlwaysOnTop, _vm.Opacity);
        _windowService.HideMainWindow();
        _windowService.ResizeForProviderCount(_vm.Dashboard.Providers.Count);
    }

    private void OnProvidersChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        _windowService.ResizeForProviderCount(_vm.Dashboard.Providers.Count);
    }

    private void OnDragStripPressed(object sender, PointerEventArgs e)
    {
        _windowService.StartDrag();
    }

    private void OnSettingsClicked(object sender, EventArgs e)
    {
        if (_settingsWindow != null)
        {
            Application.Current!.CloseWindow(_settingsWindow);
            _settingsWindow = null;
            return;
        }

        // Pass the same _vm instance so settings and mini window share one ViewModel
        var settingsPage = new MiniModeSettingsPage(_vm, _windowService);
        _settingsWindow = new Window(settingsPage) { Title = "Mini Mode Settings" };
        _settingsWindow.Destroying += (_, _) => _settingsWindow = null;
        Application.Current!.OpenWindow(_settingsWindow);
    }

    private void OnReturnToMainClicked(object sender, EventArgs e)
    {
        _windowService.ShowMainWindow();
        Application.Current!.CloseWindow(Window!);
    }
}
