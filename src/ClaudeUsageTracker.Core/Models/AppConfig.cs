namespace ClaudeUsageTracker.Core.Models;

public class AppConfig
{
    public string AdminApiKey { get; set; } = "";
    public bool IsConfigured => !string.IsNullOrEmpty(AdminApiKey);
}
