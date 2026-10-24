using AudioSourceSelect.Core.Audio;
using AudioSourceSelect.Core.Settings;
using AudioSourceSelect.Core.Startup;
using Microsoft.UI.Dispatching;

namespace AudioOrb.Services;

/// <summary>
/// Application-level coordinator for settings, endpoint state, and switching.
/// All members are expected to be used from the UI thread.
/// </summary>
public sealed class AppHost : IDisposable
{
    private readonly SettingsStore _settingsStore;
    private readonly DispatcherQueue _dispatcher;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _switchGate = new(1, 1);

    private AudioDeviceMonitor? _monitor;
    private readonly AudioVolumeProbe _volumeProbe = new();
    private CancellationTokenSource? _refreshDebounce;
    private CancellationTokenSource? _saveDebounce;
    private bool _disposed;

    public AppHost(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
        _settingsStore = new SettingsStore();
        Settings = _settingsStore.Load();
    }

    public AppSettings Settings { get; }

    public IReadOnlyList<AudioEndpoint> Endpoints { get; private set; } = [];

    public AudioEndpoint? CurrentDefault => Endpoints.FirstOrDefault(x => x.IsDefault);

    public IReadOnlyList<AudioEndpoint> SelectedEndpoints =>
        DeviceSwitchPlanner.ResolveEnabledEndpoints(Settings.SwitchDeviceIds, Endpoints);

    public bool IsSwitching { get; private set; }

    public float VolumeLevel { get; private set; } = 1f;

    public bool IsMuted { get; private set; }

    public event EventHandler? VolumeChanged;

    public string SettingsFilePath => _settingsStore.FilePath;

    public static string ExecutablePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "AudioOrb.exe");

    public event EventHandler? DevicesUpdated;

    public event EventHandler? SettingsChanged;

    public async Task InitializeAsync()
    {
        try
        {
            _monitor = new AudioDeviceMonitor();
            _monitor.DevicesChanged += OnDevicesChanged;
            _monitor.DefaultDeviceChanged += OnDefaultDeviceChanged;
        }
        catch (Exception ex)
        {
            AppLog.Error("无法订阅音频设备通知，将只提供手动刷新。", ex);
        }

        ReconcileStartupRegistration();
        await RefreshDevicesAsync();
        RefreshVolume();
    }

    public void RefreshVolume()
    {
        if (_disposed || !_volumeProbe.TryRead(out var level, out var muted))
        {
            return;
        }

        if (Math.Abs(level - VolumeLevel) < 0.001f && muted == IsMuted)
        {
            return;
        }

        VolumeLevel = level;
        IsMuted = muted;
        VolumeChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool AdjustVolume(int direction)
    {
        if (_disposed ||
            !_volumeProbe.TryAdjust(direction * 0.02f, out var level, out var muted))
        {
            return false;
        }

        VolumeLevel = level;
        IsMuted = muted;
        VolumeChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task RefreshDevicesAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _refreshGate.WaitAsync();
        try
        {
            var endpoints = await Task.Run(AudioDeviceCatalog.GetRenderEndpoints);
            if (_disposed)
            {
                return;
            }

            Endpoints = endpoints;
            var selectionChanged = EnsureSelection();
            if (selectionChanged)
            {
                SaveSettingsCore(raiseEvent: false);
            }

            DevicesUpdated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            AppLog.Error("枚举输出设备失败。", ex);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task<AudioSwitchResult> CycleAsync(int direction = 1)
    {
        var activeIds = Endpoints.Select(x => x.Id).ToList();
        var targetId = DeviceSwitchPlanner.ChooseNext(
            Settings.SwitchDeviceIds,
            activeIds,
            CurrentDefault?.Id,
            direction);

        if (targetId is null)
        {
            return AudioSwitchResult.Fail("没有可切换的设备，请先在设置中选择至少两个输出设备。");
        }

        return await SwitchToAsync(targetId);
    }

    public async Task<AudioSwitchResult> SwitchToAsync(string endpointId)
    {
        if (IsSwitching)
        {
            return AudioSwitchResult.Fail("正在切换设备，请稍候。");
        }

        await _switchGate.WaitAsync();
        try
        {
            if (_disposed)
            {
                return AudioSwitchResult.Fail("应用正在退出。");
            }

            IsSwitching = true;
            var endpoint = Endpoints.FirstOrDefault(
                x => string.Equals(x.Id, endpointId, StringComparison.OrdinalIgnoreCase));
            AppLog.Info($"切换输出设备：{endpoint?.Name ?? endpointId}");

            var result = await Task.Run(() => AudioDeviceCatalog.SwitchDefault(endpointId));
            await RefreshDevicesAsync();

            if (result.Success)
            {
                AppLog.Info($"切换成功：{CurrentDefault?.Name ?? endpointId}");
            }
            else
            {
                AppLog.Warning($"切换失败：{result.ErrorMessage}");
            }

            return result;
        }
        finally
        {
            IsSwitching = false;
            _switchGate.Release();
        }
    }

    public bool SetDeviceEnabled(string endpointId, bool enabled)
    {
        var active = Endpoints.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activeSelected = Settings.SwitchDeviceIds.Count(active.Contains);

        if (!enabled)
        {
            var minimum = Endpoints.Count >= 2 ? 2 : 1;
            if (activeSelected <= minimum)
            {
                return false;
            }

            Settings.SwitchDeviceIds.RemoveAll(
                id => string.Equals(id, endpointId, StringComparison.OrdinalIgnoreCase));
        }
        else if (!Settings.SwitchDeviceIds.Contains(endpointId, StringComparer.OrdinalIgnoreCase))
        {
            Settings.SwitchDeviceIds.Add(endpointId);
        }

        SaveSettings();
        return true;
    }

    public bool MoveDevice(string endpointId, int direction)
    {
        var index = Settings.SwitchDeviceIds.FindIndex(
            id => string.Equals(id, endpointId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return false;
        }

        var target = index + Math.Sign(direction);
        if (target < 0 || target >= Settings.SwitchDeviceIds.Count)
        {
            return false;
        }

        (Settings.SwitchDeviceIds[index], Settings.SwitchDeviceIds[target]) =
            (Settings.SwitchDeviceIds[target], Settings.SwitchDeviceIds[index]);
        SaveSettings();
        return true;
    }

    public bool SelectDefaultPair()
    {
        if (Endpoints.Count == 0)
        {
            return false;
        }

        Settings.SwitchDeviceIds = PickDefaultSelection(Endpoints).ToList();
        SaveSettings();
        return true;
    }

    public bool SetLaunchAtStartup(bool enabled, out string? error)
    {
        try
        {
            StartupRegistration.SetEnabled(enabled, ExecutablePath);
            Settings.LaunchAtStartup = enabled;
            SaveSettings();
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("更新开机启动设置失败。", ex);
            error = "无法写入当前用户的开机启动项，请检查系统权限。";
            return false;
        }
    }

    public void SaveSettings()
    {
        SaveSettingsCore(raiseEvent: true);
    }

    /// <summary>Applies in-memory changes to live UI without touching disk yet.</summary>
    public void NotifySettingsPreview()
    {
        if (!_disposed)
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SaveSettingsDeferred()
    {
        if (_disposed)
        {
            return;
        }

        _saveDebounce?.Cancel();
        _saveDebounce?.Dispose();
        _saveDebounce = new CancellationTokenSource();
        _ = SaveSettingsDeferredAsync(_saveDebounce.Token);
    }

    private async Task SaveSettingsDeferredAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(280, token);
            if (!token.IsCancellationRequested && !_disposed)
            {
                SaveSettingsCore(raiseEvent: true);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Error("延迟保存设置失败。", ex);
        }
    }

    private void SaveSettingsCore(bool raiseEvent)
    {
        try
        {
            _settingsStore.Save(Settings);
            if (raiseEvent)
            {
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("保存设置失败。", ex);
        }
    }

    private bool EnsureSelection()
    {
        if (Endpoints.Count == 0)
        {
            return false;
        }

        var activeSelected = DeviceSwitchPlanner.ResolveEnabledEndpoints(Settings.SwitchDeviceIds, Endpoints);
        if (activeSelected.Count == 0)
        {
            Settings.SwitchDeviceIds = PickDefaultSelection(Endpoints).ToList();
            return true;
        }

        // A fresh install with a single saved id should still get a useful pair.
        if (activeSelected.Count == 1 && Endpoints.Count >= 2 && Settings.SwitchDeviceIds.Count <= 1)
        {
            var current = activeSelected[0];
            var other = PickOther(Endpoints, current);
            if (other is not null)
            {
                Settings.SwitchDeviceIds.Add(other.Id);
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> PickDefaultSelection(IReadOnlyList<AudioEndpoint> endpoints)
    {
        if (endpoints.Count == 0)
        {
            return [];
        }

        var current = endpoints.FirstOrDefault(x => x.IsDefault) ?? endpoints[0];
        var other = PickOther(endpoints, current);
        return other is null ? [current.Id] : [current.Id, other.Id];
    }

    private static AudioEndpoint? PickOther(IReadOnlyList<AudioEndpoint> endpoints, AudioEndpoint current)
    {
        // Pair a speaker with a headset, and a headset with a speaker. When two
        // "Game" and "Chat" endpoints exist, the Game channel is the correct
        // playback device; Chat is usually the communications/microphone side.
        var preferHeadphones = !current.IsHeadphones;

        return endpoints
            .Where(x => !string.Equals(x.Id, current.Id, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => preferHeadphones ? x.IsHeadphones : !x.IsHeadphones)
            .ThenByDescending(x => x.IsGameEndpoint)
            .ThenBy(x => x.IsChatEndpoint)
            .ThenBy(x => x.IsDefault)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault();
    }

    private void ReconcileStartupRegistration()
    {
        try
        {
            if (Settings.LaunchAtStartup)
            {
                StartupRegistration.SetEnabled(true, ExecutablePath);
                StartupRegistration.SyncCurrentPath(ExecutablePath);
            }
            else if (StartupRegistration.IsEnabled())
            {
                StartupRegistration.SetEnabled(false, ExecutablePath);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warning($"同步开机启动项失败：{ex.Message}");
        }
    }

    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        _dispatcher.TryEnqueue(() => ScheduleRefresh(120));
    }

    private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
    {
        _dispatcher.TryEnqueue(() => ScheduleRefresh(80));
    }

    private void ScheduleRefresh(int delayMilliseconds)
    {
        if (_disposed)
        {
            return;
        }

        _refreshDebounce?.Cancel();
        _refreshDebounce?.Dispose();
        _refreshDebounce = new CancellationTokenSource();
        _ = RefreshAfterDelayAsync(delayMilliseconds, _refreshDebounce.Token);
    }

    private async Task RefreshAfterDelayAsync(int delayMilliseconds, CancellationToken token)
    {
        try
        {
            await Task.Delay(delayMilliseconds, token);
            if (!token.IsCancellationRequested && !_disposed)
            {
                await RefreshDevicesAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Error("刷新设备列表失败。", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refreshDebounce?.Cancel();
        _saveDebounce?.Cancel();
        _monitor?.Dispose();
        _volumeProbe.Dispose();
        SaveSettingsCore(raiseEvent: false);

        _refreshGate.Dispose();
        _switchGate.Dispose();
    }
}
