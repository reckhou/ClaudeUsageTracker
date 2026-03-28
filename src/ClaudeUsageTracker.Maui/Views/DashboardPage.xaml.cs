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
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.RefreshCommand.Execute(null);
    }

    private void OnDailyUsagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        CostChart.Drawable = new CostBarChartDrawable(_vm.DailyUsages);
        CostChart.Invalidate();
    }

    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//setup");
    }
}
