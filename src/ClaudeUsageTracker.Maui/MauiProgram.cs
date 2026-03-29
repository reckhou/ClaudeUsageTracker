using CommunityToolkit.Maui;
using ClaudeUsageTracker.Core.Services;
using ClaudeUsageTracker.Core.ViewModels;
using ClaudeUsageTracker.Maui.Services;
using ClaudeUsageTracker.Maui.Views;
using ClaudeUsageTracker.Maui.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace ClaudeUsageTracker.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkit()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.Services.AddSingleton<ISecureStorageService, MauiSecureStorageService>();
		builder.Services.AddSingleton<HttpClient>();
		builder.Services.AddSingleton<AnthropicApiService>();
		builder.Services.AddSingleton<IUsageDataService>(_ =>
		{
			var path = Path.Combine(FileSystem.AppDataDirectory, "usage.db");
			return new UsageDataService(path);
		});
		builder.Services.AddSingleton<IClaudeAiUsageService, ClaudeAiUsageService>();
		builder.Services.AddSingleton<IUsageProvider, MiniMaxiUsageProvider>();
		builder.Services.AddSingleton<IUsageProvider, GoogleAIUsageProvider>();
		builder.Services.AddSingleton<IUsageProvider, ClaudeProUsageProvider>();
		builder.Services.AddTransient<SetupViewModel>();
		builder.Services.AddTransient<DashboardViewModel>(sp => new DashboardViewModel(
			sp.GetRequiredService<ISecureStorageService>(),
			sp.GetRequiredService<AnthropicApiService>(),
			sp.GetRequiredService<IUsageDataService>(),
			sp.GetRequiredService<IClaudeAiUsageService>()));
		builder.Services.AddSingleton<ProvidersDashboardViewModel>(sp =>
			new ProvidersDashboardViewModel(
				sp.GetRequiredService<IUsageDataService>() as UsageDataService
					?? throw new InvalidOperationException("UsageDataService must be UsageDataService"),
				sp.GetRequiredService<IEnumerable<IUsageProvider>>(),
				sp.GetRequiredService<ISecureStorageService>()));
		builder.Services.AddTransient<SetupPage>();
		builder.Services.AddTransient<DashboardPage>();
		builder.Services.AddTransient<MobileDashboardPage>();
		builder.Services.AddTransient<ProvidersDashboardPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
