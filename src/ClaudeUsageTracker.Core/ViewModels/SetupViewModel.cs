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
        var (valid, error) = await api.ValidateApiKeyAsync(ApiKey);
        if (!valid)
        {
            ErrorMessage = error ?? "Unknown error";
            HasError = true;
            IsValidating = false;
            return;
        }
        HasError = false;
        try
        {
            await storage.SetAsync("admin_api_key", ApiKey);
            await db.InitAsync();
            NavigateToDashboard?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsValidating = false;
        }
    }
}
