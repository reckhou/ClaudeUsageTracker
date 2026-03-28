using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Core.Services;

public interface IUsageProvider
{
    string ProviderName { get; }
    Task<ProviderUsageRecord?> FetchAsync(string apiKey, CancellationToken ct = default);
}
