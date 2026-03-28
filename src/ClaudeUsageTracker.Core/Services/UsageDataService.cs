using System.Globalization;
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
            await _db.CreateTableAsync<UsageRecord>();
            await _db.CreateTableAsync<CostRecord>();
            await _db.CreateTableAsync<QuotaRecord>();
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task UpsertUsageRecordsAsync(IEnumerable<UsageRecord> records)
    {
        EnsureInit();
        foreach (var record in records)
        {
            await _db!.ExecuteAsync(
                "DELETE FROM UsageRecords WHERE BucketStart = ? AND Model = ?",
                record.BucketStart, record.Model);
            await _db.InsertAsync(record);
        }
    }

    public async Task UpsertCostRecordsAsync(IEnumerable<CostRecord> records)
    {
        EnsureInit();
        foreach (var record in records)
        {
            await _db!.ExecuteAsync(
                "DELETE FROM CostRecords WHERE BucketStart = ? AND Description = ?",
                record.BucketStart, record.Description);
            await _db.InsertAsync(record);
        }
    }

    public async Task<List<UsageRecord>> GetUsageAsync(DateTime from, DateTime to)
    {
        EnsureInit();
        return await _db!.Table<UsageRecord>()
            .Where(r => r.BucketStart >= from && r.BucketStart <= to)
            .ToListAsync();
    }

    public async Task<List<CostRecord>> GetCostsAsync(DateTime from, DateTime to)
    {
        EnsureInit();
        return await _db!.Table<CostRecord>()
            .Where(r => r.BucketStart >= from && r.BucketStart <= to)
            .ToListAsync();
    }

    public async Task<DateTime?> GetLastFetchedAtAsync()
    {
        EnsureInit();
        var result = await _db!.ExecuteScalarAsync<string>(
            "SELECT MAX(FetchedAt) FROM UsageRecords");
        if (string.IsNullOrEmpty(result)) return null;
        if (long.TryParse(result, out var ticks))
            return new DateTime(ticks, DateTimeKind.Utc);
        if (DateTime.TryParse(result, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var dt))
            return dt;
        return null;
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

    private void EnsureInit()
    {
        if (_db == null)
            throw new InvalidOperationException("Call InitAsync before using the data service.");
    }
}
