using System.Diagnostics;
using AudioOrb.Models;
using AudioOrb.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AudioOrb.Pages;

public sealed partial class DevicesPage : Page
{
    private AppHost? _host;
    private bool _refreshing;

    public DevicesPage()
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

        _host.DevicesUpdated += Host_DevicesUpdated;
        RefreshUi();

        if (_host.Endpoints.Count == 0)
        {
            _ = RefreshAsync(silent: true);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (_host is not null)
        {
            _host.DevicesUpdated -= Host_DevicesUpdated;
        }

        base.OnNavigatedFrom(e);
    }

    private void Host_DevicesUpdated(object? sender, EventArgs e)
    {
        RefreshUi();
    }

    private void RefreshUi()
    {
        if (_host is null)
        {
            return;
        }

        var rows = new List<DeviceRow>();
        foreach (var endpoint in _host.Endpoints)
        {
            var index = _host.Settings.SwitchDeviceIds.FindIndex(
                id => string.Equals(id, endpoint.Id, StringComparison.OrdinalIgnoreCase));
            rows.Add(DeviceRow.FromEndpoint(endpoint, index >= 0, index + 1));
        }

        // Update the existing rows in place. Replacing ItemsSource on every
        // device switch restarts the ToggleSwitch animations and makes the
        // disabled order buttons flash.
        if (DeviceItems.ItemsSource is IList<DeviceRow> existing &&
            existing.Count == rows.Count &&
            existing.Select(x => x.Id).SequenceEqual(rows.Select(x => x.Id), StringComparer.OrdinalIgnoreCase))
        {
            for (var i = 0; i < rows.Count; i++)
            {
                if (!string.Equals(existing[i].Name, rows[i].Name, StringComparison.Ordinal) ||
                    !string.Equals(existing[i].Detail, rows[i].Detail, StringComparison.Ordinal))
                {
                    DeviceItems.ItemsSource = rows;
                    break;
                }

                existing[i].Update(
                    _host.Endpoints[i],
                    rows[i].IsSelected,
                    rows[i].Order);
            }
        }
        else
        {
            DeviceItems.ItemsSource = rows;
        }

        EmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DeviceItems.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        DeviceCountText.Text = rows.Count == 0
            ? "未找到设备"
            : $"{rows.Count} 台活动设备 · {rows.Count(x => x.IsSelected)} 台参与切换";

    }

    private void DeviceToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_host is null || sender is not ToggleSwitch toggle || toggle.Tag is not string id)
        {
            return;
        }

        var currentlySelected = _host.Settings.SwitchDeviceIds.Contains(id, StringComparer.OrdinalIgnoreCase);
        if (currentlySelected == toggle.IsOn)
        {
            return;
        }

        if (!_host.SetDeviceEnabled(id, toggle.IsOn))
        {
            ShowMessage("至少需要保留两个参与切换的设备。", InfoBarSeverity.Warning);
            RefreshUi();
            return;
        }

        RefreshUi();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync(silent: false);
    }

    private async Task RefreshAsync(bool silent)
    {
        if (_host is null || _refreshing)
        {
            return;
        }

        _refreshing = true;
        RefreshRing.IsActive = true;
        RefreshButton.IsEnabled = false;
        try
        {
            await _host.RefreshDevicesAsync();
            RefreshUi();
            if (!silent)
            {
                ShowMessage(
                    $"已刷新，共找到 {_host.Endpoints.Count} 台活动输出设备。",
                    InfoBarSeverity.Success);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("刷新输出设备失败。", ex);
            ShowMessage("刷新设备列表失败，请查看日志。", InfoBarSeverity.Error);
        }
        finally
        {
            _refreshing = false;
            RefreshRing.IsActive = false;
            RefreshButton.IsEnabled = true;
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
