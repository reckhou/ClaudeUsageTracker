using ClaudeUsageTracker.Maui.Services;
using ClaudeUsageTracker.Maui.ViewModels;

namespace ClaudeUsageTracker.Maui.Views;

public partial class MiniModePage : ContentPage
{
    private readonly MiniModeWindowService _windowService;
    private readonly MiniModeViewModel _vm;

    public MiniModePage(MiniModeViewModel vm, MiniModeWindowService windowService)
    {
        InitializeComponent();
        _vm = vm;
        _windowService = windowService;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Window handle is ready by OnAppearing — configure chrome, size, opacity, always-on-top
        _windowService.ConfigureWindow(Window, _vm.IsAlwaysOnTop, _vm.Opacity);
    }

    private void OnSettingsToggleClicked(object sender, EventArgs e)
    {
        _vm.IsSettingsExpanded = !_vm.IsSettingsExpanded;
        SettingsToggleButton.Text = _vm.IsSettingsExpanded ? "⚙ Settings ▲" : "⚙ Settings ▼";
    }
}
