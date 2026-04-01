using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ClaudeUsageTracker.Maui.Services;
using Microsoft.Maui.Storage;

namespace ClaudeUsageTracker.Maui.ViewModels;

public partial class MiniModeViewModel : ObservableObject
{
    private readonly MiniModeWindowService _windowService;
    private readonly HashSet<string> _prefsLoaded = new();

    public ProvidersDashboardViewModel Dashboard { get; }

    /// <summary>
    /// Filtered subset of Dashboard.Providers where ShowInMiniMode is true.
    /// MiniModePage binds to this instead of Dashboard.Providers directly.
    /// </summary>
    public ObservableCollection<ProviderCardViewModel> MiniProviders { get; } = new();

    private const string OpacityPrefKey    = "mini_opacity";
    private const string AlwaysOnTopPrefKey = "mini_always_on_top";

    private double _opacity;
    public double Opacity
    {
        get => _opacity;
        set
        {
            if (SetProperty(ref _opacity, Math.Clamp(value, 0.3, 1.0)))
            {
                OnPropertyChanged(nameof(OpacityPercent));
                Preferences.Set(OpacityPrefKey, _opacity);
                _windowService.SetOpacity(_opacity);
            }
        }
    }

    public string OpacityPercent => $"{(int)(_opacity * 100)}%";

    private bool _isAlwaysOnTop;
    public bool IsAlwaysOnTop
    {
        get => _isAlwaysOnTop;
        set
        {
            if (SetProperty(ref _isAlwaysOnTop, value))
            {
                Preferences.Set(AlwaysOnTopPrefKey, value);
                _windowService.SetAlwaysOnTop(value);
            }
        }
    }

    public MiniModeViewModel(ProvidersDashboardViewModel dashboard, MiniModeWindowService windowService)
    {
        Dashboard = dashboard;
        _windowService = windowService;
        _opacity      = Preferences.Get(OpacityPrefKey,    0.95);
        _isAlwaysOnTop = Preferences.Get(AlwaysOnTopPrefKey, true);
        Dashboard.Providers.CollectionChanged += OnSourceProvidersChanged;
        foreach (var p in Dashboard.Providers)
            p.PropertyChanged += OnProviderPropertyChanged;
        Dashboard.GoogleAiCard.PropertyChanged += OnGoogleAiCardPropertyChanged;
        RebuildMiniProviders();
    }

    private void OnGoogleAiCardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GoogleAiCardViewModel.ShowInMiniMode))
            _windowService.ResizeForProviderCount(MiniProviders.Count, Dashboard.GoogleAiCard.ShowInMiniMode);
    }

    private void OnSourceProvidersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
            foreach (ProviderCardViewModel p in e.OldItems)
                p.PropertyChanged -= OnProviderPropertyChanged;

        if (e.NewItems != null)
            foreach (ProviderCardViewModel p in e.NewItems)
                p.PropertyChanged += OnProviderPropertyChanged;

        if (e.Action == NotifyCollectionChangedAction.Reset)
            foreach (var p in Dashboard.Providers)
                p.PropertyChanged += OnProviderPropertyChanged;

        RebuildMiniProviders();
    }

    private void OnProviderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ProviderCardViewModel provider) return;

        if (e.PropertyName == nameof(ProviderCardViewModel.ProviderName)
            && !string.IsNullOrEmpty(provider.ProviderName)
            && _prefsLoaded.Add(provider.ProviderName))
        {
            // First time this provider's name is known — load saved visibility.
            // Temporarily unsub to avoid re-entrancy while setting ShowInMiniMode.
            provider.PropertyChanged -= OnProviderPropertyChanged;
            provider.ShowInMiniMode = Preferences.Get(PrefKey(provider.ProviderName), true);
            provider.PropertyChanged += OnProviderPropertyChanged;
            RebuildMiniProviders();
            _windowService.ResizeForProviderCount(MiniProviders.Count, Dashboard.GoogleAiCard.ShowInMiniMode);
            return;
        }

        if (e.PropertyName == nameof(ProviderCardViewModel.ShowInMiniMode))
        {
            if (!string.IsNullOrEmpty(provider.ProviderName))
                Preferences.Set(PrefKey(provider.ProviderName), provider.ShowInMiniMode);

            RebuildMiniProviders();
            _windowService.ResizeForProviderCount(MiniProviders.Count, Dashboard.GoogleAiCard.ShowInMiniMode);
        }
    }

    private void RebuildMiniProviders()
    {
        MiniProviders.Clear();
        foreach (var p in Dashboard.Providers)
        {
            // Load persisted preference the first time we see a named provider.
            // Handles the case where providers were named before this VM was created.
            if (!string.IsNullOrEmpty(p.ProviderName) && _prefsLoaded.Add(p.ProviderName))
            {
                p.PropertyChanged -= OnProviderPropertyChanged;
                p.ShowInMiniMode = Preferences.Get(PrefKey(p.ProviderName), true);
                p.PropertyChanged += OnProviderPropertyChanged;
            }

            if (p.ShowInMiniMode)
                MiniProviders.Add(p);
        }
    }

    private static string PrefKey(string providerName) => $"mini_visible_{providerName}";
}
