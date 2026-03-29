namespace ClaudeUsageTracker.Maui.Services;

public class MiniModeWindowService
{
#if WINDOWS
    private IntPtr _hwnd;
    private Microsoft.UI.Windowing.AppWindow? _appWindow;
    private Microsoft.UI.Windowing.OverlappedPresenter? _presenter;
    private Microsoft.UI.Windowing.AppWindow? _mainAppWindow;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseCapture();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const int GWL_EXSTYLE       = -20;
    private const int WS_EX_LAYERED     = 0x00080000;
    private const uint LWA_ALPHA        = 0x00000002;
    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION         = 2;

    // Approximate physical-pixel measurements for auto-resize (at 100% DPI).
    // name row + session row + weekly row + resets label + divider + padding/spacing
    private const int RowHeightPx    = 110;
    private const int HeaderHeightPx = 40;
    private const int PaddingPx      = 24;
    private const int WindowWidthPx  = 460;

    private static Microsoft.UI.Windowing.AppWindow GetAppWindow(Window mauiWindow)
    {
        var native = mauiWindow.Handler!.PlatformView as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException("No native WinUI window.");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(native);
        var id   = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);
    }
#endif

    // ── Mini window setup ─────────────────────────────────────────────────

    public void ConfigureWindow(Window window, bool isAlwaysOnTop, double opacity)
    {
#if WINDOWS
        var native = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException("No native WinUI window.");
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(native);
        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);

        _appWindow.Resize(new Windows.Graphics.SizeInt32(WindowWidthPx, HeaderHeightPx + PaddingPx));

        if (_appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
        {
            _presenter      = p;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.IsResizable   = true;
            p.IsAlwaysOnTop = isAlwaysOnTop;
        }

        // Extend XAML content into the title bar so our custom drag strip fills the
        // full window surface. Make system caption buttons invisible — our custom ←
        // and ⚙ buttons handle those roles. The invisible system close button remains
        // as a fallback (if clicked, Destroying fires and restores the main window).
        var tb = _appWindow.TitleBar;
        tb.ExtendsContentIntoTitleBar      = true;
        tb.ButtonBackgroundColor           = Microsoft.UI.Colors.Transparent;
        tb.ButtonInactiveBackgroundColor   = Microsoft.UI.Colors.Transparent;
        tb.ButtonForegroundColor           = Microsoft.UI.Colors.Transparent;
        tb.ButtonInactiveForegroundColor   = Microsoft.UI.Colors.Transparent;
        tb.ButtonHoverBackgroundColor      = Microsoft.UI.Colors.Transparent;
        tb.ButtonHoverForegroundColor      = Microsoft.UI.Colors.Transparent;
        tb.ButtonPressedBackgroundColor    = Microsoft.UI.Colors.Transparent;
        tb.ButtonPressedForegroundColor    = Microsoft.UI.Colors.Transparent;

        var exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED);
        SetOpacity(opacity);
#endif
    }

    public void SetOpacity(double opacity)
    {
#if WINDOWS
        if (_hwnd == IntPtr.Zero) return;
        var alpha = (byte)(Math.Clamp(opacity, 0.3, 1.0) * 255);
        SetLayeredWindowAttributes(_hwnd, 0, alpha, LWA_ALPHA);
#endif
    }

    public void SetAlwaysOnTop(bool alwaysOnTop)
    {
#if WINDOWS
        if (_presenter is not null)
            _presenter.IsAlwaysOnTop = alwaysOnTop;
#endif
    }

    /// <summary>
    /// Initiates a native window drag from a PointerPressed event on the drag strip.
    /// ReleaseCapture drops MAUI's pointer capture so Windows can own the move loop.
    /// </summary>
    public void StartDrag()
    {
#if WINDOWS
        if (_hwnd == IntPtr.Zero) return;
        ReleaseCapture();
        SendMessage(_hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
#endif
    }

    /// <summary>
    /// Resizes the mini window height to fit the given number of provider rows.
    /// Width stays fixed. Call whenever Providers.Count changes.
    /// </summary>
    public void ResizeForProviderCount(int count)
    {
#if WINDOWS
        if (_appWindow is null) return;
        var height = HeaderHeightPx + PaddingPx + Math.Max(count, 1) * RowHeightPx;
        _appWindow.Resize(new Windows.Graphics.SizeInt32(WindowWidthPx, height));
#endif
    }

    // ── Main window management ────────────────────────────────────────────

    /// <summary>
    /// Stores the main window's AppWindow reference so it can be hidden/shown later.
    /// Call before OpenWindow(miniWindow).
    /// </summary>
    public void SetMainWindow(Window mainWindow)
    {
#if WINDOWS
        if (mainWindow.Handler?.PlatformView is null) return;
        _mainAppWindow = GetAppWindow(mainWindow);
#endif
    }

    /// <summary>Hides the main window (removes from taskbar, preserves all state).</summary>
    public void HideMainWindow()
    {
#if WINDOWS
        _mainAppWindow?.Hide();
#endif
    }

    /// <summary>Restores the main window.</summary>
    public void ShowMainWindow()
    {
#if WINDOWS
        _mainAppWindow?.Show();
#endif
    }

    // ── Settings window setup ─────────────────────────────────────────────

    /// <summary>
    /// Configures the settings window: small, always-on-top, positioned to the
    /// right of the mini window (falls back to default OS placement if unavailable).
    /// </summary>
    public void ConfigureSettingsWindow(Window settingsWindow)
    {
#if WINDOWS
        var native = settingsWindow.Handler?.PlatformView as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException("No native WinUI window for settings.");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(native);
        var id   = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var settingsAppWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);

        settingsAppWindow.Resize(new Windows.Graphics.SizeInt32(320, 240));

        if (_appWindow is not null)
        {
            var pos = _appWindow.Position;
            settingsAppWindow.Move(new Windows.Graphics.PointInt32(
                pos.X + WindowWidthPx + 8,
                pos.Y));
        }

        if (settingsAppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter sp)
        {
            sp.IsMaximizable = false;
            sp.IsMinimizable = false;
            sp.IsResizable   = false;
            sp.IsAlwaysOnTop = true;
        }
#endif
    }
}
