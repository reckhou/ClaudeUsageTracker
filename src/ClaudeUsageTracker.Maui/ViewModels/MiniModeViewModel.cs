using CommunityToolkit.Mvvm.ComponentModel;
using ClaudeUsageTracker.Maui.Services;

namespace ClaudeUsageTracker.Maui.ViewModels;

public partial class MiniModeViewModel : ObservableObject
{
    private readonly MiniModeWindowService _windowService;

    // Exposes the singleton dashboard VM so XAML can bind to Providers,
    // AutoRefreshMinutes, IsAutoRefreshRunning, ToggleAutoRefreshCommand directly.
    public ProvidersDashboardViewModel Dashboard { get; }

    private double _opacity = 0.95;
    public double Opacity
    {
        get => _opacity;
        set
        {
            if (SetProperty(ref _opacity, Math.Clamp(value, 0.3, 1.0)))
            {
                OnPropertyChanged(nameof(OpacityPercent));
                _windowService.SetOpacity(_opacity);
            }
        }
    }

    public string OpacityPercent => $"{(int)(_opacity * 100)}%";

    private bool _isAlwaysOnTop = true;
    public bool IsAlwaysOnTop
    {
        get => _isAlwaysOnTop;
        set
        {
            if (SetProperty(ref _isAlwaysOnTop, value))
                _windowService.SetAlwaysOnTop(value);
        }
    }

    public MiniModeViewModel(ProvidersDashboardViewModel dashboard, MiniModeWindowService windowService)
    {
        Dashboard = dashboard;
        _windowService = windowService;
    }
}
