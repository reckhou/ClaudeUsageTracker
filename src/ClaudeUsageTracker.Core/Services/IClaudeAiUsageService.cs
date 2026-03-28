using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Core.Services;

public interface IClaudeAiUsageService
{
    /// <summary>Show WebView auth flow and fetch quota. Returns null if auth fails or cancelled.</summary>
    Task<QuotaRecord?> ConnectAndFetchAsync();

    /// <summary>Silently re-fetch quota using existing session. Returns null if session expired.</summary>
    Task<QuotaRecord?> FetchQuotaAsync();
}
