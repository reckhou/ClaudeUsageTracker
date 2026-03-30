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
        var ver = string.Join(".", AppInfo.VersionString.Split('.').Take(3));
        return new Window(new AppShell()) { Title = $"Claude Usage Tracker v{ver}" };
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
