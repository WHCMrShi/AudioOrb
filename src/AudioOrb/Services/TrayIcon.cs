using System.Runtime.InteropServices;
using System.Text;
using AudioOrb.Interop;
using Microsoft.Win32;

namespace AudioOrb.Services;

/// <summary>
/// Win32 notification-area icon. WinUI 3 has no tray-icon primitive, so this
/// uses a message-only window and Shell_NotifyIcon. The context menu is a
/// native menu, which gets Windows 11's system Acrylic menu treatment.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint TrayCallbackMessage = 0x8000 + 0x31;
    private const uint TrayIconId = 1;
    private const int WmNull = 0x0000;
    private const int WmContextMenu = 0x007B;
    private const int WmLButtonUp = 0x0202;
    private const int WmLButtonDoubleClick = 0x0203;
    private const int WmRButtonUp = 0x0205;

    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifShowTip = 0x00000080;
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;

    private const uint MfString = 0x00000000;
    private const uint MfChecked = 0x00000008;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmNoNotify = 0x0080;
    private const uint TpmReturnCmd = 0x0100;

    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x0010;
    private const uint LrDefaultSize = 0x0040;
    private const uint WsPopup = 0x80000000;
    private const long WsExToolWindow = 0x00000080;
    private const int SwShowNoActivate = 4;

    private const uint CmdSettings = 2;
    private const uint CmdStartup = 4;
    private const uint CmdExit = 5;

    private readonly App _app;
    private readonly IntPtr _hwnd;
    private readonly uint _taskbarCreated;
    private readonly NativeMethods.SubclassProc _windowProc;
    private NotifyIconData _data;
    private IntPtr _icon;
    private bool _iconOwned;
    private bool _disposed;

    public TrayIcon(App app)
    {
        _app = app;
        _windowProc = WindowProc;
        _taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        // A hidden top-level window, not a message-only window: the menu needs
        // a real foreground owner or TrackPopupMenuEx cannot receive clicks.
        _hwnd = CreateWindowEx(
            WsExToolWindow,
            "STATIC",
            "AudioOrb.Tray",
            WsPopup,
            -32000,
            -32000,
            1,
            1,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.GetModuleHandle(null),
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            AppLog.Warning("无法创建托盘窗口。");
            return;
        }

        ShowWindow(_hwnd, SwShowNoActivate);
        WindowEffects.SetDarkMode(_hwnd, ThemeService.IsDark(app.Host.Settings.Theme));
        NativeMethods.SetWindowSubclass(_hwnd, _windowProc, (UIntPtr)TrayIconId, IntPtr.Zero);
        _icon = LoadApplicationIcon();
        _data = new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = _hwnd,
            uID = TrayIconId,
            uFlags = NifMessage | NifIcon | NifTip | NifShowTip,
            uCallbackMessage = TrayCallbackMessage,
            hIcon = _icon,
            szTip = TooltipText(),
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
            guidItem = Guid.Empty,
        };

        AddIcon();
        app.Host.DevicesUpdated += Host_DevicesUpdated;
        app.Host.SettingsChanged += Host_SettingsChanged;
    }

    public void UpdateTooltip()
    {
        if (_disposed || _hwnd == IntPtr.Zero)
        {
            return;
        }

        _data.uFlags = NifTip | NifShowTip;
        _data.szTip = TooltipText();
        Shell_NotifyIcon(NimModify, ref _data);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _app.Host.DevicesUpdated -= Host_DevicesUpdated;
            _app.Host.SettingsChanged -= Host_SettingsChanged;
        }
        catch
        {
            // Host may already be disposed during shutdown.
        }

        if (_hwnd != IntPtr.Zero)
        {
            Shell_NotifyIcon(NimDelete, ref _data);
            NativeMethods.RemoveWindowSubclass(_hwnd, _windowProc, (UIntPtr)TrayIconId);
            NativeMethods.DestroyWindow(_hwnd);
        }

        if (_iconOwned && _icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
        }

        _icon = IntPtr.Zero;
    }

    private void AddIcon()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        _data.uFlags = NifMessage | NifIcon | NifTip | NifShowTip;
        _data.hIcon = _icon;
        _data.szTip = TooltipText();
        if (!Shell_NotifyIcon(NimAdd, ref _data))
        {
            AppLog.Warning("无法添加系统托盘图标。");
        }
        else
        {
            PromoteIcon();
        }
    }

    private void PromoteIcon()
    {
        // Windows 11 puts new notification icons in the overflow flyout by
        // default. The shell stores each icon's state in NotifyIconSettings;
        // promoting our own entry keeps the icon in the visible tray area.
        try
        {
            var target = Path.GetFullPath(AppHost.ExecutablePath);
            using var baseKey = Registry.CurrentUser.OpenSubKey(
                @"Control Panel\NotifyIconSettings",
                writable: true);
            if (baseKey is null)
            {
                return;
            }

            foreach (var name in baseKey.GetSubKeyNames())
            {
                using var item = baseKey.OpenSubKey(name, writable: true);
                if (item?.GetValue("ExecutablePath") is not string executable)
                {
                    continue;
                }

                if (!string.Equals(
                        Path.GetFullPath(executable),
                        target,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                item.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warning($"无法固定托盘图标：{ex.Message}");
        }
    }

    private string TooltipText()
    {
        var current = _app.Host?.CurrentDefault?.Name;
        var text = string.IsNullOrWhiteSpace(current)
            ? "声枢 · 单击打开设置"
            : $"声枢 · {current}";
        return text.Length <= 127 ? text : text[..127];
    }

    private void Host_DevicesUpdated(object? sender, EventArgs e) => UpdateTooltip();

    private void Host_SettingsChanged(object? sender, EventArgs e) => UpdateTooltip();

    private IntPtr WindowProc(
        IntPtr hWnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr subclassId,
        IntPtr refData)
    {
        if (message == TrayCallbackMessage)
        {
            var mouseMessage = (uint)(lParam.ToInt64() & 0xFFFF);
            switch (mouseMessage)
            {
                case WmLButtonUp:
                case WmLButtonDoubleClick:
                    _app.OpenSettings();
                    return IntPtr.Zero;

                case WmRButtonUp:
                case WmContextMenu:
                    ShowMenu();
                    return IntPtr.Zero;
            }

            return IntPtr.Zero;
        }

        if (message == _taskbarCreated)
        {
            AddIcon();
            return IntPtr.Zero;
        }

        return NativeMethods.DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        var dark = ThemeService.IsDark(_app.Host.Settings.Theme);
        WindowEffects.SetDarkMode(_hwnd, dark);
        SetWindowTheme(_hwnd, dark ? "DarkMode_Explorer" : "Explorer", null);

        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenu(menu, MfString, CmdSettings, "设置");
            AppendMenu(
                menu,
                MfString | (_app.Host.Settings.LaunchAtStartup ? MfChecked : 0),
                CmdStartup,
                "开机启动");
            AppendMenu(menu, MfString, CmdExit, "退出声枢");

            // A fully offscreen owner cannot reliably become foreground, which
            // makes a native popup menu visible but non-interactive. Move the
            // 1x1 owner onto the desktop while the menu is open.
            NativeMethods.SetWindowPos(
                _hwnd,
                IntPtr.Zero,
                0,
                0,
                1,
                1,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
            WindowEffects.ForceForeground(_hwnd);
            var command = TrackPopupMenuEx(
                menu,
                TpmReturnCmd | TpmRightButton | TpmNoNotify,
                cursor.X,
                cursor.Y,
                _hwnd,
                IntPtr.Zero);
            NativeMethods.PostMessage(_hwnd, WmNull, IntPtr.Zero, IntPtr.Zero);
            NativeMethods.SetWindowPos(
                _hwnd,
                IntPtr.Zero,
                -32000,
                -32000,
                1,
                1,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);

            switch (command)
            {
                case CmdSettings:
                    _app.OpenSettings();
                    break;
                case CmdStartup:
                    if (!_app.Host.SetLaunchAtStartup(!_app.Host.Settings.LaunchAtStartup, out var error))
                    {
                        AppLog.Warning($"托盘菜单切换开机启动失败：{error}");
                    }
                    break;
                case CmdExit:
                    _app.ExitApp();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private IntPtr LoadApplicationIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "App.ico");
        if (File.Exists(path))
        {
            var icon = LoadImage(
                IntPtr.Zero,
                path,
                ImageIcon,
                0,
                0,
                LrLoadFromFile | LrDefaultSize);
            if (icon != IntPtr.Zero)
            {
                _iconOwned = true;
                return icon;
            }
        }

        return LoadIcon(IntPtr.Zero, new IntPtr(32512));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        long exStyle,
        string className,
        string windowName,
        long style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(
        IntPtr instance,
        string name,
        uint type,
        int cx,
        int cy,
        uint load);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr name);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, uint item, string? text);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPosition);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        IntPtr owner,
        IntPtr parameters);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? subAppName, string? subIdList);
}
