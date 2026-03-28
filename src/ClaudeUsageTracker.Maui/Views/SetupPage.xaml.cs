using ClaudeUsageTracker.Core.Services;
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

    private async void OnConnectClaudeProClicked(object sender, EventArgs e)
    {
        var service = Handler?.MauiContext?.Services.GetService<IClaudeAiUsageService>();
        if (service == null) return;
        var record = await service.ConnectAndFetchAsync();
        if (record != null)
        {
            var storage = Handler?.MauiContext?.Services.GetService<ISecureStorageService>();
            if (storage != null) await storage.SetAsync("claude_pro_connected", "true");
            var db = Handler?.MauiContext?.Services.GetService<IUsageDataService>();
            if (db != null) { await db.InitAsync(); await db.UpsertQuotaRecordAsync(record); }
            _vm.IsClaudeProConnected = true;
            _vm.ClaudeProStatus = $"Connected \u00b7 Session {record.FiveHourUtilization}% \u00b7 Weekly {record.SevenDayUtilization}%";
        }
    }
}
