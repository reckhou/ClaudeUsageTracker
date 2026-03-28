using ClaudeUsageTracker.Core.ViewModels;

namespace ClaudeUsageTracker.Maui.Views;

public partial class MobileDashboardPage : ContentPage
{
    private readonly DashboardViewModel _vm;

    public MobileDashboardPage(DashboardViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.RefreshCommand.Execute(null);
    }
}
