using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeUsageTracker.Core.Services;

namespace ClaudeUsageTracker.Core.ViewModels;

public partial class SetupViewModel(
    ISecureStorageService storage,
    AnthropicApiService api,
    IUsageDataService db) : ObservableObject
{
    // Admin API section
    [ObservableProperty] private string _adminApiKey = "";
    [ObservableProperty] private bool _isValidatingApi;
    [ObservableProperty] private string _apiError = "";
    [ObservableProperty] private bool _hasApiError;
    [ObservableProperty] private bool _isApiConnected;

    // Claude Pro section
    [ObservableProperty] private bool _isClaudeProConnected;
    [ObservableProperty] private string _claudeProStatus = "Not connected";

    public event Action? NavigateToDashboard;

    public async Task LoadAsync()
    {
        var key = await storage.GetAsync("admin_api_key");
        IsApiConnected = !string.IsNullOrEmpty(key);
        AdminApiKey = IsApiConnected ? "••••••••••••" : "";

        var proConnected = await storage.GetAsync("claude_pro_connected");
        IsClaudeProConnected = proConnected == "true";
        ClaudeProStatus = IsClaudeProConnected ? "Connected" : "Not connected";
    }

    [RelayCommand]
    public async Task SaveApiKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(AdminApiKey) || AdminApiKey.StartsWith("•")) return;
        IsValidatingApi = true; ApiError = ""; HasApiError = false;
        var (valid, error) = await api.ValidateApiKeyAsync(AdminApiKey);
        if (!valid) { ApiError = error ?? "Unknown error"; HasApiError = true; IsValidatingApi = false; return; }
        await storage.SetAsync("admin_api_key", AdminApiKey);
        await db.InitAsync();
        IsApiConnected = true;
        IsValidatingApi = false;
    }

    [RelayCommand]
    public async Task DisconnectApiAsync()
    {
        await storage.RemoveAsync("admin_api_key");
        IsApiConnected = false;
        AdminApiKey = "";
    }

    [RelayCommand]
    public async Task DisconnectClaudeProAsync()
    {
        await storage.RemoveAsync("claude_pro_connected");
        IsClaudeProConnected = false;
        ClaudeProStatus = "Not connected";
    }

    [RelayCommand]
    public void GoToDashboard() => NavigateToDashboard?.Invoke();
}
