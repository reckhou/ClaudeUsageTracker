using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Maui.Views;

/// <summary>
/// Scrapes Google AI Studio usage and spend data via WebView DOM injection.
/// Supports both interactive mode (modal, for first-time login) and silent mode
/// (hidden WebView embedded in dashboard for background refresh).
/// </summary>
public class GoogleAiWebViewPage : ContentPage
{
    private readonly WebView _webView;
    private readonly Label _statusLabel;
    private readonly bool _silent;

    // Multi-project fetch state
    private List<string> _projectIds = [];
    private int _currentProjectIndex;
    private ScrapePhase _phase;
    private readonly List<GoogleAiUsageRecord> _allRecords = [];
    private GoogleAiSpendInfo? _currentSpendInfo;

    private TaskCompletionSource<List<GoogleAiUsageRecord>?>? _tcs;

    public Grid SilentWebViewGrid { get; }

    private enum ScrapePhase { UsagePage, SpendPage, Done }

    public GoogleAiWebViewPage(bool silent = false)
    {
        _silent = silent;
        _webView = new WebView();
        if (silent)
            _webView.InputTransparent = true;
        _webView.Navigated += OnNavigated;

        _statusLabel = new Label
        {
            Text = "Loading Google AI Studio\u2026",
            Padding = new Thickness(16, 8),
            FontSize = 12
        };

        if (silent)
        {
            SilentWebViewGrid = new Grid
            {
                IsVisible = true,
                InputTransparent = true,
                Children = { _webView, _statusLabel }
            };
        }
        else
        {
            Title = "Connect Google AI Studio";

            var cancelBtn = new Button { Text = "Cancel" };
            cancelBtn.Clicked += (_, _) => _tcs?.TrySetResult(null);

            var retryBtn = new Button
            {
                Text = "I\u2019ve logged in \u2014 Retry",
                FontSize = 13,
                Padding = new Thickness(16, 8)
            };
            retryBtn.Clicked += (_, _) =>
            {
                if (_tcs == null || _projectIds.Count == 0) return;
                _statusLabel.Text = "Retrying\u2026";
                _phase = ScrapePhase.UsagePage;
                _currentProjectIndex = 0;
                _allRecords.Clear();
                NavigateToUsagePage(_projectIds[0]);
            };

            var controlsStack = new VerticalStackLayout
            {
                Padding = new Thickness(16),
                Spacing = 8
            };
            controlsStack.Children.Add(_statusLabel);
            controlsStack.Children.Add(retryBtn);
            controlsStack.Children.Add(cancelBtn);

            var rootGrid = new Grid
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto)
                }
            };
            rootGrid.Children.Add(_webView);
            rootGrid.Children.Add(controlsStack);
            Grid.SetRow(controlsStack, 1);
            Content = rootGrid;
        }
    }

    /// <summary>
    /// Fetches usage data for all provided project IDs.
    /// Call this before awaiting WaitForResultAsync.
    /// </summary>
    public void BeginFetch(IEnumerable<string> projectIds)
    {
        _projectIds = projectIds.ToList();
        _currentProjectIndex = 0;
        _allRecords.Clear();
        _phase = ScrapePhase.UsagePage;

        if (_projectIds.Count > 0)
            NavigateToUsagePage(_projectIds[0]);
    }

    public Task<List<GoogleAiUsageRecord>?> WaitForResultAsync(int timeoutMs = 120_000)
    {
        _tcs = new TaskCompletionSource<List<GoogleAiUsageRecord>?>();
        _ = Task.Delay(timeoutMs).ContinueWith(_ => _tcs.TrySetResult(null));
        return _tcs.Task;
    }

    private void NavigateToUsagePage(string projectId)
    {
        var url = $"https://aistudio.google.com/usage?timeRange=last-1-day&project={projectId}";
        System.Diagnostics.Debug.WriteLine($"[GoogleAiWebView] Navigating to usage: {url}");
        _statusLabel.Text = $"Loading usage for {projectId}\u2026";
        _webView.Source = new UrlWebViewSource { Url = url };
    }

    private void NavigateToSpendPage(string projectId)
    {
        var url = $"https://aistudio.google.com/spend?project={projectId}";
        System.Diagnostics.Debug.WriteLine($"[GoogleAiWebView] Navigating to spend: {url}");
        _statusLabel.Text = $"Loading spend for {projectId}\u2026";
        _webView.Source = new UrlWebViewSource { Url = url };
    }

    private async void OnNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (_tcs == null || _tcs.Task.IsCompleted) return;
        try { await OnNavigatedCoreAsync(e); }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GoogleAiWebView] OnNavigated EXCEPTION: {ex.Message}");
            _tcs.TrySetResult(null);
        }
    }

    private async Task OnNavigatedCoreAsync(WebNavigatedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[GoogleAiWebView] Navigated: result={e.Result}, phase={_phase}, url={e.Url}");

        if (e.Result != WebNavigationResult.Success)
        {
            System.Diagnostics.Debug.WriteLine($"[GoogleAiWebView] Navigation failed: {e.Result}");
            return;
        }
        if (!e.Url.StartsWith("https://aistudio.google.com"))
        {
            System.Diagnostics.Debug.WriteLine($"[GoogleAiWebView] Ignoring non-aistudio URL: {e.Url}");
            return;
        }

        // Wait for SPA render
        await Task.Delay(5000);

        // Poll readyState
        for (int poll = 0; poll < 5; poll++)
        {
            try
            {
                var readyState = await _webView.EvaluateJavaScriptAsync("document.readyState");
                if (readyState?.Contains("complete", StringComparison.OrdinalIgnoreCase) == true)
                    break;
            }
            catch { }
            await Task.Delay(2000);
        }

        var coreWV2 = TryGetCoreWebView2();

        if (_phase == ScrapePhase.UsagePage)
            await ScrapeUsagePageAsync(coreWV2);
        else if (_phase == ScrapePhase.SpendPage)
            await ScrapeSpendPageAsync(coreWV2);
    }

    private async Task ScrapeUsagePageAsync(Microsoft.Web.WebView2.Core.CoreWebView2? coreWV2)
    {
        var projectId = _projectIds[_currentProjectIndex];
        System.Diagnostics.Debug.WriteLine($"[GoogleAiWebView] Scraping usage for {projectId}");

        const string js = """
            (async () => {
                try {
                    // Click all "Populate data" buttons
                    const buttons = [...document.querySelectorAll('button')]
                        .filter(b => b.textContent.includes('Populate data'));
                    buttons.forEach(b => b.click());

                    // Wait for tables to populate
                    await new Promise(r => setTimeout(r, 500));

                    const tables = document.querySelectorAll('table');
                    const result = { models: [] };

                    // T3 = Input Tokens (index 3), T4 = Requests (index 4)
                    const extractTable = (table) => {
                        const rows = [...table.querySelectorAll('tr')];
                        if (rows.length < 2) return [];
                        return rows.slice(1).map(row => {
                            const cells = [...row.querySelectorAll('td, th')];
                            const label = cells[0]?.textContent.trim().split(' Play')[0];
                            const values = cells.slice(2).map(c => c.textContent.trim());
                            return { label, values };
                        });
                    };

                    if (tables.length > 4) {
                        result.inputTokens = extractTable(tables[3]);
                        result.requests = extractTable(tables[4]);
                    } else {
                        result.inputTokens = [];
                        result.requests = [];
                    }

                    window._googleAiResult = JSON.stringify({ ok: true, data: result });
                } catch (ex) {
                    window._googleAiResult = JSON.stringify({ error: ex.message });
                }
            })();
            'started';
            """;

        var raw = await ExecuteJsAndRetrieveAsync(coreWV2, js, "window._googleAiResult || 'null'");

        if (string.IsNullOrEmpty(raw) || raw == "null")
        {
            System.Diagnostics.Debug.WriteLine($"[GoogleAiWebView] No usage data for {projectId}");
            if (_silent) { AdvanceToNextProjectOrFinish(); return; }
            _statusLabel.Text = "Not signed in — log in above then tap Retry.";
            return;
        }

        var records = ParseUsagePage(raw, projectId);
        _allRecords.AddRange(records);
        System.Diagnostics.Debug.WriteLine($"[GoogleAiWebView] Parsed {records.Count} records for {projectId}");

        // Now navigate to spend page for same project
        _phase = ScrapePhase.SpendPage;
        NavigateToSpendPage(projectId);
    }

    private async Task ScrapeSpendPageAsync(Microsoft.Web.WebView2.Core.CoreWebView2? coreWV2)
    {
        var projectId = _projectIds[_currentProjectIndex];
        System.Diagnostics.Debug.WriteLine($"[GoogleAiWebView] Scraping spend for {projectId}");

        const string js = """
            (async () => {
                try {
                    const text = document.body.innerText;
                    const costMatch = text.match(/Cost\s+([\£\$\€])([\d.]+)/);
                    const capMatch = text.match(/([\£\$\€])([\d.]+)\s*\/\s*[\£\$\€]([\d.]+)/);
                    window._googleAiSpendResult = JSON.stringify({
                        ok: true,
                        data: {
                            currency: costMatch?.[1] || '',
                            cost: costMatch?.[2] || '0',
                            capUsed: capMatch?.[2] || '0',
                            capLimit: capMatch?.[3] || '0'
                        }
                    });
                } catch (ex) {
                    window._googleAiSpendResult = JSON.stringify({ error: ex.message });
                }
            })();
            'started';
            """;

        var raw = await ExecuteJsAndRetrieveAsync(coreWV2, js, "window._googleAiSpendResult || 'null'");

        if (!string.IsNullOrEmpty(raw) && raw != "null")
        {
            var spendInfo = ParseSpendPage(raw);
            if (spendInfo != null)
            {
                // Apply spend info to all records for this project (last-1-day range)
                foreach (var r in _allRecords.Where(r => r.ProjectId == projectId))
                {
                    r.Cost = spendInfo.Cost;
                    r.SpendCapUsed = spendInfo.CapUsed;
                    r.SpendCapLimit = spendInfo.CapLimit;
                    r.Currency = spendInfo.Currency;
                }
            }
        }

        AdvanceToNextProjectOrFinish();
    }

    private void AdvanceToNextProjectOrFinish()
    {
        _currentProjectIndex++;
        if (_currentProjectIndex < _projectIds.Count)
        {
            _phase = ScrapePhase.UsagePage;
            NavigateToUsagePage(_projectIds[_currentProjectIndex]);
        }
        else
        {
            _phase = ScrapePhase.Done;
            System.Diagnostics.Debug.WriteLine($"[GoogleAiWebView] All projects done, {_allRecords.Count} total records");
            _statusLabel.Text = "Done.";
            _tcs?.TrySetResult(_allRecords.Count > 0 ? _allRecords : null);
        }
    }

    private async Task<string?> ExecuteJsAndRetrieveAsync(
        Microsoft.Web.WebView2.Core.CoreWebView2? coreWV2,
        string js,
        string retrieveExpr)
    {
        string? raw = null;

        // Step 1: fire JS
        if (coreWV2 != null)
        {
            try
            {
                raw = await coreWV2.ExecuteScriptAsync(js).AsTask();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GoogleAiWebView] JS fire EXCEPTION: {ex.Message}");
            }
        }

        // Step 2: retrieve result
        if (string.IsNullOrEmpty(raw) || raw == "\"started\"")
        {
            await Task.Delay(1500);
            try
            {
                raw = await (coreWV2 != null
                    ? coreWV2.ExecuteScriptAsync(retrieveExpr).AsTask()
                    : _webView.EvaluateJavaScriptAsync(retrieveExpr));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GoogleAiWebView] JS retrieve EXCEPTION: {ex.Message}");
            }
        }

        // Fallback to MAUI wrapper
        if (string.IsNullOrEmpty(raw) || raw == "\"started\"")
        {
            try
            {
                raw = await _webView.EvaluateJavaScriptAsync(js);
                if (string.IsNullOrEmpty(raw) || raw == "\"started\"")
                {
                    await Task.Delay(1500);
                    raw = await _webView.EvaluateJavaScriptAsync(retrieveExpr);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GoogleAiWebView] MAUI fallback EXCEPTION: {ex.Message}");
            }
        }

        // Unwrap JSON string literal if needed
        if (!string.IsNullOrEmpty(raw) && raw != "null" && raw.StartsWith('"'))
        {
            try { raw = System.Text.Json.JsonSerializer.Deserialize<string>(raw); }
            catch { raw = raw.Trim('"'); }
        }

        return raw;
    }

    private static List<GoogleAiUsageRecord> ParseUsagePage(string json, string projectId)
    {
        var records = new List<GoogleAiUsageRecord>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("ok", out _)) return records;
            if (!root.TryGetProperty("data", out var data)) return records;

            // Build a lookup: modelName -> inputTokens from the inputTokens table
            var tokensByModel = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            if (data.TryGetProperty("inputTokens", out var tokensArr))
            {
                foreach (var row in tokensArr.EnumerateArray())
                {
                    var label = row.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(label)) continue;
                    long total = 0;
                    if (row.TryGetProperty("values", out var vals))
                        foreach (var v in vals.EnumerateArray())
                            total += ParseTokenValue(v.GetString() ?? "");
                    tokensByModel[label] = total;
                }
            }

            if (data.TryGetProperty("requests", out var requestsArr))
            {
                foreach (var row in requestsArr.EnumerateArray())
                {
                    var label = row.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(label)) continue;
                    long totalRequests = 0;
                    if (row.TryGetProperty("values", out var vals))
                        foreach (var v in vals.EnumerateArray())
                        {
                            if (long.TryParse(v.GetString()?.Trim(), out var n))
                                totalRequests += n;
                        }

                    tokensByModel.TryGetValue(label, out var tokens);
                    records.Add(new GoogleAiUsageRecord
                    {
                        ProjectId = projectId,
                        ModelName = label,
                        TimeRange = "last-1-day",
                        RequestCount = totalRequests,
                        InputTokens = tokens,
                        FetchedAt = DateTime.UtcNow
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GoogleAiWebView] ParseUsagePage EXCEPTION: {ex.Message}");
        }
        return records;
    }

    private static GoogleAiSpendInfo? ParseSpendPage(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("ok", out _)) return null;
            if (!root.TryGetProperty("data", out var data)) return null;

            var currency = data.TryGetProperty("currency", out var c) ? c.GetString() ?? "" : "";
            decimal.TryParse(data.TryGetProperty("cost", out var cost) ? cost.GetString() : "0", out var costVal);
            decimal.TryParse(data.TryGetProperty("capUsed", out var capUsed) ? capUsed.GetString() : "0", out var capUsedVal);
            decimal.TryParse(data.TryGetProperty("capLimit", out var capLimit) ? capLimit.GetString() : "0", out var capLimitVal);

            return new GoogleAiSpendInfo
            {
                Currency = currency,
                Cost = costVal,
                CapUsed = capUsedVal,
                CapLimit = capLimitVal
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GoogleAiWebView] ParseSpendPage EXCEPTION: {ex.Message}");
            return null;
        }
    }

    internal static long ParseTokenValue(string text)
    {
        text = text.Trim();
        if (string.IsNullOrEmpty(text) || text == "0") return 0;

        var multiplier = 1.0;
        if (text.EndsWith("M", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1_000_000;
            text = text[..^1];
        }
        else if (text.EndsWith("K", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1_000;
            text = text[..^1];
        }

        return double.TryParse(text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var val)
            ? (long)(val * multiplier)
            : 0;
    }

    private Microsoft.Web.WebView2.Core.CoreWebView2? TryGetCoreWebView2()
    {
        try
        {
            if (_webView.Handler is Microsoft.Maui.Handlers.IWebViewHandler h)
            {
                var pv = h.PlatformView;
                if (pv is Microsoft.UI.Xaml.Controls.WebView2 wv2)
                    return wv2.CoreWebView2;
                var pi = pv?.GetType().GetProperty("CoreWebView2");
                return pi?.GetValue(pv) as Microsoft.Web.WebView2.Core.CoreWebView2;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GoogleAiWebView] CoreWebView2 access failed: {ex.Message}");
        }
        return null;
    }

    private record GoogleAiSpendInfo
    {
        public string Currency { get; init; } = "";
        public decimal Cost { get; init; }
        public decimal CapUsed { get; init; }
        public decimal CapLimit { get; init; }
    }
}
