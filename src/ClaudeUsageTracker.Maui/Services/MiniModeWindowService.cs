namespace ClaudeUsageTracker.Maui.Services;

public class MiniModeWindowService
{
#if WINDOWS
    private IntPtr _hwnd;
    private Microsoft.UI.Windowing.AppWindow? _appWindow;
    private Microsoft.UI.Windowing.OverlappedPresenter? _presenter;
    private Microsoft.UI.Windowing.AppWindow? _mainAppWindow;
    private double _dpiScale    = 1.0;
    private double _osDpiScale  = 1.0;
    private bool   _dpiInitialized;

    // ── P/Invoke (only what's needed for WndProc subclass + DPI) ─────────
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr CallWindowProcW(IntPtr lpPrevWndFunc, IntPtr hWnd,
        uint Msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    // WndProc delegate — must be kept in a field to prevent GC collection while
    // the native callback is registered on the window.
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(
        System.Runtime.InteropServices.CallingConvention.StdCall)]
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private WndProcDelegate? _wndProc;
    private IntPtr           _oldWndProc;

    private static void SvcLog(string msg)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClaudeUsageTracker", "mini_debug.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} [MiniModeWindowService] {msg}\n");
        }
        catch { }
    }

    // ── Win32 constants ───────────────────────────────────────────────────
    private const int  GWL_WNDPROC      = -4;
    private const uint WM_NCHITTEST     = 0x0084;
    private const int  HTCAPTION        = 2;

    // ── Layout constants (logical pixels at 100 % DPI) ────────────────────
    // Actual XAML row breakdown:
    //   Grid col=* (name+refresh) = 36
    //   session row               = 18
    //   weekly row                = 18
    //   resets label              = 15
    //   divider                   = 1
    //   VerticalStackLayout spacing between items = 6×4 = 24
    //   VerticalStackLayout padding = 8×2 = 16
    // Total per provider row ≈ 128 px; use 130 for comfortable spacing.
    private const int RowHeightPx        = 130;
    private const int HeaderHeightPx     = 40;
    private const int ContainerPaddingPx = 16;  // matches VerticalStackLayout Padding="12,8"
    private const int WindowWidthPx      = 320; // half the previous 460 for a compact widget
    // Approximate right-side width occupied by the ⚙ Settings + ← Main buttons.
    // WM_NCHITTEST returns HTCLIENT here so the buttons still receive clicks.
    private const int ButtonsWidthPx     = 160;

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

    /// <summary>
    /// Configures the mini window as a frameless, always-on-top widget.
    /// Installs a WndProc hook so the header strip acts as a native drag caption.
    /// Returns the auto-detected OS DPI scale (used to initialise the DPI slider).
    /// </summary>
    public double ConfigureWindow(Window window, bool isAlwaysOnTop, double opacity)
    {
#if WINDOWS
        SvcLog($"ConfigureWindow start: opacity={opacity}");
        var native = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException("No native WinUI window.");
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(native);
        var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);
        SvcLog($"  _hwnd={_hwnd}, AppWindow obtained");

        // Auto-detect the physical OS DPI for this window (e.g. 144 = 150 %).
        // Only overwrite _dpiScale on the very first call — subsequent opens
        // preserve whatever the user set via the slider.
        _osDpiScale = GetDpiForWindow(_hwnd) / 96.0;
        if (!_dpiInitialized)
        {
            _dpiScale      = _osDpiScale;
            _dpiInitialized = true;
        }

        if (_appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
        {
            _presenter      = p;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.IsResizable   = false;
            p.IsAlwaysOnTop = isAlwaysOnTop;
        }

        // Extend MAUI content into the title bar area — this gives the frameless
        // look while keeping the WinUI3 compositor happy. Both SetBorderAndTitleBar
        // and Win32 WS_CAPTION removal cause black rendering in MAUI release builds
        // because the compositor loses track of the content area.
        var titleBar = _appWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.ButtonBackgroundColor       = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonHoverBackgroundColor  = Windows.UI.Color.FromArgb(30, 255, 255, 255);
        titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(50, 255, 255, 255);
        SvcLog("  presenter + titleBar configured (ExtendsContentIntoTitleBar)");

        // Subclass the WndProc so the top strip returns HTCAPTION from
        // WM_NCHITTEST, enabling immediate native drag without any MAUI event.
        _wndProc    = WndProcHook;
        var ptr     = System.Runtime.InteropServices.Marshal
                          .GetFunctionPointerForDelegate(_wndProc);
        var result  = SetWindowLongPtr(_hwnd, GWL_WNDPROC, ptr);
        SvcLog($"  WndProc subclass: result={result}");
        if (result == IntPtr.Zero)
        {
            _oldWndProc = IntPtr.Zero;
        }
        else
        {
            _oldWndProc = result;
        }

        // NOTE: WS_EX_LAYERED is intentionally NOT used here. Layered windows
        // (WS_EX_LAYERED + SetLayeredWindowAttributes) require per-pixel alpha
        // blending which is incompatible with MAUI's WinUI child window pipeline
        // in AOT release builds — the layered update bitmap captures a black frame
        // before MAUI content renders, resulting in a fully black window.
        // Opacity is controlled via MAUI's Window.Opacity property instead.
        SetOpacity(opacity);

        return _dpiScale;
#else
        return 1.0;
#endif
    }

#if WINDOWS
    /// <summary>
    /// WndProc hook: returns HTCAPTION for the left portion of the header strip
    /// so Windows moves the window natively on mouse-down — instant, no latency.
    /// The right ~160 px (button zone) falls through to HTCLIENT so clicks work.
    /// </summary>
    private IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // Guard: if subclassing failed (SetWindowLongPtr returned 0), fall through
        // to DefWindowProc so we don't crash the window.
        if (msg == WM_NCHITTEST && _oldWndProc != IntPtr.Zero)
        {
            // lParam = MAKELPARAM(screenX, screenY) — signed 16-bit each
            int   lp      = (int)(lParam.ToInt64() & 0xFFFFFFFF);
            short screenX = (short)(lp & 0xFFFF);
            short screenY = (short)((uint)lp >> 16);

            GetWindowRect(hWnd, out RECT rect);
            int relX = screenX - rect.Left;
            int relY = screenY - rect.Top;

            // Guard against uninitialized _osDpiScale (defaults to 1.0, but be safe)
            double dpiScale = _osDpiScale > 0 ? _osDpiScale : 1.0;
            int headerPx     = (int)(HeaderHeightPx   * dpiScale);
            int buttonAreaPx = (int)(ButtonsWidthPx    * dpiScale);
            int windowW      = rect.Right - rect.Left;

            if (relY >= 0 && relY < headerPx && relX >= 0 && relX < windowW - buttonAreaPx)
                return (IntPtr)HTCAPTION;
        }
        // Use DefWindowProc when subclassing was not applied (prevents crash on null proc).
        if (_oldWndProc == IntPtr.Zero)
            return DefWindowProcW(hWnd, msg, wParam, lParam);
        return CallWindowProcW(_oldWndProc, hWnd, msg, wParam, lParam);
    }
#endif

    public void SetOpacity(double opacity)
    {
        // Opacity via Win32 WS_EX_LAYERED is disabled because it causes black
        // rendering in AOT release builds (see ConfigureWindow notes). MAUI's
        // Window class does not expose an Opacity property, so this is a no-op
        // until a MAUI-native transparency mechanism is available.
    }

    public void SetAlwaysOnTop(bool alwaysOnTop)
    {
#if WINDOWS
        if (_presenter is not null)
            _presenter.IsAlwaysOnTop = alwaysOnTop;
#endif
    }

    public void SetDpiScale(double scale)
    {
#if WINDOWS
        _dpiScale = scale;
#endif
    }

    /// <summary>
    /// Resizes the mini window height to fit the given number of provider rows.
    /// Width stays fixed. Call whenever Providers.Count changes.
    /// </summary>
    public void ResizeForProviderCount(int count)
    {
#if WINDOWS
        if (_appWindow is null) { SvcLog($"ResizeForProviderCount: _appWindow is null, skipping"); return; }
        var w = (int)(WindowWidthPx * _dpiScale);
        var h = (int)((HeaderHeightPx + ContainerPaddingPx
                       + Math.Max(count, 1) * RowHeightPx) * _dpiScale);
        _appWindow.Resize(new Windows.Graphics.SizeInt32(w, h));
        SvcLog($"ResizeForProviderCount: count={count}, size={w}x{h}, dpiScale={_dpiScale}");
#endif
    }

    // ── Main window management ────────────────────────────────────────────

    public void SetMainWindow(Window mainWindow)
    {
#if WINDOWS
        if (mainWindow.Handler?.PlatformView is null) return;
        _mainAppWindow = GetAppWindow(mainWindow);
#endif
    }

    public void HideMainWindow()
    {
#if WINDOWS
        _mainAppWindow?.Hide();
#endif
    }

    public void ShowMainWindow()
    {
#if WINDOWS
        _mainAppWindow?.Show();
#endif
    }

    // ── Settings window setup ─────────────────────────────────────────────

    public void ConfigureSettingsWindow(Window settingsWindow)
    {
#if WINDOWS
        var native = settingsWindow.Handler?.PlatformView as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException("No native WinUI window for settings.");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(native);
        var id   = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var settingsAppWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);

        settingsAppWindow.Resize(new Windows.Graphics.SizeInt32(320, 290));

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
