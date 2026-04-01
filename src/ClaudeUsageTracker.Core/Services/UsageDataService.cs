using ClaudeUsageTracker.Core.Models;
using SQLite;

namespace ClaudeUsageTracker.Core.Services;

public class UsageDataService(string dbPath) : IUsageDataService
{
    private SQLiteAsyncConnection? _db;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public async Task InitAsync()
    {
        if (_db != null) return;
        await _initLock.WaitAsync();
        try
        {
            if (_db != null) return;
            _db = new SQLiteAsyncConnection(dbPath);
            await _db.CreateTableAsync<QuotaRecord>();
            await _db.CreateTableAsync<ProviderUsageRecord>();
            await _db.CreateTableAsync<GoogleAiUsageRecord>();
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task UpsertQuotaRecordAsync(QuotaRecord record)
    {
        EnsureInit();
        await _db!.ExecuteAsync("DELETE FROM QuotaRecords");
        await _db.InsertAsync(record);
    }

    public async Task<QuotaRecord?> GetLatestQuotaAsync()
    {
        EnsureInit();
        return await _db!.Table<QuotaRecord>()
            .OrderByDescending(r => r.FetchedAt)
            .FirstOrDefaultAsync();
    }

    public async Task UpsertProviderRecordAsync(ProviderUsageRecord record)
    {
        EnsureInit();
        await _db!.ExecuteAsync("DELETE FROM ProviderUsageRecords WHERE Provider = ?", record.Provider);
        await _db.InsertAsync(record);
    }

    public async Task<List<ProviderUsageRecord>> GetAllProviderRecordsAsync()
    {
        EnsureInit();
        return await _db!.Table<ProviderUsageRecord>().ToListAsync();
    }

    public async Task<bool> HasAnyQuotaRecordAsync()
    {
        if (_db == null) await InitAsync();
        return await _db!.Table<QuotaRecord>().CountAsync() > 0;
    }

    public async Task UpsertGoogleAiRecordsAsync(string projectId, string timeRange, List<GoogleAiUsageRecord> records)
    {
        EnsureInit();
        await _db!.ExecuteAsync(
            "DELETE FROM GoogleAiUsageRecords WHERE ProjectId = ? AND TimeRange = ?",
            projectId, timeRange);
        foreach (var r in records)
            await _db.InsertAsync(r);
    }

    public async Task<List<GoogleAiUsageRecord>> GetGoogleAiRecordsAsync(string? projectId = null, string? timeRange = null)
    {
        EnsureInit();
        var query = _db!.Table<GoogleAiUsageRecord>();
        if (projectId != null)
            query = query.Where(r => r.ProjectId == projectId);
        if (timeRange != null)
            query = query.Where(r => r.TimeRange == timeRange);
        return await query.ToListAsync();
    }

    public async Task DeleteGoogleAiRecordsAsync(string projectId)
    {
        EnsureInit();
        await _db!.ExecuteAsync(
            "DELETE FROM GoogleAiUsageRecords WHERE ProjectId = ?",
            projectId);
    }

    private void EnsureInit()
    {
        if (_db == null)
            throw new InvalidOperationException("Call InitAsync before using the data service.");
    }
}
