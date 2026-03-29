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
            var pro = await _storage.GetAsync("claude_pro_connected");
            if (!string.IsNullOrEmpty(key) || pro == "true")
                await Shell.Current.GoToAsync("//providers");
        }
        catch { /* stay on setup page */ }
    }
}
