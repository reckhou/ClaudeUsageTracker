using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace ClaudeUsageTracker.Maui.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	// Held for process lifetime — prevents GC from releasing the OS mutex handle
	private static Mutex? _singleInstanceMutex;

	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		_singleInstanceMutex = new Mutex(initiallyOwned: true,
			name: "ClaudeUsageTracker_SingleInstance",
			out bool isNewInstance);

		if (!isNewInstance)
		{
			// Another instance is already running — exit before any UI is created
			Environment.Exit(0);
			return;
		}

		this.InitializeComponent();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

