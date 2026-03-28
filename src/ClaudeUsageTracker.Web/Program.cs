using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ClaudeUsageTracker.Web;
using ClaudeUsageTracker.Web.Services;
using ClaudeUsageTracker.Core.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped<HttpClient>(_ => new HttpClient());
builder.Services.AddScoped<ISecureStorageService, BrowserSecureStorageService>();
builder.Services.AddScoped<AnthropicApiService>();
builder.Services.AddScoped<IUsageDataService>(_ => new UsageDataService(":memory:"));
builder.Services.AddScoped<ClaudeUsageTracker.Core.ViewModels.SetupViewModel>();
builder.Services.AddScoped<ClaudeUsageTracker.Core.ViewModels.DashboardViewModel>();

await builder.Build().RunAsync();
