namespace ClaudeUsageTracker.Maui.Services;

public class MiniModeWindowService
{
#if WINDOWS
    private IntPtr _hwnd;
    private Microsoft.UI.Windowing.OverlappedPresenter? _presenter;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    private const int GWL_EXSTYLE   = -20;
    private const int WS_EX_LAYERED = 0x00080000;
    private const uint LWA_ALPHA    = 0x00000002;
#endif

    public void ConfigureWindow(Window window, bool isAlwaysOnTop, double opacity)
    {
#if WINDOWS
        var nativeWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException("No native WinUI window.");

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

        appWindow.Resize(new Windows.Graphics.SizeInt32(460, 260));

        if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            _presenter = presenter;
            presenter.IsMaximizable = false;
            presenter.IsAlwaysOnTop = isAlwaysOnTop;
        }

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
}
