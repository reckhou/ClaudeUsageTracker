using System.Collections.ObjectModel;
using System.Net.Http;
using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Maui.Services;
using ClaudeUsageTracker.Maui.ViewModels;
using Microsoft.Maui.Controls;

namespace ClaudeUsageTracker.Maui.Views;

public partial class ProvidersDashboardPage : ContentPage
{
    private readonly ProvidersDashboardViewModel _vm;
    private readonly MiniModeWindowService _miniModeWindowService;
    private TaskCompletionSource<QuotaRecord?>? _claudeTcs;
    private string? _extractedUrl;
    private Window? _miniWindow;

    /// <summary>Set to the current active page before any refresh call so providers can reach the embedded WebView.</summary>
    public static ProvidersDashboardPage? Current { get; private set; }

    public ProvidersDashboardPage(ProvidersDashboardViewModel vm, MiniModeWindowService miniModeWindowService)
    {
        InitializeComponent();
        _vm = vm;
        _miniModeWindowService = miniModeWindowService;
        BindingContext = vm;
        Current = this;
        ClaudeSilentWebView.Navigated += OnClaudeSilentNavigated;
    }

    /// <summary>Performs a silent WebView-based quota fetch for Claude, returning null on failure.</summary>
    public async Task<QuotaRecord?> FetchClaudeQuotaAsync()
    {
        if (_claudeTcs != null) return null; // Already in progress
        _claudeTcs = new TaskCompletionSource<QuotaRecord?>();

        if (_extractedUrl != null)
        {
            // Page already loaded — OnNavigated won't fire again from visibility change.
            // Set _extractedUrl to null FIRST so the direct call processes correctly,
            // then call OnNavigatedCoreAsync directly.
            var url = _extractedUrl;
            _extractedUrl = null;
            System.Diagnostics.Debug.WriteLine($"[ClaudeSilentWebView] Already loaded, calling core directly with url={url}");
            _ = OnClaudeSilentNavigatedCoreAsync(
                new WebNavigatedEventArgs(WebNavigationEvent.Refresh, null!, url, WebNavigationResult.Success),
                _claudeTcs);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("[ClaudeSilentWebView] First load, waiting for OnNavigated");
            ClaudeSilentWebView.IsVisible = true;
            ClaudeSilentWebView.Opacity = 0; // Hide visually — WebView still initializes and navigates

            // On a new page instance (after tab switch), the WebView navigated during
            // construction BEFORE _claudeTcs was set here, so OnNavigated fired and the
            // handler returned early (tcs was null). Reload to force a fresh OnNavigated
            // that fires AFTER _claudeTcs is set and can be captured.
            try { ClaudeSilentWebView.Reload(); } catch { }
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            return await _claudeTcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("[ClaudeSilentWebView] Timeout waiting for navigation");
            return null;
        }
        finally
        {
            ClaudeSilentWebView.IsVisible = false;
            ClaudeSilentWebView.Opacity = 1;
            _claudeTcs = null;
        }
    }

    private async void OnClaudeSilentNavigated(object? sender, WebNavigatedEventArgs e)
    {
        // Capture TCS locally so we still have it if timeout's finally nulls _claudeTcs while we run
        var tcs = _claudeTcs;
        if (tcs == null) return;

        try
        {
            await OnClaudeSilentNavigatedCoreAsync(e, tcs);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeSilentWebView] OnNavigated EXCEPTION: {ex.Message}");
            tcs.TrySetResult(null);
        }
    }

    private async Task OnClaudeSilentNavigatedCoreAsync(WebNavigatedEventArgs e, TaskCompletionSource<QuotaRecord?> tcs)
    {
        System.Diagnostics.Debug.WriteLine($"[ClaudeSilentWebView] OnNavigated: {e.Result}, url={e.Url}");

        if (e.Result != WebNavigationResult.Success)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeSilentWebView] Navigation failed: {e.Result}");
            return;
        }
        if (!e.Url.StartsWith("https://claude.ai"))
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeSilentWebView] Ignoring non-claude URL: {e.Url}");
            return;
        }
        if (_extractedUrl == e.Url) return;

        System.Diagnostics.Debug.WriteLine($"[ClaudeSilentWebView] Processing: {e.Url}");

        // Wait for page to fully load (React SPA needs time)
        await Task.Delay(5000);

        // Probe WebView2
        var coreWV2 = TryGetCoreWebView2();
        System.Diagnostics.Debug.WriteLine($"[ClaudeSilentWebView] CoreWebView2: {coreWV2 != null}");

        const string js = """
            (async () => {
                try {
                    const orgsResp = await fetch('/api/organizations', { credentials: 'include' });
                    const orgsText = await orgsResp.text();
                    if (!orgsResp.ok) { window._claudeResult = JSON.stringify({ error: 'orgs:' + orgsResp.status }); return; }
                    let orgs;
                    try { orgs = JSON.parse(orgsText); } catch { window._claudeResult = JSON.stringify({ error: 'orgs:bad_json' }); return; }
                    const uuid = orgs[0]?.uuid;
                    if (!uuid) { window._claudeResult = JSON.stringify({ error: 'no uuid' }); return; }
                    const usageResp = await fetch('/api/organizations/' + uuid + '/usage', { credentials: 'include' });
                    const usageText = await usageResp.text();
                    if (!usageResp.ok) { window._claudeResult = JSON.stringify({ error: 'usage:' + usageResp.status }); return; }
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

        // Step 1: fire JS
        if (coreWV2 != null)
        {
            try
            {
                raw = await coreWV2.ExecuteScriptAsync(js).AsTask();
                System.Diagnostics.Debug.WriteLine($"[ClaudeSilentWebView] Fire result: {raw}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClaudeSilentWebView] Fire EXCEPTION: {ex.Message}");
            }
        }

        // Step 2: retrieve result
        if (string.IsNullOrEmpty(raw) || raw == "\"started\"")
        {
            await Task.Delay(500);
            try
            {
                raw = await (coreWV2 != null
                    ? coreWV2.ExecuteScriptAsync("window._claudeResult || 'null'").AsTask()
                    : ClaudeSilentWebView.EvaluateJavaScriptAsync("window._claudeResult || 'null'"));
                System.Diagnostics.Debug.WriteLine($"[ClaudeSilentWebView] Retrieved: {raw}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClaudeSilentWebView] Retrieve EXCEPTION: {ex.Message}");
            }
        }

        // Fallback to MAUI wrapper
        if (string.IsNullOrEmpty(raw) || raw == "\"started\"")
        {
            try
            {
                raw = await ClaudeSilentWebView.EvaluateJavaScriptAsync(js);
                if (!string.IsNullOrEmpty(raw) && raw != "\"started\"") { /* got it */ }
                else
                {
                    await Task.Delay(500);
                    raw = await ClaudeSilentWebView.EvaluateJavaScriptAsync("window._claudeResult || 'null'");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ClaudeSilentWebView] MAUI fallback EXCEPTION: {ex.Message}");
            }
        }

        if (string.IsNullOrEmpty(raw) || raw == "\"started\"" || raw == "null")
        {
            System.Diagnostics.Debug.WriteLine("[ClaudeSilentWebView] No data retrieved");
            tcs.TrySetResult(null);
            return;
        }

        string? json;
        try { json = System.Text.Json.JsonSerializer.Deserialize<string>(raw); }
        catch { json = raw.Trim('"'); }

        if (string.IsNullOrEmpty(json)) { tcs.TrySetResult(null); return; }

        var record = ClaudeProWebViewPage.ParseUsageResponse(json, out var parseError);
        if (record != null)
        {
            _extractedUrl = e.Url;
            System.Diagnostics.Debug.WriteLine($"[ClaudeSilentWebView] Success: {record.FiveHourUtilization}%, {record.SevenDayUtilization}%");
            tcs.TrySetResult(record);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeSilentWebView] Parse error: {parseError}");
            tcs.TrySetResult(null);
        }
    }

    /// <summary>
    /// Performs a silent Google AI Studio fetch using the embedded silent WebView.
    /// Returns null on failure or timeout.
    /// </summary>
    public async Task<List<ClaudeUsageTracker.Core.Models.GoogleAiUsageRecord>?> FetchGoogleAiUsageAsync(List<string> projectIds)
    {
        var page = new GoogleAiWebViewPage(silent: true);

        // Add the silent WebView grid to the page layout so it can navigate
        GoogleAiSilentWebViewContainer.Children.Clear();
        GoogleAiSilentWebViewContainer.Children.Add(page.SilentWebViewGrid);

        try
        {
            page.BeginFetch(projectIds);
            return await page.WaitForResultAsync();
        }
        finally
        {
            GoogleAiSilentWebViewContainer.Children.Clear();
        }
    }

    private void OnMiniModeClicked(object sender, EventArgs e)
    {
        if (_miniWindow != null)
        {
            Application.Current!.CloseWindow(_miniWindow);
            // _miniWindow cleared + main window restored by the Destroying handler
            return;
        }

        // Store main window reference before opening mini so the service can hide it
        _miniModeWindowService.SetMainWindow(Window!);

        // Construct MiniModePage directly (not via DI) so we can pass the resolved
        // MiniModeViewModel instance to MiniModeSettingsPage later without creating
        // a second, disconnected ViewModel instance.
        var miniVm   = Handler!.MauiContext!.Services.GetRequiredService<MiniModeViewModel>();
        var miniPage = new MiniModePage(miniVm, _miniModeWindowService);
        _miniWindow  = new Window(miniPage) { Title = "" };
        _miniWindow.Destroying += (_, _) =>
        {
            // Ensure main window is restored even if mini was force-closed
            _miniModeWindowService.ShowMainWindow();
            _miniWindow = null;
            MiniModeButton.Text = "⊞ Mini";
        };
        Application.Current!.OpenWindow(_miniWindow);
        MiniModeButton.Text = "⊠ Mini";
    }

    private Microsoft.Web.WebView2.Core.CoreWebView2? TryGetCoreWebView2()
    {
        try
        {
            if (ClaudeSilentWebView.Handler is Microsoft.Maui.Handlers.IWebViewHandler webViewHandler)
            {
                var platformView = webViewHandler.PlatformView;
                if (platformView is Microsoft.UI.Xaml.Controls.WebView2 wv2)
                    return wv2.CoreWebView2;
                var pi = platformView?.GetType().GetProperty("CoreWebView2");
                return pi?.GetValue(platformView) as Microsoft.Web.WebView2.Core.CoreWebView2;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClaudeSilentWebView] CoreWebView2 access failed: {ex.Message}");
        }
        return null;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Show Google AI card immediately from cached SQLite data (no WebView scrape needed).
        await _vm.LoadGoogleAiFromCacheAsync();

        // Guard only prevents re-loading Claude/MiniMaxi providers on page re-visits.
        // Google AI refresh and auto-refresh timers must always be set up.
        var firstLoad = _vm.Providers.Count == 0;
        if (firstLoad)
        {
            try { await _vm.RefreshAllAsync(); } catch { }
            if (!_vm.IsAutoRefreshRunning)
                _vm.ToggleAutoRefresh();
        }

        // Always ensure Google AI scrape + auto-refresh are running (even on page re-visit)
        var googleAiProjects = await _vm.GetGoogleAiProjectIdsAsync();
        if (googleAiProjects.Count > 0)
        {
            if (firstLoad)
            {
                // RefreshAllAsync already called RefreshGoogleAiAsync — just start the timer
                _vm.StartGoogleAiAutoRefresh();
            }
            else if (!_vm.IsGoogleAiAutoRefreshRunning)
            {
                // Page re-visit: trigger an immediate scrape then start the timer
                try { await _vm.RefreshGoogleAiAsync(); } catch { }
                _vm.StartGoogleAiAutoRefresh();
            }
        }
    }
}
