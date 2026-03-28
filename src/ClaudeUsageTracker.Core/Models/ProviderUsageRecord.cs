using SQLite;

namespace ClaudeUsageTracker.Core.Models;

[Table("ProviderUsageRecords")]
public class ProviderUsageRecord
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public string Provider { get; set; } = "";           // "ClaudePro", "MiniMaxi", etc.
    public int IntervalUtilization { get; set; }         // 0-100, current interval %
    public long IntervalUsed { get; set; }                // absolute units used
    public long IntervalTotal { get; set; }               // absolute units total
    public DateTime IntervalResetsAt { get; set; }        // when interval resets (UTC)
    public int WeeklyUtilization { get; set; }            // 0-100, weekly %
    public long WeeklyUsed { get; set; }
    public long WeeklyTotal { get; set; }
    public DateTime WeeklyResetsAt { get; set; }
    public DateTime FetchedAt { get; set; }               // when this record was fetched
}
