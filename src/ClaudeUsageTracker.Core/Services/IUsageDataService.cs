using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Core.Services;

public interface IUsageDataService
{
    Task InitAsync();
    Task UpsertUsageRecordsAsync(IEnumerable<UsageRecord> records);
    Task UpsertCostRecordsAsync(IEnumerable<CostRecord> records);
    Task<List<UsageRecord>> GetUsageAsync(DateTime from, DateTime to);
    Task<List<CostRecord>> GetCostsAsync(DateTime from, DateTime to);
    Task<DateTime?> GetLastFetchedAtAsync();
    Task UpsertQuotaRecordAsync(QuotaRecord record);
    Task<QuotaRecord?> GetLatestQuotaAsync();
    Task UpsertProviderRecordAsync(ProviderUsageRecord record);
    Task<List<ProviderUsageRecord>> GetAllProviderRecordsAsync();
}
