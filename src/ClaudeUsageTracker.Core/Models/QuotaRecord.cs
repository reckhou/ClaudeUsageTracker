using SQLite;

namespace ClaudeUsageTracker.Core.Models;

[Table("QuotaRecords")]
public class QuotaRecord
{
    [PrimaryKey, AutoIncrement] public int Id { get; set; }
    public int FiveHourUtilization { get; set; }    // current session %, 0-100
    public DateTime FiveHourResetsAt { get; set; }
    public int SevenDayUtilization { get; set; }    // weekly %, 0-100
    public DateTime SevenDayResetsAt { get; set; }
    public bool ExtraUsageEnabled { get; set; }
    public int ExtraUsageUtilization { get; set; }  // 0 if not enabled
    public DateTime FetchedAt { get; set; }
}
