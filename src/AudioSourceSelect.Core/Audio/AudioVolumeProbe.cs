using System.Runtime.InteropServices;

namespace AudioSourceSelect.Core.Audio;

/// <summary>
/// Reads the master volume of the current default render endpoint. The
/// enumerator is kept alive; the active endpoint is rebuilt only when the
/// default device changes.
/// </summary>
public sealed class AudioVolumeProbe : IDisposable
{
    private IMMDeviceEnumerator? _enumerator;
    private IMMDevice? _device;
    private IAudioEndpointVolume? _volume;
    private string? _deviceId;
    private bool _disposed;

    public bool TryRead(out float level, out bool muted)
    {
        level = 1;
        muted = false;
        if (_disposed)
        {
            return false;
        }

        try
        {
            _enumerator ??= (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
            if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Console, out var endpoint) != 0 ||
                endpoint is null)
            {
                return false;
            }

            try
            {
                if (endpoint.GetId(out var id) != 0 || string.IsNullOrWhiteSpace(id))
                {
                    return false;
                }

                if (!string.Equals(_deviceId, id, StringComparison.OrdinalIgnoreCase) || _volume is null)
                {
                    ReleaseVolume();
                    _device = endpoint;
                    _deviceId = id;
                    endpoint = null;

                    var iid = typeof(IAudioEndpointVolume).GUID;
                    if (_device.Activate(ref iid, 0x17, IntPtr.Zero, out var instance) != 0 ||
                        instance is not IAudioEndpointVolume volume)
                    {
                        return false;
                    }

                    _volume = volume;
                }
            }
            finally
            {
                if (endpoint is not null)
                {
                    Marshal.ReleaseComObject(endpoint);
                }
            }

            if (_volume is null ||
                _volume.GetMasterVolumeLevelScalar(out var scalar) != 0)
            {
                return false;
            }

            level = Math.Clamp(scalar, 0f, 1f);
            if (_volume.GetMute(out var isMuted) == 0)
            {
                muted = isMuted;
            }

            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }

    public bool TryAdjust(float delta, out float level, out bool muted)
    {
        level = 1;
        muted = false;
        if (!TryRead(out var current, out var currentMuted) || _volume is null)
        {
            return false;
        }

        var target = Math.Clamp(current + delta, 0f, 1f);
        var context = Guid.Empty;
        if (_volume.SetMasterVolumeLevelScalar(target, ref context) != 0)
        {
            return false;
        }

        if (delta > 0 && currentMuted)
        {
            _volume.SetMute(false, ref context);
        }

        return TryRead(out level, out muted);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseVolume();
        if (_enumerator is not null)
        {
            Marshal.ReleaseComObject(_enumerator);
            _enumerator = null;
        }
    }

    private void ReleaseVolume()
    {
        if (_volume is not null)
        {
            Marshal.ReleaseComObject(_volume);
            _volume = null;
        }

        if (_device is not null)
        {
            Marshal.ReleaseComObject(_device);
            _device = null;
        }

        _deviceId = null;
    }
}
