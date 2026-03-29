using ClaudeUsageTracker.Core.ViewModels;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ClaudeUsageTracker.Maui.Views;

public partial class DashboardPage : ContentPage
{
    private readonly DashboardViewModel _vm;

    public DashboardPage(DashboardViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
        vm.DailyUsages.CollectionChanged += OnDailyUsagesChanged;
        vm.TokenChartData.CollectionChanged += OnTokenChartDataChanged;
        vm.PropertyChanged += OnVmPropertyChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.RefreshCommand.Execute(null);
        ProviderPicker.SelectedIndex = (int)_vm.SelectedProvider;
    }

    private void OnDailyUsagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        CostChart.Drawable = new CostBarChartDrawable(_vm.DailyUsages, _vm.CostUnavailableMessage);
        CostChart.Invalidate();
    }

    private void OnTokenChartDataChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        TokenChart.Drawable = new TokenBarChartDrawable(
            _vm.TokenChartData.ToList(),
            _vm.TimeRangeLabel,
            _vm.TokenUnavailableMessage);
        TokenChart.Invalidate();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DashboardViewModel.CostUnavailableMessage))
        {
            CostChart.Drawable = new CostBarChartDrawable(_vm.DailyUsages, _vm.CostUnavailableMessage);
            CostChart.Invalidate();
        }
        else if (e.PropertyName is nameof(DashboardViewModel.TokenUnavailableMessage))
        {
            TokenChart.Drawable = new TokenBarChartDrawable(
                _vm.TokenChartData.ToList(), _vm.TimeRangeLabel, _vm.TokenUnavailableMessage);
            TokenChart.Invalidate();
        }
    }

    private void OnProviderChanged(object sender, EventArgs e)
    {
        if (ProviderPicker.SelectedIndex < 0) return;
        _vm.SelectedProvider = (ProviderFilter)ProviderPicker.SelectedIndex;
    }

    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//setup");
    }
}
