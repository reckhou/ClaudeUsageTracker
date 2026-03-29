using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Core.Services;

public interface IUsageDataService
{
    Task InitAsync();
    Task UpsertQuotaRecordAsync(QuotaRecord record);
    Task<QuotaRecord?> GetLatestQuotaAsync();
    Task UpsertProviderRecordAsync(ProviderUsageRecord record);
    Task<List<ProviderUsageRecord>> GetAllProviderRecordsAsync();
}
