using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Core.Services;
using ClaudeUsageTracker.Maui.Views;
using Microsoft.Maui.Controls;

namespace ClaudeUsageTracker.Maui.Services;

public class ClaudeAiUsageService : IClaudeAiUsageService
{
    public async Task<QuotaRecord?> ConnectAndFetchAsync()
    {
        var page = new ClaudeProWebViewPage();
        await Application.Current!.MainPage!.Navigation.PushModalAsync(page);
        var record = await page.WaitForResultAsync();
        await Application.Current.MainPage.Navigation.PopModalAsync();
        return record;
    }

    public async Task<QuotaRecord?> FetchQuotaAsync()
    {
        var page = new ClaudeProWebViewPage(silent: true);
        var rootPage = Application.Current!.MainPage!.Navigation.NavigationStack.LastOrDefault()
            ?? Application.Current.MainPage;

        // Inject WebView as a full-size invisible overlay on the current page (avoids PushModalAsync overlay on Windows)
        var hiddenGrid = page.CreateSilentWebView();
        System.Diagnostics.Debug.WriteLine($"[ClaudeAiUsageService] Root page type: {rootPage.GetType().Name}");
        rootPage.AddContentPage(hiddenGrid);
        System.Diagnostics.Debug.WriteLine("[ClaudeAiUsageService] Hidden grid added to page");

        var record = await page.WaitForResultAsync(timeoutMs: 10_000);
        System.Diagnostics.Debug.WriteLine($"[ClaudeAiUsageService] WaitForResultAsync returned: {record == null}");

        // Clean up injected WebView
        rootPage.RemoveContentPage(hiddenGrid);
        return record;
    }
}

internal static class PageExtensions
{
    // Stores the original page Content before we wrap it in Grid
    private static readonly Dictionary<Page, View> _originalContent = new();

    public static Grid CreateSilentWebView(this ClaudeProWebViewPage page) =>
        page.SilentWebViewGrid;

    public static void AddContentPage(this Page page, Grid overlay)
    {
        var cp = page as ContentPage;
        if (cp == null) return;

        // Wrap existing content in a Grid so we can stack the WebView overlay on top
        if (cp.Content is not Grid rootGrid)
        {
            _originalContent[page] = cp.Content;
            rootGrid = new Grid();
            var oldContent = cp.Content;
            cp.Content = rootGrid;
            rootGrid.Children.Add(oldContent);
        }

        // Add WebView overlay as the last child (on top), filling the Grid
        Grid.SetRow(overlay, 0);
        Grid.SetColumn(overlay, 0);
        Grid.SetRowSpan(overlay, 100); // Span all rows
        Grid.SetColumnSpan(overlay, 100); // Span all columns
        overlay.HorizontalOptions = LayoutOptions.Fill;
        overlay.VerticalOptions = LayoutOptions.Fill;
        rootGrid.Children.Add(overlay);
    }

    public static void RemoveContentPage(this Page page, Grid overlay)
    {
        var cp = page as ContentPage;
        if (cp?.Content is Grid rootGrid)
        {
            rootGrid.Children.Remove(overlay);
            // Restore original content (unwrap Grid) after last overlay removed
            if (_originalContent.TryGetValue(page, out var original))
            {
                _originalContent.Remove(page);
                cp.Content = original;
            }
        }
    }
}
