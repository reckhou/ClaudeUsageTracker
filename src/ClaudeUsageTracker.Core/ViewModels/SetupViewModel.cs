using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeUsageTracker.Core.Services;

namespace ClaudeUsageTracker.Core.ViewModels;

public partial class SetupViewModel(
    ISecureStorageService storage,
    AnthropicApiService api,
    IUsageDataService db) : ObservableObject
{
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private bool _isValidating;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _hasError;

    public event Action? NavigateToDashboard;

    [RelayCommand]
    public async Task SaveAsync()
    {
        IsValidating = true;
        ErrorMessage = "";
        bool valid = await api.ValidateApiKeyAsync(ApiKey);
        if (!valid)
        {
            ErrorMessage = "Invalid Admin API key. Make sure it starts with sk-ant-admin.";
            HasError = true;
            IsValidating = false;
            return;
        }
        HasError = false;
        await storage.SetAsync("admin_api_key", ApiKey);
        await db.InitAsync();
        IsValidating = false;
        NavigateToDashboard?.Invoke();
    }
}
