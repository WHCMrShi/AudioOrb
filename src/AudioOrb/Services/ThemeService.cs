using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace AudioOrb.Services;

internal static class ThemeService
{
    public static ElementTheme ToElementTheme(string theme) => theme switch
    {
        "Light" => ElementTheme.Light,
        "Dark" => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    public static bool IsDark(string theme)
    {
        return theme switch
        {
            "Dark" => true,
            "Light" => false,
            _ => Application.Current.RequestedTheme == ApplicationTheme.Dark,
        };
    }

    public static void ApplyTitleBarTheme(AppWindow appWindow, bool dark)
    {
        var titleBar = appWindow.TitleBar;
        if (titleBar is null)
        {
            return;
        }

        titleBar.BackgroundColor = Colors.Transparent;
        titleBar.ForegroundColor = dark ? Colors.White : Color.FromArgb(255, 32, 32, 32);
        titleBar.InactiveBackgroundColor = Colors.Transparent;
        titleBar.InactiveForegroundColor = dark
            ? Color.FromArgb(255, 160, 160, 160)
            : Color.FromArgb(255, 110, 110, 110);
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = dark ? Colors.White : Color.FromArgb(255, 32, 32, 32);
        titleBar.ButtonInactiveForegroundColor = dark
            ? Color.FromArgb(255, 160, 160, 160)
            : Color.FromArgb(255, 110, 110, 110);
        titleBar.ButtonHoverBackgroundColor = dark
            ? Color.FromArgb(24, 255, 255, 255)
            : Color.FromArgb(16, 0, 0, 0);
        titleBar.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
        titleBar.ButtonPressedBackgroundColor = dark
            ? Color.FromArgb(36, 255, 255, 255)
            : Color.FromArgb(24, 0, 0, 0);
        titleBar.ButtonPressedForegroundColor = dark ? Colors.White : Colors.Black;
    }
}
