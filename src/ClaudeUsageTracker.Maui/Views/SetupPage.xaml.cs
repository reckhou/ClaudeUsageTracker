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

    private async void OnNavigateToDashboard()
    {
        _vm.NavigateToDashboard -= OnNavigateToDashboard;
        await Shell.Current.GoToAsync("//dashboard");
    }

    private async void OnCopyErrorClicked(object? sender, EventArgs e)
    {
        await Clipboard.Default.SetTextAsync(_vm.ErrorMessage);
    }
}
