using CommunityToolkit.Mvvm.ComponentModel;
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
}
