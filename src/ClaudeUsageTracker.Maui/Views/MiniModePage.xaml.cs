using ClaudeUsageTracker.Maui.Services;
using ClaudeUsageTracker.Maui.ViewModels;

namespace ClaudeUsageTracker.Maui.Views;

public partial class MiniModePage : ContentPage
{
    private readonly MiniModeWindowService _windowService;
    private readonly MiniModeViewModel _vm;
    private Window? _settingsWindow;

    private bool _headerVisible = true;
    private CancellationTokenSource? _hideCts;

    public MiniModePage(MiniModeViewModel vm, MiniModeWindowService windowService)
    {
        try { InitializeComponent(); }
        catch (Exception ex)
        {
            var msg = $"[MiniModePage] InitializeComponent FAILED: {ex}";
            System.Diagnostics.Debug.WriteLine(msg);
            WriteLog(msg);
        }
        _vm = vm;
        _windowService = windowService;
        BindingContext = vm;
    }

    private static void WriteLog(string msg)
    {
        try
        {
            var path = Path.Combine(FileSystem.AppDataDirectory, "mini_debug.log");
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {msg}\n");
        }
        catch { }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.MiniProviders.CollectionChanged += OnMiniProvidersChanged;
        WriteLog("OnAppearing entered");

        if (TryConfigure())
        {
            WriteLog("TryConfigure succeeded");
            return;
        }

        WriteLog("TryConfigure deferred — subscribing to HandlerChanged");
        Window.HandlerChanged += OnWindowHandlerReady;
    }

    private bool TryConfigure()
    {
        try
        {
            WriteLog($"TryConfigure: Window={Window?.GetType().Name}, Handler={Window?.Handler?.GetType().Name}, PlatformView={Window?.Handler?.PlatformView?.GetType().Name}");
            if (Window?.Handler?.PlatformView is null)
            {
                WriteLog("TryConfigure: PlatformView is null, returning false");
                return false;
            }
            ConfigureAndResize();
            return true;
        }
        catch (Exception ex)
        {
            var msg = $"[MiniModePage] Configure failed: {ex}";
            System.Diagnostics.Debug.WriteLine(msg);
            WriteLog(msg);
            return false;
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.MiniProviders.CollectionChanged -= OnMiniProvidersChanged;
        _hideCts?.Cancel();
        _windowService.OnPointerEntered = null;
        _windowService.OnPointerExited  = null;
    }

    private void OnWindowHandlerReady(object? sender, EventArgs e)
    {
        Window.HandlerChanged -= OnWindowHandlerReady;
        WriteLog("OnWindowHandlerReady fired");
        if (!TryConfigure())
        {
            WriteLog("TryConfigure failed even after HandlerChanged — using fallback resize");
            _ = WaitForProvidersAndResizeAsync();
        }
        else
        {
            WriteLog("TryConfigure succeeded from HandlerChanged");
        }
    }

    private async Task WaitForProvidersAndResizeAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await Task.Run(async () =>
            {
                while (_vm.MiniProviders.Count == 0 && !cts.Token.IsCancellationRequested)
                    await Task.Delay(100, cts.Token);
            }, cts.Token);
        }
        catch (OperationCanceledException) { }

        _windowService.ResizeForProviderCount(_vm.MiniProviders.Count, _headerVisible, _vm.Dashboard.GoogleAiCard.ShowInMiniMode);
    }

    private void ConfigureAndResize()
    {
        try
        {
            WriteLog($"ConfigureAndResize: opacity={_vm.Opacity}, isAlwaysOnTop={_vm.IsAlwaysOnTop}, miniProviders={_vm.MiniProviders.Count}");
            _windowService.ConfigureWindow(Window!, _vm.IsAlwaysOnTop, _vm.Opacity);
            _windowService.OnPointerEntered = () => MainThread.BeginInvokeOnMainThread(OnWindowHovered);
            _windowService.OnPointerExited  = () => MainThread.BeginInvokeOnMainThread(StartHideTimer);
            _windowService.HideMainWindow();
            _windowService.ResizeForProviderCount(_vm.MiniProviders.Count, _headerVisible, _vm.Dashboard.GoogleAiCard.ShowInMiniMode);
            StartHideTimer();
            WriteLog("ConfigureAndResize: completed successfully");
        }
        catch (Exception ex)
        {
            var msg = $"[MiniModePage] ConfigureAndResize FAILED: {ex}";
            System.Diagnostics.Debug.WriteLine(msg);
            WriteLog(msg);
        }
    }

    private void OnWindowHovered()
    {
        _hideCts?.Cancel();
        ShowHeader();
    }

    private void StartHideTimer()
    {
        _hideCts?.Cancel();
        _hideCts = new CancellationTokenSource();
        var cts = _hideCts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2000, cts.Token);
                MainThread.BeginInvokeOnMainThread(HideHeader);
            }
            catch (OperationCanceledException) { }
        });
    }

    private void ShowHeader()
    {
        if (_headerVisible) return;
        _headerVisible = true;
        DragStrip.IsVisible = true;
        RootGrid.RowDefinitions[0].Height = new GridLength(40);
        _windowService.ResizeForProviderCount(_vm.MiniProviders.Count, headerVisible: true, _vm.Dashboard.GoogleAiCard.ShowInMiniMode);
    }

    private void HideHeader()
    {
        if (!_headerVisible) return;
        _headerVisible = false;
        DragStrip.IsVisible = false;
        RootGrid.RowDefinitions[0].Height = new GridLength(0);
        _windowService.ResizeForProviderCount(_vm.MiniProviders.Count, headerVisible: false, _vm.Dashboard.GoogleAiCard.ShowInMiniMode);
    }

    private void OnMiniProvidersChanged(object? sender,
        System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        _windowService.ResizeForProviderCount(_vm.MiniProviders.Count, _headerVisible, _vm.Dashboard.GoogleAiCard.ShowInMiniMode);
    }

    private void OnSettingsClicked(object sender, EventArgs e)
    {
        try
        {
            if (_settingsWindow != null)
            {
                Application.Current!.CloseWindow(_settingsWindow);
                _settingsWindow = null;
                return;
            }

            WriteLog("OnSettingsClicked: creating MiniModeSettingsPage...");
            var settingsPage = new MiniModeSettingsPage(_vm, _windowService);
            WriteLog("OnSettingsClicked: page created OK, opening window...");
            _settingsWindow = new Window(settingsPage) { Title = "Mini Mode Settings" };
            _settingsWindow.Destroying += (_, _) => _settingsWindow = null;
            Application.Current!.OpenWindow(_settingsWindow);
            WriteLog("OnSettingsClicked: window opened OK");
        }
        catch (Exception ex)
        {
            WriteLog($"OnSettingsClicked CRASHED: {ex}");
        }
    }

    private void OnReturnToMainClicked(object sender, EventArgs e)
    {
        if (_settingsWindow != null)
            Application.Current!.CloseWindow(_settingsWindow);
        _windowService.ShowMainWindow();
        Application.Current!.CloseWindow(Window!);
    }
}
