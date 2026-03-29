using ClaudeUsageTracker.Maui.Services;
using ClaudeUsageTracker.Maui.ViewModels;

namespace ClaudeUsageTracker.Maui.Views;

public partial class MiniModeSettingsPage : ContentPage
{
    private readonly MiniModeWindowService _windowService;

    public MiniModeSettingsPage(MiniModeViewModel vm, MiniModeWindowService windowService)
    {
        try { InitializeComponent(); }
        catch (Exception ex)
        {
            var msg = $"[MiniModeSettingsPage] InitializeComponent FAILED: {ex}";
            System.Diagnostics.Debug.WriteLine(msg);
            try
            {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ClaudeUsageTracker", "mini_debug.log");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {msg}\n");
            }
            catch { }
        }
        _windowService = windowService;
        BindingContext = vm;
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
