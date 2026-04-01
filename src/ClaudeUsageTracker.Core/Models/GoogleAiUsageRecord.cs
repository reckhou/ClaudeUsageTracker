using SQLite;

namespace ClaudeUsageTracker.Core.Models;

[Table("GoogleAiUsageRecords")]
public class GoogleAiUsageRecord
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string ProjectId { get; set; } = "";        // Google Cloud project ID
    public string ModelName { get; set; } = "";         // e.g. "gemini-2.5-flash-lite"
    public string TimeRange { get; set; } = "";         // "last-1-day", "last-7-days", "last-28-days"
    public long RequestCount { get; set; }              // total requests in time range
    public long InputTokens { get; set; }               // total input tokens in time range
    public decimal Cost { get; set; }                   // cost in account currency (from spend page)
    public decimal SpendCapUsed { get; set; }           // current spend cap usage (e.g. 0.03)
    public decimal SpendCapLimit { get; set; }          // spend cap limit (e.g. 4.00)
    public string Currency { get; set; } = "";          // "£", "$", etc.
    public DateTime FetchedAt { get; set; }             // when this snapshot was taken (UTC)
}
