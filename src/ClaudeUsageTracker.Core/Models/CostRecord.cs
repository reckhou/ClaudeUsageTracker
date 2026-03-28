using SQLite;

namespace ClaudeUsageTracker.Core.Models;

[Table("CostRecords")]
public class CostRecord
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public DateTime BucketStart { get; set; }
    public string Description { get; set; } = "";
    public decimal CostUsd { get; set; }
    public DateTime FetchedAt { get; set; }
}
