using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Core.Services;
using ClaudeUsageTracker.Maui.Views;

namespace ClaudeUsageTracker.Maui.Services;

public class ClaudeProUsageProvider : IUsageProvider
{
    public string ProviderName => "Claude";

    public async Task<ProviderUsageRecord?> FetchAsync(string apiKey, CancellationToken ct = default)
    {
        var page = ProvidersDashboardPage.Current;
        var record = page != null
            ? await page.FetchClaudeQuotaAsync()
            : null;

        if (record == null) return null;

        return new ProviderUsageRecord
        {
            Provider = ProviderName,
            IntervalUtilization = record.FiveHourUtilization,
            IntervalUsed = 0,
            IntervalTotal = 0,
            IntervalResetsAt = record.FiveHourResetsAt == default ? DateTime.MinValue : record.FiveHourResetsAt,
            WeeklyUtilization = record.SevenDayUtilization,
            WeeklyUsed = 0,
            WeeklyTotal = 0,
            WeeklyResetsAt = record.SevenDayResetsAt == default ? DateTime.MinValue : record.SevenDayResetsAt,
            FetchedAt = record.FetchedAt
        };
    }
}
