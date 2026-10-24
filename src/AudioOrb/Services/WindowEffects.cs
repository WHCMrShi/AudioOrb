using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using AudioOrb.Interop;

namespace AudioOrb.Services;

internal static class WindowEffects
{
    public static void ApplyWidgetWindowStyle(IntPtr hwnd, bool alwaysOnTop)
    {
        // A normal (non-layered) popup so DWM can render the acrylic backdrop
        // and antialiased rounded corners like the Start menu.
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlStyle).ToInt64();
        style &= ~(NativeMethods.WsCaption |
                   NativeMethods.WsThickFrame |
                   NativeMethods.WsDlgFrame |
                   NativeMethods.WsSysMenu |
                   NativeMethods.WsMinimizeBox |
                   NativeMethods.WsMaximizeBox |
                   NativeMethods.WsBorder);
        style |= NativeMethods.WsPopup | NativeMethods.WsVisible;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlStyle, new IntPtr(style));

        var extendedStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        // A layered window is required for reliable WS_EX_TOPMOST on this
        // borderless WinUI surface. The opacity is applied separately.
        extendedStyle |= NativeMethods.WsExLayered | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        if (alwaysOnTop)
        {
            extendedStyle |= NativeMethods.WsExTopmost;
        }
        else
        {
            extendedStyle &= ~NativeMethods.WsExTopmost;
        }

        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, new IntPtr(extendedStyle));

        var corner = NativeMethods.DwmwcpRound;
        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DwmwaWindowCornerPreference,
            ref corner,
            sizeof(int));

        var border = NativeMethods.DwmColorNone;
        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DwmwaBorderColor,
            ref border,
            sizeof(int));

        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove |
            NativeMethods.SwpNoSize |
            NativeMethods.SwpNoZOrder |
            NativeMethods.SwpNoActivate |
            NativeMethods.SwpFrameChanged);

        SetTopmost(hwnd, alwaysOnTop);
    }

    public static void ApplyDesktopWidgetStyle(IntPtr hwnd)
    {
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlStyle).ToInt64();
        style &= ~(NativeMethods.WsCaption |
                   NativeMethods.WsThickFrame |
                   NativeMethods.WsDlgFrame |
                   NativeMethods.WsSysMenu |
                   NativeMethods.WsMinimizeBox |
                   NativeMethods.WsMaximizeBox |
                   NativeMethods.WsBorder);
        style |= NativeMethods.WsPopup | NativeMethods.WsVisible;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlStyle, new IntPtr(style));

        var extendedStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        extendedStyle &= ~NativeMethods.WsExTopmost;
        extendedStyle |= NativeMethods.WsExLayered |
                         NativeMethods.WsExToolWindow |
                         NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, new IntPtr(extendedStyle));
        NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 255, NativeMethods.LwaAlpha);

        var corner = NativeMethods.DwmwcpRound;
        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DwmwaWindowCornerPreference,
            ref corner,
            sizeof(int));

        var border = NativeMethods.DwmColorNone;
        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DwmwaBorderColor,
            ref border,
            sizeof(int));

        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove |
            NativeMethods.SwpNoSize |
            NativeMethods.SwpNoZOrder |
            NativeMethods.SwpNoActivate |
            NativeMethods.SwpFrameChanged);

        PlaceAboveDesktop(hwnd);
    }

    public static void SetRoundedWindowCorners(IntPtr hwnd, bool rounded)
    {
        var preference = rounded ? NativeMethods.DwmwcpRound : NativeMethods.DwmwcpDoNotRound;
        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DwmwaWindowCornerPreference,
            ref preference,
            sizeof(int));
    }

    public static void PlaceAboveDesktop(IntPtr hwnd)
    {
        var shell = NativeMethods.GetShellWindow();
        NativeMethods.SetWindowPos(
            hwnd,
            shell != IntPtr.Zero ? shell : NativeMethods.HwndBottom,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove |
            NativeMethods.SwpNoSize |
            NativeMethods.SwpNoActivate);
    }

    public static void ShowDesktopWindow(IntPtr hwnd)
    {
        NativeMethods.ShowWindow(hwnd, NativeMethods.SwShowNoActivate);
        PlaceAboveDesktop(hwnd);
    }

    public static void ApplyHoverWindowStyle(IntPtr hwnd)
    {
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlStyle).ToInt64();
        style &= ~(NativeMethods.WsCaption |
                   NativeMethods.WsThickFrame |
                   NativeMethods.WsDlgFrame |
                   NativeMethods.WsSysMenu |
                   NativeMethods.WsMinimizeBox |
                   NativeMethods.WsMaximizeBox |
                   NativeMethods.WsBorder);
        style |= NativeMethods.WsPopup | NativeMethods.WsVisible;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlStyle, new IntPtr(style));

        var extendedStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        extendedStyle |= NativeMethods.WsExToolWindow |
                         NativeMethods.WsExNoActivate |
                         NativeMethods.WsExTransparent |
                         NativeMethods.WsExLayered |
                         NativeMethods.WsExTopmost;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, new IntPtr(extendedStyle));
        NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 255, NativeMethods.LwaAlpha);

        var corner = NativeMethods.DwmwcpRound;
        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DwmwaWindowCornerPreference,
            ref corner,
            sizeof(int));

        var border = NativeMethods.DwmColorNone;
        NativeMethods.DwmSetWindowAttribute(
            hwnd,
            NativeMethods.DwmwaBorderColor,
            ref border,
            sizeof(int));

        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove |
            NativeMethods.SwpNoSize |
            NativeMethods.SwpNoZOrder |
            NativeMethods.SwpNoActivate |
            NativeMethods.SwpFrameChanged);
    }

    /// <summary>
    /// Brings a normally non-activating utility process's real window to the
    /// foreground, including when another process currently owns the input queue.
    /// </summary>
    public static void ForceForeground(IntPtr hwnd)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundThread = foreground == IntPtr.Zero
            ? 0
            : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var currentThread = NativeMethods.GetCurrentThreadId();
        var attached = false;

        if (foregroundThread != 0 && foregroundThread != currentThread)
        {
            attached = NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
        }

        try
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SwShow);
            NativeMethods.BringWindowToTop(hwnd);
            NativeMethods.SetForegroundWindow(hwnd);
            NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HwndTopmost,
                0,
                0,
                0,
                0,
                NativeMethods.SwpNoMove |
                NativeMethods.SwpNoSize |
                NativeMethods.SwpNoActivate |
                NativeMethods.SwpShowWindow);
            NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HwndNotTopmost,
                0,
                0,
                0,
                0,
                NativeMethods.SwpNoMove |
                NativeMethods.SwpNoSize |
                NativeMethods.SwpNoActivate);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    public static void SetDarkMode(IntPtr hwnd, bool dark)
    {
        var value = dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int));
    }

    public static void SetTopmost(IntPtr hwnd, bool topmost)
    {
        NativeMethods.SetWindowPos(
            hwnd,
            topmost ? NativeMethods.HwndTopmost : NativeMethods.HwndNotTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
    }

    public static void SetOpacity(IntPtr hwnd, double opacity)
    {
        var alpha = (byte)Math.Clamp(Math.Round(opacity * 255), 0, 255);
        NativeMethods.SetLayeredWindowAttributes(hwnd, 0, alpha, NativeMethods.LwaAlpha);
    }

    public static void ShowWithoutActivation(IntPtr hwnd)
    {
        NativeMethods.ShowWindow(hwnd, NativeMethods.SwShowNoActivate);
        NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HwndTopmost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
    }

    public static void Hide(IntPtr hwnd)
    {
        NativeMethods.ShowWindow(hwnd, 0);
    }

    public static RectInt32 GetWorkArea(DisplayArea displayArea)
    {
        var work = displayArea.WorkArea;
        return new RectInt32(work.X, work.Y, work.Width, work.Height);
    }

    public static PointInt32 GetWindowPosition(AppWindow appWindow)
    {
        return appWindow.Position;
    }

    public static void MoveWithoutActivation(IntPtr hwnd, int x, int y)
    {
        NativeMethods.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            x,
            y,
            0,
            0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }

}
