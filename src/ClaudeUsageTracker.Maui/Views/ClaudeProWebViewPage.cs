using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Maui.Views;

public class ClaudeProWebViewPage : ContentPage
{
    private readonly WebView _webView;
    private readonly Label _statusLabel;
    private readonly TaskCompletionSource<QuotaRecord?> _tcs = new();
    private readonly bool _silent;
    private bool _extracted;

    public ClaudeProWebViewPage(bool silent = false)
    {
        _silent = silent;
        _webView = new WebView { Source = new UrlWebViewSource { Url = "https://claude.ai/settings/usage" } };
        _webView.Navigated += OnNavigated;

        _statusLabel = new Label
        {
            Text = "Loading claude.ai\u2026",
            Padding = new Thickness(16, 8),
            FontSize = 12
        };

        if (silent)
        {
            Content = new Grid { IsVisible = false, Children = { _webView, _statusLabel } };
        }
        else
        {
            Title = "Connect Claude Pro";
            var closeBtn = new Button { Text = "Cancel", HorizontalOptions = LayoutOptions.End };
            closeBtn.Clicked += (_, _) => _tcs.TrySetResult(null);

            var grid = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto),
                    new RowDefinition(GridLength.Auto)
                }
            };
            Grid.SetRow(_webView, 0);
            Grid.SetRow(_statusLabel, 1);
            Grid.SetRow(closeBtn, 2);
            grid.Children.Add(_webView);
            grid.Children.Add(_statusLabel);
            grid.Children.Add(closeBtn);
            Content = grid;
        }
    }

    public Task<QuotaRecord?> WaitForResultAsync(int timeoutMs = 60_000)
    {
        _ = Task.Delay(timeoutMs).ContinueWith(_ => _tcs.TrySetResult(null));
        return _tcs.Task;
    }

    private async void OnNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result != WebNavigationResult.Success) return;
        if (!e.Url.StartsWith("https://claude.ai")) return;
        if (_extracted) return;

        _statusLabel.Text = "Fetching quota data\u2026";
        await Task.Delay(1500);

        const string js = """
            (async () => {
                try {
                    const orgsResp = await fetch('/api/organizations', { credentials: 'include' });
                    if (!orgsResp.ok) return JSON.stringify({ error: 'orgs:' + orgsResp.status });
                    const orgs = await orgsResp.json();
                    const uuid = orgs[0]?.uuid;
                    if (!uuid) return JSON.stringify({ error: 'no uuid' });
                    const usageResp = await fetch(`/api/organizations/${uuid}/usage`, { credentials: 'include' });
                    if (!usageResp.ok) return JSON.stringify({ error: 'usage:' + usageResp.status });
                    const data = await usageResp.json();
                    return JSON.stringify({ ok: true, data });
                } catch (ex) {
                    return JSON.stringify({ error: ex.message });
                }
            })()
            """;

        var raw = await _webView.EvaluateJavaScriptAsync(js);
        if (string.IsNullOrEmpty(raw)) { _tcs.TrySetResult(null); return; }

        var json = System.Text.RegularExpressions.Regex.Unescape(raw.Trim('"'));
        var record = ParseUsageResponse(json);

        if (record != null)
        {
            _extracted = true;
            _statusLabel.Text = $"Session: {record.FiveHourUtilization}% \u00b7 Weekly: {record.SevenDayUtilization}%";
            _tcs.TrySetResult(record);
        }
        else
        {
            _statusLabel.Text = "Could not parse response. Try logging in at claude.ai first.";
            if (_silent) _tcs.TrySetResult(null);
        }
    }

    internal static QuotaRecord? ParseUsageResponse(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out _)) return null;
            if (!root.TryGetProperty("data", out var data)) return null;

            var record = new QuotaRecord { FetchedAt = DateTime.UtcNow };

            if (data.TryGetProperty("five_hour", out var fh) && fh.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                record.FiveHourUtilization = fh.GetProperty("utilization").GetInt32();
                record.FiveHourResetsAt = fh.GetProperty("resets_at").GetDateTime().ToUniversalTime();
            }
            if (data.TryGetProperty("seven_day", out var sd) && sd.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                record.SevenDayUtilization = sd.GetProperty("utilization").GetInt32();
                record.SevenDayResetsAt = sd.GetProperty("resets_at").GetDateTime().ToUniversalTime();
            }
            if (data.TryGetProperty("extra_usage", out var eu) && eu.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                record.ExtraUsageEnabled = eu.TryGetProperty("is_enabled", out var ie) && ie.ValueKind == System.Text.Json.JsonValueKind.True;
                if (eu.TryGetProperty("utilization", out var u) && u.ValueKind == System.Text.Json.JsonValueKind.Number)
                    record.ExtraUsageUtilization = u.GetInt32();
            }
            return record;
        }
        catch { return null; }
    }
}
