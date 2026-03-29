using System.Net.Http;
using System.Text.Json;
using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Core.Services;

namespace ClaudeUsageTracker.Maui.Services;

public class GoogleAIUsageProvider : IUsageProvider
{
    public string ProviderName => "GoogleAI";

    private static readonly HttpClient _http = new();

    public async Task<ProviderUsageRecord?> FetchAsync(string apiKey, CancellationToken ct = default)
    {
        // Google AI models list endpoint — confirms API key validity
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}");
        req.Headers.Add("x-goog-api-key", apiKey);

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var root = doc.RootElement;
        if (!root.TryGetProperty("models", out var models)) return null;
        if (models.GetArrayLength() == 0) return null;

        // Google AI quota is daily. Map interval=daily reset for display purposes.
        return new ProviderUsageRecord
        {
            Provider = ProviderName,
            IntervalUtilization = 0,
            IntervalUsed = 0,
            IntervalTotal = 0,
            IntervalResetsAt = DateTime.UtcNow.Date.AddDays(1),
            WeeklyUtilization = 0,
            WeeklyUsed = 0,
            WeeklyTotal = 0,
            WeeklyResetsAt = DateTime.UtcNow.Date.AddDays(7 - (int)DateTime.UtcNow.DayOfWeek),
            FetchedAt = DateTime.UtcNow
        };
    }
}
