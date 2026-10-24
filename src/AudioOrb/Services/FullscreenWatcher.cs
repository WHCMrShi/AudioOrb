using System.Text;
using AudioOrb.Interop;
using Microsoft.UI.Dispatching;

namespace AudioOrb.Services;

/// <summary>
/// Watches foreground-window changes with a WinEvent hook and reports whether a
/// true fullscreen application is currently in front. No polling timer is used.
/// </summary>
public sealed class FullscreenWatcher : IDisposable
{
    private static readonly HashSet<string> ShellWindowClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow",
        "ApplicationFrameWindow",
    };

    private readonly DispatcherQueue _dispatcher;
    private readonly NativeMethods.WinEventDelegate _callback;
    private readonly DispatcherQueueTimer _settleTimer;
    private IntPtr _hook;
    private bool _enabled;
    private bool _disposed;
    private bool _lastState;

    public FullscreenWatcher(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _callback = OnWinEvent;

        _settleTimer = dispatcher.CreateTimer();
        _settleTimer.Interval = TimeSpan.FromMilliseconds(250);
        _settleTimer.IsRepeating = false;
        _settleTimer.Tick += (_, _) => EvaluateAndNotify();
    }

    public event EventHandler<bool>? FullscreenChanged;

    public bool IsFullscreen { get; private set; }

    public void SetEnabled(bool enabled)
    {
        if (_disposed || _enabled == enabled)
        {
            return;
        }

        _enabled = enabled;
        if (enabled)
        {
            _hook = NativeMethods.SetWinEventHook(
                NativeMethods.EventSystemForeground,
                NativeMethods.EventSystemForeground,
                IntPtr.Zero,
                _callback,
                0,
                0,
                NativeMethods.WineventOutOfContext);

            AppLog.Info($"全屏监视已启用，hook=0x{_hook.ToInt64():X}。");
            EvaluateAndNotify();
        }
        else
        {
            if (_hook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_hook);
                _hook = IntPtr.Zero;
            }

            SetState(false);
        }
    }

    /// <summary>Re-check after the setting changes or the app is restored.</summary>
    public void Reevaluate()
    {
        if (_enabled)
        {
            EvaluateAndNotify();
        }
    }

    private void OnWinEvent(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        // Keep the hook callback tiny. The actual Win32 checks happen on the UI
        // dispatcher where the resulting window hide/show is safe.
        _dispatcher.TryEnqueue(EvaluateAndNotify);

        // A game often becomes foreground before its fullscreen window is
        // visible or resized. Check once more after it has settled; this is a
        // one-shot timer per foreground change, not a polling loop.
        _dispatcher.TryEnqueue(() =>
        {
            _settleTimer.Stop();
            _settleTimer.Start();
        });
    }

    private void EvaluateAndNotify()
    {
        if (!_enabled || _disposed)
        {
            return;
        }

        SetState(IsForegroundWindowFullscreen());
    }

    private void SetState(bool value)
    {
        if (_lastState == value)
        {
            return;
        }

        _lastState = value;
        IsFullscreen = value;
        AppLog.Info($"全屏状态变更为：{value}。");

        try
        {
            FullscreenChanged?.Invoke(this, value);
        }
        catch
        {
            // Never let a UI exception escape the WinEvent callback.
        }
    }

    private static bool IsForegroundWindowFullscreen()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        var shellWindow = NativeMethods.GetShellWindow();
        var visible = NativeMethods.IsWindowVisible(hwnd);
        var iconic = NativeMethods.IsIconic(hwnd);
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        var className = new StringBuilder(128);
        NativeMethods.GetClassName(hwnd, className, className.Capacity);

        if (hwnd == IntPtr.Zero || hwnd == shellWindow || !visible || iconic)
        {
            return false;
        }

        if (processId == (uint)Environment.ProcessId)
        {
            return false;
        }

        if (ShellWindowClasses.Contains(className.ToString()))
        {
            return false;
        }

        if (NativeMethods.DwmGetWindowAttribute(
                hwnd,
                NativeMethods.DwmwaCloaked,
                out var cloaked,
                sizeof(int)) == 0 &&
            cloaked != 0)
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var windowRect))
        {
            AppLog.Info($"全屏诊断：GetWindowRect 失败，hwnd=0x{hwnd.ToInt64():X}。");
            return false;
        }

        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            AppLog.Info($"全屏诊断：MonitorFromWindow 失败，hwnd=0x{hwnd.ToInt64():X}。");
            return false;
        }

        var info = new NativeMethods.MonitorInfo { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            AppLog.Info($"全屏诊断：GetMonitorInfo 失败，monitor=0x{monitor.ToInt64():X}。");
            return false;
        }

        const int tolerance = 2;
        return windowRect.Left <= info.rcMonitor.Left + tolerance &&
               windowRect.Top <= info.rcMonitor.Top + tolerance &&
               windowRect.Right >= info.rcMonitor.Right - tolerance &&
               windowRect.Bottom >= info.rcMonitor.Bottom - tolerance;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _enabled = false;
        _settleTimer.Stop();

        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
