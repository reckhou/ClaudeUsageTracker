namespace ClaudeUsageTracker.Maui.Views;

public partial class ProvidersDashboardPage : ContentPage
{
    private readonly ViewModels.ProvidersDashboardViewModel _vm;

    public ProvidersDashboardPage(ViewModels.ProvidersDashboardViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
        _vm.RefreshCommand.Execute(null);
    }
}
