using CommunityToolkit.Maui;
using ClaudeUsageTracker.Core.Services;
using ClaudeUsageTracker.Core.ViewModels;
using ClaudeUsageTracker.Maui.Services;
using ClaudeUsageTracker.Maui.Views;
using ClaudeUsageTracker.Maui.ViewModels;
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
		builder.Services.AddSingleton<IUsageDataService>(_ =>
		{
			var path = Path.Combine(FileSystem.AppDataDirectory, "usage.db");
			return new UsageDataService(path);
		});
		builder.Services.AddSingleton<IClaudeAiUsageService, ClaudeAiUsageService>();
		builder.Services.AddSingleton<IUpdateService>(_ =>
			new UpdateService(
				AppInfo.VersionString,
				quitApp: () => Application.Current?.Quit()));
		builder.Services.AddSingleton<IUsageProvider, MiniMaxiUsageProvider>();
		builder.Services.AddSingleton<IUsageProvider, ClaudeProUsageProvider>();
		builder.Services.AddTransient<SetupViewModel>(sp =>
			new SetupViewModel(
				sp.GetRequiredService<ISecureStorageService>(),
				sp.GetRequiredService<IUsageDataService>()));
		builder.Services.AddSingleton<ProvidersDashboardViewModel>(sp =>
			new ProvidersDashboardViewModel(
				sp.GetRequiredService<IUsageDataService>() as UsageDataService
					?? throw new InvalidOperationException("UsageDataService must be UsageDataService"),
				sp.GetRequiredService<IEnumerable<IUsageProvider>>(),
				sp.GetRequiredService<ISecureStorageService>(),
				sp.GetRequiredService<IUpdateService>()));
		builder.Services.AddTransient<SetupPage>();
		builder.Services.AddSingleton<MiniModeWindowService>();
		builder.Services.AddTransient<ProvidersDashboardPage>(sp =>
			new ProvidersDashboardPage(
				sp.GetRequiredService<ProvidersDashboardViewModel>(),
				sp.GetRequiredService<MiniModeWindowService>()));
		builder.Services.AddSingleton<MiniModeViewModel>();
		builder.Services.AddTransient<MiniModePage>();
		builder.Services.AddTransient<MiniModeSettingsPage>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
