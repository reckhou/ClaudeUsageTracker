using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Core.Services;

public interface IUsageDataService
{
    Task InitAsync();
    Task UpsertQuotaRecordAsync(QuotaRecord record);
    Task<QuotaRecord?> GetLatestQuotaAsync();
    Task UpsertProviderRecordAsync(ProviderUsageRecord record);
    Task<List<ProviderUsageRecord>> GetAllProviderRecordsAsync();
    /// <summary>Returns true if at least one QuotaRecord exists in this build's database.</summary>
    Task<bool> HasAnyQuotaRecordAsync();

    Task UpsertGoogleAiRecordsAsync(string projectId, string timeRange, List<GoogleAiUsageRecord> records);
    Task<List<GoogleAiUsageRecord>> GetGoogleAiRecordsAsync(string? projectId = null, string? timeRange = null);
    Task DeleteGoogleAiRecordsAsync(string projectId);
}
