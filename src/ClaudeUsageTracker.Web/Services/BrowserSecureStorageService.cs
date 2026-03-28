using ClaudeUsageTracker.Core.Services;
using Microsoft.JSInterop;

namespace ClaudeUsageTracker.Web.Services;

public class BrowserSecureStorageService(IJSRuntime js) : ISecureStorageService
{
    public async Task<string?> GetAsync(string key) =>
        await js.InvokeAsync<string?>("secureStorage.get", key);

    public async Task SetAsync(string key, string value) =>
        await js.InvokeVoidAsync("secureStorage.set", key, value);

    public async Task RemoveAsync(string key) =>
        await js.InvokeVoidAsync("secureStorage.remove", key);
}
