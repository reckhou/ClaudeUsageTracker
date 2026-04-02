using System.Text.Json;

namespace ClaudeUsageTracker.Core.Models;

/// <summary>
/// Represents a Google AI Studio project with its display name and Cloud project ID.
/// </summary>
public class GoogleAiProject
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>Display name if available, otherwise the raw project ID.</summary>
    public string DisplayName => string.IsNullOrEmpty(Name) ? Id : Name;

    /// <summary>Serialise a list of projects to JSON for SecureStorage.</summary>
    public static string ToJson(IEnumerable<GoogleAiProject> projects)
        => JsonSerializer.Serialize(projects);

    /// <summary>
    /// Deserialise from JSON. Falls back to parsing legacy comma-separated IDs.
    /// </summary>
    public static List<GoogleAiProject> FromJson(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return [];

        // New JSON format
        if (stored.TrimStart().StartsWith('['))
        {
            try
            {
                return JsonSerializer.Deserialize<List<GoogleAiProject>>(stored) ?? [];
            }
            catch { /* fall through to legacy */ }
        }

        // Legacy: comma-separated project IDs (no names)
        return stored.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => new GoogleAiProject { Id = id.Trim(), Name = "" })
            .ToList();
    }
}
