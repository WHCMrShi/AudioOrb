using System.Diagnostics;
using System.Reflection;
using AudioOrb.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AudioOrb.Pages;

public sealed partial class AboutPage : Page
{
    private AppHost? _host;

    public AboutPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _host = e.Parameter as AppHost;

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
            ?? "1.0.0";

        var plusIndex = version.IndexOf('+');
        if (plusIndex > 0)
        {
            version = version[..plusIndex];
        }

        VersionText.Text = $"版本 {version} · WinUI 3";
        SettingsPathText.Text = _host?.SettingsFilePath ?? string.Empty;

        if (_host is not null)
        {
            var theme = _host.Settings.Theme switch
            {
                "Light" => "浅色",
                "Dark" => "深色",
                _ => "跟随系统",
            };
            var selected = _host.SelectedEndpoints;
            var order = selected.Count == 0
                ? "未选择设备"
                : string.Join("  →  ", selected.Select(x => x.Name));
            var width = _host.Settings.WidgetWidth;
            var layout = width <= 56
                ? "图标方形"
                : width <= 200
                    ? "名称"
                    : width <= 236
                        ? "标准"
                        : "宽";
            ConfigText.Text =
                $"主题 {theme} · {layout} · 宽度 {Math.Round(width)} px" +
                $"{Environment.NewLine}切换顺序：{order}";
        }
    }

    private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null)
        {
            return;
        }

        OpenFolder(Path.GetDirectoryName(_host.SettingsFilePath));
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolder(AppLog.DirectoryPath);
    }

    private static void OpenFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppLog.Error($"打开文件夹失败：{folder}", ex);
        }
    }

    private void OpenSoundSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:sound",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("打开 Windows 声音设置失败。", ex);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        (Application.Current as App)?.ExitApp();
    }
}
