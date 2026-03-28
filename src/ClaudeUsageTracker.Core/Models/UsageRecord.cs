using SQLite;

namespace ClaudeUsageTracker.Core.Models;

[Table("UsageRecords")]
public class UsageRecord
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public DateTime BucketStart { get; set; }
    public string Model { get; set; } = "";
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheCreationTokens { get; set; }
    public DateTime FetchedAt { get; set; }
}
