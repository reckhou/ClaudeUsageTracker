using ClaudeUsageTracker.Maui.Views;

namespace ClaudeUsageTracker.Maui;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        if (DeviceInfo.Idiom == DeviceIdiom.Phone)
            Routing.RegisterRoute("dashboard", typeof(MobileDashboardPage));
        else
            Routing.RegisterRoute("dashboard", typeof(DashboardPage));
        Routing.RegisterRoute("providers", typeof(ProvidersDashboardPage));
    }
}
