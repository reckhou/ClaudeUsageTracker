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
            var key = await _storage.GetAsync("admin_api_key");
            if (!string.IsNullOrEmpty(key))
                await Shell.Current.GoToAsync("//dashboard");
        }
        catch { /* stay on setup page */ }
    }
}
