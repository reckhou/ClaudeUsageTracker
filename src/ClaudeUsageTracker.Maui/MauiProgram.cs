using CommunityToolkit.Maui;
using ClaudeUsageTracker.Core.Services;
using ClaudeUsageTracker.Core.ViewModels;
using ClaudeUsageTracker.Maui.Services;
using ClaudeUsageTracker.Maui.Views;
using Microsoft.Extensions.Logging;

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
		builder.Services.AddTransient<SetupViewModel>();
		builder.Services.AddTransient<DashboardViewModel>();
		builder.Services.AddTransient<SetupPage>();
		builder.Services.AddTransient<DashboardPage>();
		builder.Services.AddTransient<MobileDashboardPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
