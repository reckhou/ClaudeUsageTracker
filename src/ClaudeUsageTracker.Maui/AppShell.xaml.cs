using ClaudeUsageTracker.Maui.Views;

namespace ClaudeUsageTracker.Maui;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("dashboard", typeof(DashboardPage));
    }
}
