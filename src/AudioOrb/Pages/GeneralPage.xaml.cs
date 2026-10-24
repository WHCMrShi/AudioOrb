using System.Diagnostics;
using AudioOrb.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AudioOrb.Pages;

public sealed partial class GeneralPage : Page
{
    private AppHost? _host;
    private bool _loading;

    public GeneralPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _host = e.Parameter as AppHost;
        if (_host is null)
        {
            return;
        }

        _loading = true;
        StartupToggle.IsOn = _host.Settings.LaunchAtStartup;
        TrayToggle.IsOn = _host.Settings.ShowTrayIcon;
        FullscreenToggle.IsOn = _host.Settings.HideInFullscreen;
        AnimationToggle.IsOn = _host.Settings.Animations;
        SelectWheelAction(_host.Settings.WheelAction);
        UpdateOperationDescription();
        _loading = false;
    }

    private void SelectWheelAction(string action)
    {
        for (var i = 0; i < WheelPicker.Items.Count; i++)
        {
            if (WheelPicker.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag as string, action, StringComparison.OrdinalIgnoreCase))
            {
                WheelPicker.SelectedIndex = i;
                return;
            }
        }

        WheelPicker.SelectedIndex = 0;
    }

    private void UpdateOperationDescription()
    {
        OperationCard.Description = _host?.Settings.WheelAction == "Volume"
            ? "单击：切换设备 · 滚轮：调整系统音量 · 右键：打开菜单 · 拖动：移动组件"
            : "单击：切换设备 · 滚轮：前后切换 · 右键：打开菜单 · 拖动：移动组件";
    }

    private void WheelPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _host is null)
        {
            return;
        }

        var tag = (WheelPicker.SelectedItem as ComboBoxItem)?.Tag as string;
        if (tag is null)
        {
            return;
        }

        _host.Settings.WheelAction = tag;
        UpdateOperationDescription();
        _host.SaveSettings();
    }

    private void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _host is null)
        {
            return;
        }

        var wanted = StartupToggle.IsOn;
        if (!_host.SetLaunchAtStartup(wanted, out var error))
        {
            _loading = true;
            StartupToggle.IsOn = _host.Settings.LaunchAtStartup;
            _loading = false;
            ShowMessage(error ?? "无法更新开机启动设置。", InfoBarSeverity.Error);
            return;
        }

        ShowMessage(wanted ? "已开启开机启动。" : "已关闭开机启动。", InfoBarSeverity.Success);
    }

    private void TrayToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _host is null)
        {
            return;
        }

        _host.Settings.ShowTrayIcon = TrayToggle.IsOn;
        _host.SaveSettings();
    }

    private void FullscreenToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _host is null)
        {
            return;
        }

        _host.Settings.HideInFullscreen = FullscreenToggle.IsOn;
        _host.SaveSettings();
    }

    private void AnimationToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || _host is null)
        {
            return;
        }

        _host.Settings.Animations = AnimationToggle.IsOn;
        _host.SaveSettings();
    }

    private void ResetPosition_Click(object sender, RoutedEventArgs e)
    {
        (Application.Current as App)?.ResetBallPosition();
        ShowMessage("桌面组件已回到屏幕右下角。", InfoBarSeverity.Success);
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
            ShowMessage("无法打开 Windows 声音设置。", InfoBarSeverity.Error);
        }
    }

    private void ShowMessage(string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Severity = severity;
        StatusInfoBar.Message = message;
        StatusInfoBar.IsOpen = true;
    }
}
