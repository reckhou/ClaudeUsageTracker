using ClaudeUsageTracker.Core.Models;
using SQLite;

namespace ClaudeUsageTracker.Core.Services;

public class UsageDataService(string dbPath) : IUsageDataService
{
    private SQLiteAsyncConnection? _db;

    public async Task InitAsync()
    {
        if (_db != null) return;
        _db = new SQLiteAsyncConnection(dbPath);
        await _db.CreateTableAsync<UsageRecord>();
        await _db.CreateTableAsync<CostRecord>();
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
        return DateTime.Parse(result);
    }

    private void EnsureInit()
    {
        if (_db == null)
            throw new InvalidOperationException("Call InitAsync before using the data service.");
    }
}
