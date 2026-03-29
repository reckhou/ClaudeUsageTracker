using ClaudeUsageTracker.Core.ViewModels;
using System.Collections.Specialized;

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
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.RefreshCommand.Execute(null);
        ProviderPicker.SelectedIndex = 0; // Default to Anthropic
    }

    private void OnDailyUsagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        CostChart.Drawable = new CostBarChartDrawable(_vm.DailyUsages);
        CostChart.Invalidate();
    }

    private void OnTokenChartDataChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        TokenChart.Drawable = new TokenBarChartDrawable(
            _vm.TokenChartData.ToList(),
            _vm.TimeRangeLabel);
        TokenChart.Invalidate();
    }

    private void OnProviderChanged(object sender, EventArgs e)
    {
        if (ProviderPicker.SelectedIndex < 0) return;
        _vm.SelectedProvider = (ProviderFilter)ProviderPicker.SelectedIndex;
        _ = _vm.LoadTokenChartDataAsync();
    }

    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//setup");
    }
}
