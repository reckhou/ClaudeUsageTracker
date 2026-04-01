namespace ClaudeUsageTracker.Maui.Services;

public class MiniModeWindowService
{
#if WINDOWS
    private IntPtr _hwnd;
    private Microsoft.UI.Windowing.AppWindow? _appWindow;
    private Microsoft.UI.Windowing.OverlappedPresenter? _presenter;
    private Microsoft.UI.Windowing.AppWindow? _mainAppWindow;
    private double _osDpiScale = 1.0;

    // ── P/Invoke ─────────────────────────────────────────────────────────
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    private const uint SWP_NOMOVE       = 0x0002;
    private const uint SWP_NOSIZE       = 0x0001;
    private const uint SWP_NOZORDER     = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MARGINS { public int Left; public int Right; public int Top; public int Bottom; }

    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_COLOR_NONE   = unchecked((int)0xFFFFFFFE);

    private const int GWL_EXSTYLE   = -20;
    private const int WS_EX_LAYERED = 0x00080000;
    private const uint LWA_ALPHA    = 0x2;

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    // ── Hover callbacks (used by MiniModePage for auto-hide header) ──────
    public Action? OnPointerEntered { get; set; }
    public Action? OnPointerExited  { get; set; }

    // ── Header visibility tracking (for Y-position compensation) ─────────
    private bool _lastHeaderVisible = true;

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
    private const int RowHeightPx        = 130;
    private const int HeaderHeightPx     = 40;
    private const int ContainerPaddingPx = 16;
    private const int WindowWidthPx      = 320;
    private const int GoogleAiRowPx      = 80; // Google AI mini row height

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
    /// </summary>
    public void ConfigureWindow(Window window, bool isAlwaysOnTop, double opacity)
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

        if (_appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
        {
            _presenter      = p;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.IsResizable   = false;
            p.IsAlwaysOnTop = isAlwaysOnTop;
        }

        var titleBar = _appWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Collapsed;
        SvcLog("  titleBar extended + collapsed");

        native.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (_presenter is not null)
                    _presenter.SetBorderAndTitleBar(false, false);
                SvcLog("  deferred SetBorderAndTitleBar(false, false) applied");

                // Remove all border artifacts:
                // 1. DWM border color → NONE
                var noBorder = DWMWA_COLOR_NONE;
                DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, ref noBorder, sizeof(int));
                // 2. Extend frame into client area
                var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(_hwnd, ref margins);
                // 3. Strip WS_CAPTION and WS_THICKFRAME from the Win32 style
                const int GWL_STYLE      = -16;
                const int WS_CAPTION     = 0x00C00000;
                const int WS_THICKFRAME  = 0x00040000;
                var style = GetWindowLong(_hwnd, GWL_STYLE);
                SetWindowLong(_hwnd, GWL_STYLE, style & ~(WS_CAPTION | WS_THICKFRAME));
                // 4. Force Windows to recalculate the frame
                SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
                SvcLog("  deferred border removal complete (DWM + Win32 + FRAMECHANGED)");
            }
            catch (Exception ex) { SvcLog($"  deferred border removal failed: {ex.Message}"); }
        });

        if (native.Content is Microsoft.UI.Xaml.UIElement rootContent)
        {
            rootContent.PointerPressed  += OnNativePointerPressed;
            rootContent.PointerMoved    += OnNativePointerMoved;
            rootContent.PointerReleased += OnNativePointerReleased;
            rootContent.PointerEntered  += OnNativePointerEntered;
            rootContent.PointerExited   += OnNativePointerExited;
            SvcLog("  native pointer events hooked for drag + hover");
        }

        SetOpacity(opacity);
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

    private void OnNativePointerEntered(object sender,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => OnPointerEntered?.Invoke();

    private void OnNativePointerExited(object sender,
        Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => OnPointerExited?.Invoke();

    public void SetOpacity(double opacity)
    {
#if WINDOWS
        if (_hwnd == IntPtr.Zero) return;
        var exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        if (opacity >= 1.0)
        {
            // Remove layered style when fully opaque — cleaner compositing
            SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle & ~WS_EX_LAYERED);
        }
        else
        {
            // Add layered style and apply alpha. Safe to call after content renders;
            // only called from the VM when the user adjusts the slider.
            SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED);
            SetLayeredWindowAttributes(_hwnd, 0, (byte)(opacity * 255), LWA_ALPHA);
        }
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
    /// Resizes using the last-known header visibility — safe to call from any context
    /// that doesn't know the current header state (e.g. the ViewModel).
    /// </summary>
    public void ResizeForProviderCount(int count, bool includeGoogleAi) =>
        ResizeForProviderCount(count, _lastHeaderVisible, includeGoogleAi);

    /// <summary>
    /// Resizes using the last-known header visibility — safe to call from any context
    /// that doesn't know the current header state (e.g. the ViewModel).
    /// </summary>
    public void ResizeForProviderCount(int count) =>
        ResizeForProviderCount(count, _lastHeaderVisible, false);

    /// <summary>
    /// Resizes the mini window height to fit the given number of provider rows.
    /// Width stays fixed. Also shifts Y when header visibility changes so content stays put.
    /// </summary>
    public void ResizeForProviderCount(int count, bool headerVisible, bool includeGoogleAi)
    {
#if WINDOWS
        if (_appWindow is null) { SvcLog($"ResizeForProviderCount: _appWindow is null, skipping"); return; }

        var headerPx = (int)Math.Round(HeaderHeightPx * _osDpiScale);
        var w        = (int)Math.Round(WindowWidthPx  * _osDpiScale);
        var headerH  = headerVisible ? HeaderHeightPx : 0;
        var googleAiH = includeGoogleAi ? GoogleAiRowPx : 0;
        var h        = (int)Math.Round((headerH + ContainerPaddingPx
                                        + Math.Max(count, 1) * RowHeightPx
                                        + googleAiH) * _osDpiScale);

        var pos  = _appWindow.Position;
        var newX = pos.X;
        // Shift Y so the provider-content rows stay at the same screen position:
        //   hiding  (true→false): top shrinks inward  → move window DOWN by header height
        //   showing (false→true): top grows outward   → move window UP   by header height
        var newY = pos.Y + (headerVisible == _lastHeaderVisible ? 0
                            : headerVisible ? -headerPx : headerPx);

        if (headerVisible != _lastHeaderVisible)
            _lastHeaderVisible = headerVisible;

        // SWP_NOZORDER (0x0004) | SWP_NOACTIVATE (0x0010) — atomic resize + move, no z-order change
        SetWindowPos(_hwnd, IntPtr.Zero, newX, newY, w, h, SWP_NOZORDER | 0x0010);
        SvcLog($"ResizeForProviderCount: count={count}, headerVisible={headerVisible}, includeGoogleAi={includeGoogleAi}, pos=({newX},{newY}), size={w}x{h}, dpi={_osDpiScale}");
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
