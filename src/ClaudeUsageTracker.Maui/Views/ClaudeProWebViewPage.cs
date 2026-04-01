using ClaudeUsageTracker.Core.Models;

namespace ClaudeUsageTracker.Maui.Views;

public class ClaudeProWebViewPage : ContentPage
{
    private readonly WebView _webView;
    private readonly Label _statusLabel;
    private readonly Label _errorLabel;
    private readonly Button _retryBtn;
    private readonly Button _copyErrorBtn;
    private string? _lastError;
    private readonly TaskCompletionSource<QuotaRecord?> _tcs = new();
    private readonly bool _silent;
    private string? _extractedUrl;

    /// <summary>Returns the root Grid for the silent WebView, to be hosted directly in the current page (avoids modal overlay).</summary>
    public Grid SilentWebViewGrid { get; }

    public ClaudeProWebViewPage(bool silent = false)
    {
        _silent = silent;
        _webView = new WebView { Source = new UrlWebViewSource { Url = "https://claude.ai/settings/usage" } };
        if (silent)
            _webView.InputTransparent = true; // Let touches pass through to the dashboard below
        _webView.Navigated += OnNavigated;

        _statusLabel = new Label
        {
            Text = "Loading claude.ai\u2026",
            Padding = new Thickness(16, 8),
            FontSize = 12
        };

        _retryBtn = new Button
        {
            Text = "I\u2019ve logged in \u2014 Retry",
            FontSize = 13,
            Padding = new Thickness(16, 8),
            MinimumWidthRequest = 160
        };
        _retryBtn.Clicked += async (_, _) =>
        {
            _statusLabel.Text = "Reloading\u2026";
            _errorLabel.IsVisible = false;
            // Clear extracted URL so OnNavigated will process after login
            _extractedUrl = null;
            // Force a hard reload to ensure fresh auth state, not cached content
            _webView.Source = new UrlWebViewSource { Url = "https://claude.ai/settings/usage" };
        };

        _errorLabel = new Label
        {
            Text = "",
            IsVisible = false,
            FontSize = 11,
            TextColor = Colors.OrangeRed,
            Padding = new Thickness(16, 4, 16, 0)
        };

        _copyErrorBtn = new Button
        {
            Text = "Copy Error",
            IsVisible = false,
            FontSize = 13,
            BackgroundColor = new Color(60, 60, 60),
            TextColor = Colors.White,
            Padding = new Thickness(16, 8),
            MinimumWidthRequest = 100
        };
        _copyErrorBtn.Clicked += async (_, _) =>
        {
            if (!string.IsNullOrEmpty(_lastError))
            {
                await Clipboard.Default.SetTextAsync(_lastError);
                var original = _copyErrorBtn.Text;
                _copyErrorBtn.Text = "Copied!";
                await Task.Delay(2000);
                _copyErrorBtn.Text = original;
            }
        };

        if (silent)
        {
            // Host this Grid directly in the current page (not as a modal overlay) to avoid blocking input.
            // Temporarily visible to confirm WebView CAN navigate — then we hide it after first nav.
            SilentWebViewGrid = new Grid
            {
                IsVisible = true,
                InputTransparent = true,
                Children = { _webView, _statusLabel }
            };
        }
        else
        {
            Title = "Connect Claude Pro";

            var closeBtn = new Button { Text = "Cancel" };
            closeBtn.Clicked += (_, _) => _tcs.TrySetResult(null);

            // Always-visible horizontal button row
            var buttonRow = new HorizontalStackLayout
            {
                Spacing = 12,
                HorizontalOptions = LayoutOptions.Center,
                Padding = new Thickness(0, 8)
            };
            buttonRow.Children.Add(_retryBtn);
            buttonRow.Children.Add(_copyErrorBtn);

            var controlsStack = new VerticalStackLayout
            {
                Padding = new Thickness(16),
                Spacing = 8
            };
            controlsStack.Children.Add(_statusLabel);
            controlsStack.Children.Add(_errorLabel);
            controlsStack.Children.Add(buttonRow);
            controlsStack.Children.Add(closeBtn);

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

    public Task<QuotaRecord?> WaitForResultAsync(int timeoutMs = 60_000)
    {
        System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] WaitForResultAsync started with timeout={timeoutMs}ms");
        _ = Task.Delay(timeoutMs).ContinueWith(_ =>
        {
            System.Diagnostics.Debug.WriteLine("[ClaudeProWebView] Timeout fired, TCS set to null");
            _tcs.TrySetResult(null);
        });
        return _tcs.Task;
    }

    private async void OnNavigated(object? sender, WebNavigatedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] OnNavigated FIRED: result={e.Result}, url={e.Url}");
        // Guard: if TCS already resolved (e.g. timeout fired first), skip all work
        if (_tcs.Task.IsCompleted)
        {
            System.Diagnostics.Debug.WriteLine("[ClaudeProWebView] TCS already completed, skipping");
            return;
        }

        try
        {
            await OnNavigatedCoreAsync(e);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] OnNavigated EXCEPTION: {ex.Message}");
            if (!_tcs.Task.IsCompleted)
                _tcs.TrySetResult(null);
        }
    }

    private async Task OnNavigatedCoreAsync(WebNavigatedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] OnNavigated: result={e.Result}, url={e.Url}");
        if (e.Result != WebNavigationResult.Success)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] Navigation failed: {e.Result}");
            _statusLabel.Text = $"Navigation failed: {e.Result}";
            return;
        }
        if (!e.Url.StartsWith("https://claude.ai"))
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] Ignoring non-claude.ai URL: {e.Url}");
            return;
        }
        if (_extractedUrl == e.Url)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] Already extracted from this URL: {e.Url}");
            return;
        }
        System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] Processing claude.ai navigation to {e.Url}");

        _statusLabel.Text = "Fetching quota data\u2026";
        _retryBtn.IsVisible = false;

        // Try to access CoreWebView2 directly via the PlatformView cast
        Microsoft.Web.WebView2.Core.CoreWebView2? coreWV2 = null;
        try
        {
            if (_webView.Handler is Microsoft.Maui.Handlers.IWebViewHandler webViewHandler)
            {
                var platformView = webViewHandler.PlatformView;
                System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] PlatformView type: {platformView?.GetType().FullName}");
                if (platformView is Microsoft.UI.Xaml.Controls.WebView2 wv2)
                {
                    coreWV2 = wv2.CoreWebView2;
                    System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] CoreWebView2: {coreWV2 != null}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] PlatformView is not WebView2, trying CoreWebView2 property...");
                    var pi = platformView?.GetType().GetProperty("CoreWebView2");
                    if (pi != null)
                    {
                        coreWV2 = pi.GetValue(platformView) as Microsoft.Web.WebView2.Core.CoreWebView2;
                        System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] CoreWebView2 via reflection: {coreWV2 != null}");
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] Handler type: {_webView.Handler?.GetType().FullName ?? "null"}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] CoreWebView2 access failed: {ex.Message}");
        }

        // Wait for WebView2 runtime and React SPA to fully initialize after navigation
        await Task.Delay(5000);

        // Poll for document ready state before attempting JS eval
        for (int poll = 0; poll < 5; poll++)
        {
            string? readyState = null;
            try
            {
                readyState = await _webView.EvaluateJavaScriptAsync("document.readyState");
                System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] Document readyState: {readyState}");
                if (readyState != null && readyState.Contains("complete", StringComparison.OrdinalIgnoreCase))
                    break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] readyState poll {poll} failed: {ex.Message}");
            }
            await Task.Delay(2000);
        }

        // Probe with a simple JS eval first to verify the WebView can return any result
        string? probe = null;
        try
        {
            System.Diagnostics.Debug.WriteLine("[ClaudeProWebView] Probing with simple JSON.stringify eval...");
            probe = await _webView.EvaluateJavaScriptAsync("JSON.stringify({ probe: 'ok', ts: Date.now() })");
            System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] Probe result: {(probe ?? "null")}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] Probe EXCEPTION: {ex}");
        }

        // Test sync eval
        string? syncTest = null;
        try
        {
            System.Diagnostics.Debug.WriteLine("[ClaudeProWebView] Testing sync JS (document.title)...");
            syncTest = await coreWV2!.ExecuteScriptAsync("document.title");
            System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] Sync test result: {(syncTest ?? "null")}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] Sync test EXCEPTION: {ex}");
        }

        // Test if async returns the Promise object (which serializes to {})
        string? asyncTest = null;
        try
        {
            System.Diagnostics.Debug.WriteLine("[ClaudeProWebView] Testing async IIFE without top-level await...");
            asyncTest = await coreWV2!.ExecuteScriptAsync("(async () => 'async result')()");
            System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] Async IIFE test: {(asyncTest ?? "null")}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] Async IIFE test EXCEPTION: {ex}");
        }

        // Two-step pattern: JS stores result in window._claudeResult, then we retrieve it.
        // This avoids ExecuteScriptAsync's async/await handling issues.
        const string js = """
            (async () => {
                try {
                    const orgsResp = await fetch('/api/organizations', { credentials: 'include' });
                    const orgsText = await orgsResp.text();
                    if (!orgsResp.ok) { window._claudeResult = JSON.stringify({ error: 'orgs:' + orgsResp.status, body: orgsText.substring(0, 200) }); return; }
                    let orgs;
                    try { orgs = JSON.parse(orgsText); } catch { window._claudeResult = JSON.stringify({ error: 'orgs:bad_json' }); return; }
                    const uuid = orgs[0]?.uuid;
                    if (!uuid) { window._claudeResult = JSON.stringify({ error: 'no uuid' }); return; }
                    const usageResp = await fetch('/api/organizations/' + uuid + '/usage', { credentials: 'include' });
                    const usageText = await usageResp.text();
                    if (!usageResp.ok) { window._claudeResult = JSON.stringify({ error: 'usage:' + usageResp.status, body: usageText.substring(0, 200) }); return; }
                    let data;
                    try { data = JSON.parse(usageText); } catch { window._claudeResult = JSON.stringify({ error: 'usage:bad_json' }); return; }
                    window._claudeResult = JSON.stringify({ ok: true, data });
                } catch (ex) {
                    window._claudeResult = JSON.stringify({ error: 'js:' + ex.message });
                }
            })();
            'started';
            """;

        string? raw = null;
        string? errorDetail = null;

        try
        {
            System.Diagnostics.Debug.WriteLine("[ClaudeProWebView] Executing async JS (two-step pattern)...");
            raw = await coreWV2!.ExecuteScriptAsync(js);
            System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] ExecuteScriptAsync (fire-and-get) returned: {(raw == null ? "null" : raw)}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] CoreWebView2.ExecuteScriptAsync EXCEPTION: {ex}");
            errorDetail = "CoreWV2Exception: " + ex.Message;
        }

        // Step 2: poll for the stored result — on first boot, fresh TCP/TLS connections
        // to claude.ai take 2-3 s, so a fixed 500 ms wait misses the result.
        // Poll every 500 ms for up to 15 seconds before giving up.
        if (string.IsNullOrEmpty(raw) || raw == "\"started\"")
        {
            for (int poll = 0; poll < 30; poll++)
            {
                await Task.Delay(500);
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] Poll {poll}: retrieving window._claudeResult...");
                    raw = await coreWV2!.ExecuteScriptAsync("window._claudeResult || 'null'");
                    System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] Poll {poll}: {(raw == null ? "null" : raw)}");
                    if (!string.IsNullOrEmpty(raw) && raw != "null" && raw != "\"started\"")
                        break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] Poll {poll} EXCEPTION: {ex.Message}");
                    errorDetail = "RetrieveException: " + ex.Message;
                    break;
                }
            }
        }

        // Fall back to MAUI wrapper (polls the same way)
        if (string.IsNullOrEmpty(raw) || raw == "\"started\"" || raw == "null")
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[ClaudeProWebView] Fallback: executing via MAUI wrapper...");
                raw = await _webView.EvaluateJavaScriptAsync(js);
                System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] MAUI wrapper returned: {(raw == null ? "null" : raw)}");
                if (string.IsNullOrEmpty(raw) || raw == "\"started\"")
                {
                    for (int poll = 0; poll < 30; poll++)
                    {
                        await Task.Delay(500);
                        var retrieved = await _webView.EvaluateJavaScriptAsync("window._claudeResult || 'null'");
                        System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] MAUI poll {poll}: {(retrieved == null ? "null" : retrieved)}");
                        if (!string.IsNullOrEmpty(retrieved) && retrieved != "null" && retrieved != "\"started\"")
                        {
                            raw = retrieved;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] MAUI fallback EXCEPTION: {ex}");
                errorDetail = "MauiFallbackException: " + ex.Message;
            }
        }

        if (string.IsNullOrEmpty(raw) || raw == "\"started\"" || raw == "null")
        {
            if (_silent) { _tcs.TrySetResult(null); return; }
            var msg = errorDetail != null
                ? $"JS error: {errorDetail}. Try again."
                : "Not signed in \u2014 log in at claude.ai above, then tap Retry.";
            _statusLabel.Text = msg;
            _lastError = msg;
            _errorLabel.Text = msg;
            _errorLabel.IsVisible = true;
            _copyErrorBtn.IsVisible = true;
            _retryBtn.IsVisible = true;
            System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] Error UI shown — retryBtn visible: {_retryBtn.IsVisible}");
            return;
        }

        // raw is a JSON string literal — parse it as JSON to get the actual object string
        string? json = null;
        try
        {
            json = System.Text.Json.JsonSerializer.Deserialize<string>(raw);
        }
        catch
        {
            // Fallback: manual trim
            json = raw.Trim('"');
        }

        if (string.IsNullOrEmpty(json))
        {
            if (_silent) { _tcs.TrySetResult(null); return; }
            _lastError = "Empty response from quota API";
            _statusLabel.Text = "Empty response. Tap Retry.";
            _errorLabel.Text = "Empty response from quota API";
            _errorLabel.IsVisible = true;
            _retryBtn.IsVisible = true;
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] Parsed JSON: {json.Substring(0, Math.Min(json.Length, 100))}");
        var record = ParseUsageResponse(json, out var parseError);

        if (record != null)
        {
            _extractedUrl = e.Url;
            _statusLabel.Text = $"Session: {record.FiveHourUtilization}% \u00b7 Weekly: {record.SevenDayUtilization}%";
            _errorLabel.IsVisible = false;
            _tcs.TrySetResult(record);
        }
        else
        {
            if (_silent) { _tcs.TrySetResult(null); return; }
            var displayError = string.IsNullOrEmpty(parseError) ? "Unknown parse error" : parseError;
            _statusLabel.Text = "Could not parse response. Log in at claude.ai then tap Retry.";
            _lastError = displayError;
            _errorLabel.Text = displayError;
            _errorLabel.IsVisible = true;
            _copyErrorBtn.IsVisible = true;
            _retryBtn.IsVisible = true;
            System.Diagnostics.Debug.WriteLine($"[ClaudeProWebView] Parse error UI shown — retryBtn visible: {_retryBtn.IsVisible}");
            return;
        }
    }

    internal static QuotaRecord? ParseUsageResponse(string json, out string? error)
    {
        error = null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var errProp))
            {
                error = errProp.GetString();
                return null;
            }
            if (!root.TryGetProperty("data", out var data))
            {
                error = "Missing 'data' field";
                return null;
            }

            var record = new QuotaRecord { FetchedAt = DateTime.UtcNow };

            if (data.TryGetProperty("five_hour", out var fh) && fh.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                record.FiveHourUtilization = fh.GetProperty("utilization").GetInt32();
                if (fh.TryGetProperty("resets_at", out var fhReset) && fhReset.ValueKind == System.Text.Json.JsonValueKind.String)
                    record.FiveHourResetsAt = fhReset.GetDateTime().ToUniversalTime();
            }
            if (data.TryGetProperty("seven_day", out var sd) && sd.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                record.SevenDayUtilization = sd.GetProperty("utilization").GetInt32();
                if (sd.TryGetProperty("resets_at", out var sdReset) && sdReset.ValueKind == System.Text.Json.JsonValueKind.String)
                    record.SevenDayResetsAt = sdReset.GetDateTime().ToUniversalTime();
            }
            if (data.TryGetProperty("extra_usage", out var eu) && eu.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                record.ExtraUsageEnabled = eu.TryGetProperty("is_enabled", out var ie) && ie.ValueKind == System.Text.Json.JsonValueKind.True;
                if (eu.TryGetProperty("utilization", out var u) && u.ValueKind == System.Text.Json.JsonValueKind.Number)
                    record.ExtraUsageUtilization = u.GetInt32();
            }
            return record;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }
}
