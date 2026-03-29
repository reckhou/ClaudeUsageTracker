using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeUsageTracker.Core.Services;

namespace ClaudeUsageTracker.Core.ViewModels;

public partial class SetupViewModel(
    ISecureStorageService storage,
    IUsageDataService usageData) : ObservableObject
{
    // Claude Pro section
    [ObservableProperty] private bool _isClaudeProConnected;
    [ObservableProperty] private string _claudeProStatus = "Not connected";

    // MiniMaxi section
    [ObservableProperty] private bool _isMiniMaxiConnected;
    [ObservableProperty] private string _miniMaxiApiKey = "";
    [ObservableProperty] private bool _isValidatingMiniMaxi;

    public event Action? NavigateToDashboard;

    public async Task LoadAsync()
    {
        // Use the build-specific SQLite database as the source of truth for Claude Pro
        // connection status, not SecureStorage (which persists across debug/release builds).
        await usageData.InitAsync();
        IsClaudeProConnected = await usageData.HasAnyQuotaRecordAsync();
        ClaudeProStatus = IsClaudeProConnected ? "Connected" : "Not connected";

        var miniKey = await storage.GetAsync("MiniMaxiApiKey");
        IsMiniMaxiConnected = !string.IsNullOrEmpty(miniKey);
        MiniMaxiApiKey = IsMiniMaxiConnected ? "••••••••••" : "";
    }

    [RelayCommand]
    public async Task DisconnectClaudeProAsync()
    {
        await storage.RemoveAsync("claude_pro_connected");
        IsClaudeProConnected = false;
        ClaudeProStatus = "Not connected";
    }

    [RelayCommand]
    public async Task SaveMiniMaxiApiKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(MiniMaxiApiKey) || MiniMaxiApiKey.StartsWith("•")) return;
        IsValidatingMiniMaxi = true;
        await storage.SetAsync("MiniMaxiApiKey", MiniMaxiApiKey);
        IsMiniMaxiConnected = true;
        MiniMaxiApiKey = "••••••••••";
        IsValidatingMiniMaxi = false;
    }

    [RelayCommand]
    public async Task DisconnectMiniMaxiAsync()
    {
        await storage.RemoveAsync("MiniMaxiApiKey");
        IsMiniMaxiConnected = false;
        MiniMaxiApiKey = "";
    }

    [RelayCommand]
    public void GoToDashboard() => NavigateToDashboard?.Invoke();
}
