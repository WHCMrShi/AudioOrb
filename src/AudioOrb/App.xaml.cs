using AudioOrb.Interop;
using AudioOrb.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace AudioOrb;

public partial class App : Application
{
    private static long _lastOpenSettingsRequest;

    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private bool _startupLaunch;
    private TrayIcon? _tray;

    public App()
    {
        InitializeComponent();
        AppLog.Info("声枢启动。");
        UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    public static uint OpenSettingsMessageId { get; } =
        NativeMethods.RegisterWindowMessage("AudioOrb.OpenSettings");

    public AppHost Host { get; private set; } = null!;

    public MainWindow? Ball { get; private set; }

    public SettingsWindow? SettingsWindow { get; private set; }

    public bool IsExiting { get; private set; }

    internal TrayIcon? Tray => _tray;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var commandLine = Environment.GetCommandLineArgs().Skip(1).ToArray();
        _startupLaunch = commandLine.Any(
            x => string.Equals(x, "--startup", StringComparison.OrdinalIgnoreCase));

        _singleInstanceMutex = new Mutex(initiallyOwned: false, @"Local\AudioOrb.SingleInstance.v1");
        try
        {
            _ownsMutex = _singleInstanceMutex.WaitOne(0, exitContext: false);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
        }

        if (!_ownsMutex)
        {
            if (!_startupLaunch)
            {
                BroadcastOpenSettings();
            }

            ExitProcess();
            return;
        }

        try
        {
            Host = new AppHost(DispatcherQueue.GetForCurrentThread());
            Ball = new MainWindow(Host);
            Ball.Activate();

            await Host.InitializeAsync();

            Host.SettingsChanged += Host_SettingsChanged;
            EnsureTray();

            AppLog.Info(
                $"初始化完成：{Host.Endpoints.Count} 台输出设备，当前 {Host.CurrentDefault?.Name ?? "(无)"}。");

            if (!_startupLaunch && !Host.Settings.HasCompletedFirstRun)
            {
                await Task.Delay(500);
                OpenSettings();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("应用启动失败。", ex);
            NativeMethods.MessageBox(
                IntPtr.Zero,
                $"声枢启动失败：{ex.Message}\n\n详细信息已写入日志：\n{AppLog.FilePath}",
                "声枢",
                0x10);
            ExitApp();
        }
    }

    /// <summary>
    /// Called by the ball window when another process broadcasts the
    /// open-settings message. The sender repeats the message while waiting for
    /// the first instance to finish starting up, so deduplicate the burst.
    /// </summary>
    public void RequestOpenSettingsFromOtherInstance()
    {
        var now = Environment.TickCount64;
        var previous = Interlocked.Read(ref _lastOpenSettingsRequest);
        if (now - previous < 1500)
        {
            return;
        }

        Interlocked.Exchange(ref _lastOpenSettingsRequest, now);
        OpenSettings();
    }

    public void OpenSettings()
    {
        if (IsExiting || Host is null)
        {
            return;
        }

        if (SettingsWindow is null)
        {
            SettingsWindow = new SettingsWindow(Host);
            SettingsWindow.Hidden += SettingsWindow_Hidden;
            SettingsWindow.Closed += SettingsWindow_Closed;
        }

        SettingsWindow.ShowAndActivate();
    }

    public void ResetBallPosition()
    {
        Ball?.ResetBallPosition();
    }

    public void ExitApp()
    {
        if (IsExiting)
        {
            return;
        }

        IsExiting = true;
        AppLog.Info("声枢退出。");

        try
        {
            _tray?.Dispose();
            _tray = null;
        }
        catch (Exception ex)
        {
            AppLog.Warning($"移除托盘图标失败：{ex.Message}");
        }

        try
        {
            SettingsWindow?.AllowClose();
            SettingsWindow?.Close();
        }
        catch (Exception ex)
        {
            AppLog.Warning($"关闭设置窗口失败：{ex.Message}");
        }

        try
        {
            Ball?.Close();
        }
        catch (Exception ex)
        {
            AppLog.Warning($"关闭桌面组件失败：{ex.Message}");
        }

        try
        {
            Host?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Warning($"释放服务失败：{ex.Message}");
        }

        ReleaseMutex();
        Application.Current.Exit();
        _ = Task.Delay(600).ContinueWith(_ => Environment.Exit(0));
    }

    private void Host_SettingsChanged(object? sender, EventArgs e)
    {
        EnsureTray();
    }

    private void EnsureTray()
    {
        if (Host is null || IsExiting)
        {
            return;
        }

        if (Host.Settings.ShowTrayIcon)
        {
            if (_tray is null)
            {
                try
                {
                    _tray = new TrayIcon(this);
                }
                catch (Exception ex)
                {
                    AppLog.Error("创建系统托盘图标失败。", ex);
                }
            }
        }
        else if (_tray is not null)
        {
            _tray.Dispose();
            _tray = null;
        }
    }

    private void SettingsWindow_Hidden(object? sender, EventArgs e)
    {
        if (Host is not null && !Host.Settings.HasCompletedFirstRun)
        {
            Host.Settings.HasCompletedFirstRun = true;
            Host.SaveSettings();
        }
    }

    private void SettingsWindow_Closed(object sender, WindowEventArgs args)
    {
        if (ReferenceEquals(SettingsWindow, sender))
        {
            SettingsWindow = null;
        }
    }

    private static void BroadcastOpenSettings()
    {
        if (OpenSettingsMessageId == 0)
        {
            return;
        }

        for (var i = 0; i < 12; i++)
        {
            // HWND_BROADCAST is unreliable for the app's tool windows, so
            // enumerate and post directly instead.
            var callback = new NativeMethods.EnumWindowsProc((hWnd, _) =>
            {
                NativeMethods.PostMessage(hWnd, OpenSettingsMessageId, IntPtr.Zero, IntPtr.Zero);
                return true;
            });

            NativeMethods.EnumWindows(callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            Thread.Sleep(90);
        }
    }

    private void ExitProcess()
    {
        ReleaseMutex();
        Environment.Exit(0);
    }

    private void ReleaseMutex()
    {
        if (_singleInstanceMutex is null)
        {
            return;
        }

        if (_ownsMutex)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _ownsMutex = false;
        }

        _singleInstanceMutex.Dispose();
        _singleInstanceMutex = null;
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        AppLog.Error("未处理的 XAML 异常。", e.Exception);
    }

    private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        AppLog.Error("未处理的应用程序域异常。", e.ExceptionObject as Exception);
    }
}
