using ClaudeUsageTracker.Core.ViewModels;

namespace ClaudeUsageTracker.Maui.Views;

public partial class SetupPage : ContentPage
{
    private readonly SetupViewModel _vm;

    public SetupPage(SetupViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        vm.NavigateToDashboard += OnNavigateToDashboard;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }

    private async void OnNavigateToDashboard()
    {
        _vm.NavigateToDashboard -= OnNavigateToDashboard;
        await Shell.Current.GoToAsync("//dashboard");
    }

    private async void OnCopyApiErrorClicked(object? sender, EventArgs e)
    {
        await Clipboard.Default.SetTextAsync(_vm.ApiError);
    }

    // Full implementation added in Task 15 once IClaudeAiUsageService is registered
    private void OnConnectClaudeProClicked(object sender, EventArgs e)
    {
        // Wired in Task 15
    }
}
