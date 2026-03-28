using ClaudeUsageTracker.Core.Services;

namespace ClaudeUsageTracker.Maui;

public partial class App : Application
{
    private readonly ISecureStorageService _storage;

    public App(ISecureStorageService storage)
    {
        InitializeComponent();
        _storage = storage;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }

    protected override async void OnStart()
    {
        base.OnStart();
        var key = await _storage.GetAsync("admin_api_key");
        if (!string.IsNullOrEmpty(key))
        {
            await Shell.Current.GoToAsync("//dashboard");
        }
    }
}
