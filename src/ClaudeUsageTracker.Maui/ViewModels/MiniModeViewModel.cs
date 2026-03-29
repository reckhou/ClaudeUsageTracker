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

    private double _dpiScale = 0.5;
    public double DpiScale
    {
        get => _dpiScale;
        set
        {
            // Snap to nearest 10%
            var snapped = Math.Clamp(Math.Round(value / 0.1) * 0.1, 0.4, 2.0);
            if (SetProperty(ref _dpiScale, snapped))
            {
                OnPropertyChanged(nameof(DpiScalePercent));
                _windowService.SetDpiScale(snapped);
                _windowService.ResizeForProviderCount(Dashboard.Providers.Count);
            }
        }
    }

    public string DpiScalePercent => $"{(int)(_dpiScale * 100)}%";

    public MiniModeViewModel(ProvidersDashboardViewModel dashboard, MiniModeWindowService windowService)
    {
        Dashboard = dashboard;
        _windowService = windowService;
    }

    /// <summary>
    /// Silently syncs the DPI slider to the auto-detected OS value on first mini window open.
    /// Skipped if the user has already moved the slider away from the 1.0 default.
    /// </summary>
    public void InitializeDpiScale(double detectedScale)
    {
        if (Math.Abs(_dpiScale - 1.0) < 0.01)
        {
            var snapped = Math.Clamp(Math.Round(detectedScale / 0.1) * 0.1, 0.6, 2.0);
            SetProperty(ref _dpiScale, snapped);
            OnPropertyChanged(nameof(DpiScalePercent));
            _windowService.SetDpiScale(_dpiScale);
        }
    }
}
