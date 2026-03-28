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
                      $"?starting_at={from:yyyy-MM-dd}&ending_at={to:yyyy-MM-dd}" +
                      $"&bucket_width=1d&group_by[]=model";
            if (nextPage != null)
                url += $"&page_token={Uri.EscapeDataString(nextPage)}";

            var response = await http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var page = JsonSerializer.Deserialize<UsageReportPage>(json, JsonOptions);
            if (page?.Data == null) break;

            foreach (var item in page.Data)
            {
                records.Add(new UsageRecord
                {
                    BucketStart = item.BucketStart,
                    Model = item.Model ?? "",
                    InputTokens = item.InputTokens,
                    OutputTokens = item.OutputTokens,
                    CacheReadTokens = item.CacheReadInputTokens,
                    CacheCreationTokens = item.CacheCreationInputTokens,
                    FetchedAt = now
                });
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
                      $"?starting_at={from:yyyy-MM-dd}&ending_at={to:yyyy-MM-dd}" +
                      $"&bucket_width=1d";
            if (nextPage != null)
                url += $"&page_token={Uri.EscapeDataString(nextPage)}";

            var response = await http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var page = JsonSerializer.Deserialize<CostReportPage>(json, JsonOptions);
            if (page?.Data == null) break;

            foreach (var item in page.Data)
            {
                records.Add(new CostRecord
                {
                    BucketStart = item.BucketStart,
                    Description = item.Description ?? "",
                    CostUsd = item.Amount,
                    FetchedAt = now
                });
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
            var url = $"{BaseUrl}/v1/organizations/usage_report/messages" +
                      $"?starting_at={yesterday:yyyy-MM-dd}&ending_at={DateTime.UtcNow.Date:yyyy-MM-dd}" +
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
        public List<UsageReportItem>? Data { get; set; }
        public bool HasMore { get; set; }
        public string? NextPage { get; set; }
    }

    private class UsageReportItem
    {
        public DateTime BucketStart { get; set; }
        public string? Model { get; set; }
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        public long CacheReadInputTokens { get; set; }
        public long CacheCreationInputTokens { get; set; }
    }

    private class CostReportPage
    {
        public List<CostReportItem>? Data { get; set; }
        public bool HasMore { get; set; }
        public string? NextPage { get; set; }
    }

    private class CostReportItem
    {
        public DateTime BucketStart { get; set; }
        public string? Description { get; set; }
        public decimal Amount { get; set; }
    }
}
