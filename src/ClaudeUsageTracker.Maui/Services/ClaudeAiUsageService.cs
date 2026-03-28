using ClaudeUsageTracker.Core.Models;
using ClaudeUsageTracker.Core.Services;
using ClaudeUsageTracker.Maui.Views;

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
        await Application.Current!.MainPage!.Navigation.PushModalAsync(page, animated: false);
        var record = await page.WaitForResultAsync(timeoutMs: 10_000);
        await Application.Current.MainPage.Navigation.PopModalAsync(animated: false);
        return record;
    }
}
