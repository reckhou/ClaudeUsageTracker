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

    // ── P/Invoke ─────────────────────────────────────────────────────────
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    // ── Manual drag state ────────────────────────────────────────────────
    private bool _isDragging;
    private int  _dragStartCursorX, _dragStartCursorY;
    private int  _dragStartWinX,    _dragStartWinY;

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

    // ── Layout constants (logical pixels at 100 % DPI) ────────────────────
    // Per-provider row: name+refresh (36) + session (18) + weekly (18) +
    //   resets label (15) + divider (1) + spacing (6×4=24) + padding (8×2=16) ≈ 128 → 130
    private const int RowHeightPx        = 130;
    private const int HeaderHeightPx     = 40;
    private const int ContainerPaddingPx = 16;  // matches VerticalStackLayout Padding="12,8"
    private const int WindowWidthPx      = 320;

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

        // Step 1: ExtendsContentIntoTitleBar lets the compositor render MAUI
        // content correctly. Without this, removing the border causes a
        // permanent black frame in release builds.
        var titleBar = _appWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Collapsed;
        SvcLog("  titleBar extended + collapsed");

        // Step 2: Defer border removal to the next dispatcher tick so the
        // compositor has already rendered at least one frame of MAUI content.
        // Calling SetBorderAndTitleBar immediately causes a black window.
        native.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (_presenter is not null)
                    _presenter.SetBorderAndTitleBar(false, false);
                SvcLog("  deferred SetBorderAndTitleBar(false, false) applied");
            }
            catch (Exception ex) { SvcLog($"  deferred border removal failed: {ex.Message}"); }
        });

        // Step 3: Manual drag via WinUI3 native pointer events.
        // We hook Pressed/Moved/Released on the root content element.
        // Button clicks are handled by their own controls first (marking
        // the event as Handled), so PointerPressed only bubbles to the
        // root for non-interactive areas — drag doesn't steal button clicks.
        if (native.Content is Microsoft.UI.Xaml.UIElement rootContent)
        {
            rootContent.PointerPressed  += OnNativePointerPressed;
            rootContent.PointerMoved    += OnNativePointerMoved;
            rootContent.PointerReleased += OnNativePointerReleased;
            SvcLog("  native pointer events hooked for drag");
        }

        SetOpacity(opacity);

        return _dpiScale;
#else
        return 1.0;
#endif
    }

    private void OnNativePointerPressed(object sender,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (e.Handled || _appWindow is null) return;
        var pt = e.GetCurrentPoint(null);
        if (!pt.Properties.IsLeftButtonPressed) return;

        GetCursorPos(out var cursor);
        var winPos       = _appWindow.Position;
        _dragStartCursorX = cursor.X;
        _dragStartCursorY = cursor.Y;
        _dragStartWinX    = winPos.X;
        _dragStartWinY    = winPos.Y;
        _isDragging       = true;

        ((Microsoft.UI.Xaml.UIElement)sender).CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnNativePointerMoved(object sender,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isDragging || _appWindow is null) return;

        GetCursorPos(out var cursor);
        _appWindow.Move(new Windows.Graphics.PointInt32(
            _dragStartWinX + cursor.X - _dragStartCursorX,
            _dragStartWinY + cursor.Y - _dragStartCursorY));
    }

    private void OnNativePointerReleased(object sender,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        ((Microsoft.UI.Xaml.UIElement)sender).ReleasePointerCapture(e.Pointer);
    }

    public void SetOpacity(double opacity)
    {
        // No-op — see ConfigureWindow notes about WS_EX_LAYERED.
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
