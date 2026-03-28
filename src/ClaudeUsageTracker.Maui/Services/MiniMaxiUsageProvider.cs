using System.Net.Http;
using System.Text.Json;
using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Core.Services;

namespace ClaudeUsageTracker.Maui.Services;

public class MiniMaxiUsageProvider : IUsageProvider
{
    public string ProviderName => "MiniMaxi";

    private static readonly HttpClient _http = new();

    public async Task<ProviderUsageRecord?> FetchAsync(string apiKey, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get,
            "https://www.minimaxi.com/v1/api/openplatform/coding_plan/remains");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;

        using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream);

        var root = doc.RootElement;
        if (!root.TryGetProperty("base_resp", out var baseResp)) return null;
        var statusCode = baseResp.GetProperty("status_code").GetInt32();
        if (statusCode != 0) return null;

        var modelRemains = root.GetProperty("model_remains");

        // Use MiniMax-M* (primary coding model) if available; fall back to first model with quota
        int? intervalUtil = null, weeklyUtil = null;
        long intervalUsed = 0, intervalTotal = 0, weeklyUsed = 0, weeklyTotal = 0;
        DateTime? intervalReset = null, weeklyReset = null;

        foreach (var model in modelRemains.EnumerateArray())
        {
            var it = model.GetProperty("current_interval_total_count").GetInt64();
            if (it <= 0) continue;

            var name = model.GetProperty("model_name").GetString();
            var iu = model.GetProperty("current_interval_usage_count").GetInt64();
            var wu = model.GetProperty("current_weekly_usage_count").GetInt64();
            var wt = model.GetProperty("current_weekly_total_count").GetInt64();

            // Prefer MiniMax-M*, otherwise use first model
            if (name == "MiniMax-M*" || intervalUtil == null)
            {
                intervalUtil = model.TryGetProperty("utilization", out var u) && u.ValueKind == JsonValueKind.Number
                    ? u.GetInt32() : (int)(iu * 100 / it);
                weeklyUtil = wt > 0
                    ? (model.TryGetProperty("utilization", out var wu2) && wu2.ValueKind == JsonValueKind.Number
                        ? wu2.GetInt32() : (int)(wu * 100 / wt))
                    : 0;
                intervalUsed = iu;
                intervalTotal = it;
                weeklyUsed = wu;
                weeklyTotal = wt;
                intervalReset = UnixMsToDateTime(model.GetProperty("end_time").GetInt64());
                weeklyReset = UnixMsToDateTime(model.GetProperty("weekly_end_time").GetInt64());

                if (name == "MiniMax-M*") break; // Found preferred model
            }
        }

        if (intervalTotal == 0) return null;

        return new ProviderUsageRecord
        {
            Provider = ProviderName,
            IntervalUtilization = intervalUtil ?? 0,
            IntervalUsed = intervalUsed,
            IntervalTotal = intervalTotal,
            IntervalResetsAt = intervalReset ?? DateTime.UtcNow.AddHours(5),
            WeeklyUtilization = weeklyUtil ?? 0,
            WeeklyUsed = weeklyUsed,
            WeeklyTotal = weeklyTotal,
            WeeklyResetsAt = weeklyReset ?? DateTime.UtcNow.AddDays(7),
            FetchedAt = DateTime.UtcNow
        };
    }

    private static DateTime UnixMsToDateTime(long ms) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
}
