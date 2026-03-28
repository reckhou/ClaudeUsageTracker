using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Core.Services;

public class AnthropicApiService(HttpClient http)
{
    private const string BaseUrl = "https://api.anthropic.com";
    private const string ApiVersion = "2023-06-01";

    public void SetApiKey(string adminApiKey)
    {
        http.DefaultRequestHeaders.Remove("x-api-key");
        http.DefaultRequestHeaders.Add("x-api-key", adminApiKey);
        http.DefaultRequestHeaders.Remove("anthropic-version");
        http.DefaultRequestHeaders.Add("anthropic-version", ApiVersion);
    }

    public async Task<List<UsageRecord>> FetchUsageAsync(DateTime from, DateTime to)
    {
        var records = new List<UsageRecord>();
        var now = DateTime.UtcNow;
        string? nextPage = null;

        do
        {
            var url = $"{BaseUrl}/v1/organizations/usage_report/messages" +
                      $"?starting_at={from:yyyy-MM-ddTHH:mm:ssZ}&ending_at={to:yyyy-MM-ddTHH:mm:ssZ}" +
                      $"&bucket_width=1d&group_by[]=model";
            if (nextPage != null)
                url += $"&page={Uri.EscapeDataString(nextPage)}";

            var response = await http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var page = JsonSerializer.Deserialize<UsageReportPage>(json, JsonOptions);
            if (page?.Data == null) break;

            foreach (var bucket in page.Data)
            {
                foreach (var item in bucket.Results ?? [])
                {
                    records.Add(new UsageRecord
                    {
                        BucketStart = bucket.StartingAt,
                        Model = item.Model ?? "",
                        InputTokens = item.UncachedInputTokens,
                        OutputTokens = item.OutputTokens,
                        CacheReadTokens = item.CacheReadInputTokens,
                        CacheCreationTokens = (item.CacheCreation?.Ephemeral1hInputTokens ?? 0) +
                                              (item.CacheCreation?.Ephemeral5mInputTokens ?? 0),
                        FetchedAt = now
                    });
                }
            }

            nextPage = page.HasMore ? page.NextPage : null;
        } while (nextPage != null);

        return records;
    }

    public async Task<List<CostRecord>> FetchCostsAsync(DateTime from, DateTime to)
    {
        var records = new List<CostRecord>();
        var now = DateTime.UtcNow;
        string? nextPage = null;

        do
        {
            var url = $"{BaseUrl}/v1/organizations/cost_report" +
                      $"?starting_at={from:yyyy-MM-ddTHH:mm:ssZ}&ending_at={to:yyyy-MM-ddTHH:mm:ssZ}" +
                      $"&bucket_width=1d";
            if (nextPage != null)
                url += $"&page={Uri.EscapeDataString(nextPage)}";

            var response = await http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var page = JsonSerializer.Deserialize<CostReportPage>(json, JsonOptions);
            if (page?.Data == null) break;

            foreach (var bucket in page.Data)
            {
                foreach (var item in bucket.Results ?? [])
                {
                    records.Add(new CostRecord
                    {
                        BucketStart = bucket.StartingAt,
                        Description = item.Description ?? "",
                        CostUsd = decimal.TryParse(item.Amount, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var amt) ? amt / 100m : 0m,
                        FetchedAt = now
                    });
                }
            }

            nextPage = page.HasMore ? page.NextPage : null;
        } while (nextPage != null);

        return records;
    }

    public async Task<(bool Success, string? Error)> ValidateApiKeyAsync(string key)
    {
        SetApiKey(key);
        try
        {
            var yesterday = DateTime.UtcNow.Date.AddDays(-1);
            var today = DateTime.UtcNow.Date;
            var url = $"{BaseUrl}/v1/organizations/usage_report/messages" +
                      $"?starting_at={yesterday:yyyy-MM-ddTHH:mm:ssZ}&ending_at={today:yyyy-MM-ddTHH:mm:ssZ}" +
                      $"&bucket_width=1d";
            var response = await http.GetAsync(url);
            if (response.IsSuccessStatusCode) return (true, null);
            var body = await response.Content.ReadAsStringAsync();
            return (false, $"HTTP {(int)response.StatusCode}: {body}");
        }
        catch (Exception ex)
        {
            return (false, $"Network error: {ex.Message}");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private class UsageReportPage
    {
        public List<UsageBucket>? Data { get; set; }
        public bool HasMore { get; set; }
        public string? NextPage { get; set; }
    }

    private class UsageBucket
    {
        public DateTime StartingAt { get; set; }
        public DateTime EndingAt { get; set; }
        public List<UsageResult>? Results { get; set; }
    }

    private class UsageResult
    {
        public string? Model { get; set; }
        public long UncachedInputTokens { get; set; }
        public long OutputTokens { get; set; }
        public long CacheReadInputTokens { get; set; }
        public CacheCreationData? CacheCreation { get; set; }
    }

    private class CacheCreationData
    {
        [JsonPropertyName("ephemeral_1h_input_tokens")]
        public long Ephemeral1hInputTokens { get; set; }
        [JsonPropertyName("ephemeral_5m_input_tokens")]
        public long Ephemeral5mInputTokens { get; set; }
    }

    private class CostReportPage
    {
        public List<CostBucket>? Data { get; set; }
        public bool HasMore { get; set; }
        public string? NextPage { get; set; }
    }

    private class CostBucket
    {
        public DateTime StartingAt { get; set; }
        public DateTime EndingAt { get; set; }
        public List<CostResult>? Results { get; set; }
    }

    private class CostResult
    {
        public string? Amount { get; set; }
        public string? Currency { get; set; }
        public string? Description { get; set; }
    }
}
