using System.Runtime.InteropServices;

namespace AudioSourceSelect.Core.Audio;

public sealed class DefaultDeviceChangedEventArgs(string? deviceId) : EventArgs
{
    public string? DeviceId { get; } = deviceId;
}

/// <summary>
/// Registers for Windows Core Audio endpoint notifications. The callback is
/// event-driven, so the idle application does not need a polling timer.
/// </summary>
public sealed class AudioDeviceMonitor : IDisposable
{
    private readonly IMMDeviceEnumerator _enumerator;
    private readonly NotificationClient _callback;
    private bool _disposed;

    public AudioDeviceMonitor()
    {
        _enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
        _callback = new NotificationClient(this);

        var hr = _enumerator.RegisterEndpointNotificationCallback(_callback);
        if (hr != 0)
        {
            Marshal.ReleaseComObject(_callback);
            Marshal.ReleaseComObject(_enumerator);
            throw new COMException("无法订阅 Windows 音频设备变更通知。", hr);
        }
    }

    /// <summary>Raised when an endpoint is added, removed, or changes state.</summary>
    public event EventHandler? DevicesChanged;

    /// <summary>Raised when the default render endpoint changes.</summary>
    public event EventHandler<DefaultDeviceChangedEventArgs>? DefaultDeviceChanged;

    internal void NotifyDevicesChanged()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // A notification callback must never throw into the audio service.
        }
    }

    internal void NotifyDefaultDeviceChanged(string? deviceId)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            DefaultDeviceChanged?.Invoke(this, new DefaultDeviceChangedEventArgs(deviceId));
        }
        catch
        {
            // A notification callback must never throw into the audio service.
        }
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
            _enumerator.UnregisterEndpointNotificationCallback(_callback);
        }
        catch (COMException)
        {
            // The audio service may already be shutting down.
        }

        try
        {
            Marshal.ReleaseComObject(_callback);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidComObjectException)
        {
        }

        try
        {
            Marshal.ReleaseComObject(_enumerator);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidComObjectException)
        {
        }
    }

    private sealed class NotificationClient(AudioDeviceMonitor owner) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            owner.NotifyDevicesChanged();
            return 0;
        }

        public int OnDeviceAdded(string deviceId)
        {
            owner.NotifyDevicesChanged();
            return 0;
        }

        public int OnDeviceRemoved(string deviceId)
        {
            owner.NotifyDevicesChanged();
            return 0;
        }

        public int OnDefaultDeviceChanged(EDataFlow dataFlow, ERole role, string defaultDeviceId)
        {
            // Windows sends one notification per role. React only to the console
            // role so a single default-device change does not refresh the UI 3x.
            if (dataFlow == EDataFlow.Render && role == ERole.Console)
            {
                owner.NotifyDefaultDeviceChanged(string.IsNullOrWhiteSpace(defaultDeviceId) ? null : defaultDeviceId);
            }

            return 0;
        }

        public int OnPropertyValueChanged(string deviceId, PropertyKey key)
        {
            if (key.FormatId == PropertyKeys.DeviceFriendlyName.FormatId ||
                key.FormatId == PropertyKeys.AudioEndpointFormFactor.FormatId)
            {
                owner.NotifyDevicesChanged();
            }

            return 0;
        }
    }
}
