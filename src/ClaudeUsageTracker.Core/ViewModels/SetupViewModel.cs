using System.Collections.ObjectModel;
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

    // Google AI Studio section
    [ObservableProperty] private bool _isGoogleAiConnected;
    [ObservableProperty] private string _googleAiProjectId = "";
    [ObservableProperty] private ObservableCollection<string> _googleAiProjects = [];

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

        var googleProjects = await storage.GetAsync("google_ai_projects");
        GoogleAiProjects.Clear();
        if (!string.IsNullOrEmpty(googleProjects))
            foreach (var id in googleProjects.Split(',', StringSplitOptions.RemoveEmptyEntries))
                GoogleAiProjects.Add(id);
        IsGoogleAiConnected = GoogleAiProjects.Count > 0;
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
    public async Task AddGoogleAiProjectAsync()
    {
        var id = GoogleAiProjectId.Trim();
        if (string.IsNullOrEmpty(id) || GoogleAiProjects.Contains(id)) return;
        GoogleAiProjects.Add(id);
        GoogleAiProjectId = "";
        await SaveGoogleAiProjectsAsync();
        IsGoogleAiConnected = true;
    }

    [RelayCommand]
    public async Task RemoveGoogleAiProjectAsync(string projectId)
    {
        GoogleAiProjects.Remove(projectId);
        await SaveGoogleAiProjectsAsync();
        IsGoogleAiConnected = GoogleAiProjects.Count > 0;
        if (!IsGoogleAiConnected)
            await usageData.DeleteGoogleAiRecordsAsync(projectId);
    }

    [RelayCommand]
    public async Task DisconnectGoogleAiAsync()
    {
        foreach (var id in GoogleAiProjects.ToList())
            await usageData.DeleteGoogleAiRecordsAsync(id);
        GoogleAiProjects.Clear();
        await storage.RemoveAsync("google_ai_projects");
        IsGoogleAiConnected = false;
    }

    private async Task SaveGoogleAiProjectsAsync()
    {
        await storage.SetAsync("google_ai_projects", string.Join(",", GoogleAiProjects));
    }

    [RelayCommand]
    public void GoToDashboard() => NavigateToDashboard?.Invoke();
}
