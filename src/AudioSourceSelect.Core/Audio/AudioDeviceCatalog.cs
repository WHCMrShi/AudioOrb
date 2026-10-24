using System.Runtime.InteropServices;
using System.Text;

namespace AudioSourceSelect.Core.Audio;

/// <summary>
/// Stateless, thread-safe access to Windows Core Audio render endpoints.
/// COM objects are created and released for each operation so calls can be made
/// from a worker thread without apartment affinity concerns.
/// </summary>
public static class AudioDeviceCatalog
{
    public static IReadOnlyList<AudioEndpoint> GetRenderEndpoints()
    {
        return QueryRenderEndpoints(out _);
    }

    public static string? GetDefaultRenderEndpointId()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
            var hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Console, out device);
            if (hr != 0 || device is null)
            {
                return null;
            }

            hr = device.GetId(out var id);
            return hr == 0 ? id : null;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            Release(device);
            Release(enumerator);
        }
    }

    public static AudioSwitchResult SwitchDefault(string endpointId)
    {
        if (string.IsNullOrWhiteSpace(endpointId))
        {
            return AudioSwitchResult.Fail("设备 ID 为空。");
        }

        IPolicyConfig? policyConfig = null;
        try
        {
            policyConfig = (IPolicyConfig)(object)new PolicyConfigClientComObject();
        }
        catch (COMException ex)
        {
            return AudioSwitchResult.Fail($"无法初始化 Windows 音频策略：{ex.Message}");
        }

        try
        {
            var consoleResult = policyConfig.SetDefaultEndpoint(endpointId, ERole.Console);
            var multimediaResult = policyConfig.SetDefaultEndpoint(endpointId, ERole.Multimedia);
            var communicationsResult = policyConfig.SetDefaultEndpoint(endpointId, ERole.Communications);

            if (consoleResult != 0 && multimediaResult != 0 && communicationsResult != 0)
            {
                return AudioSwitchResult.Fail(DescribeHResult(consoleResult));
            }

            // IPolicyConfig usually applies synchronously, but give the audio
            // service a short grace period on slower machines.
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var current = GetDefaultRenderEndpointId();
                if (string.Equals(current, endpointId, StringComparison.OrdinalIgnoreCase))
                {
                    return AudioSwitchResult.Ok();
                }

                Thread.Sleep(25);
            }

            return AudioSwitchResult.Fail("Windows 未接受默认设备变更，请稍后重试。");
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            return AudioSwitchResult.Fail($"切换失败：{ex.Message}");
        }
        finally
        {
            Release(policyConfig);
        }
    }

    private static IReadOnlyList<AudioEndpoint> QueryRenderEndpoints(out string? defaultId)
    {
        var result = new List<AudioEndpoint>();
        defaultId = null;

        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();

            var defaultDevice = GetDefaultDevice(enumerator, out var id);
            defaultId = id;
            Release(defaultDevice);

            var hr = enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceState.Active, out collection);
            if (hr != 0 || collection is null)
            {
                return result;
            }

            hr = collection.GetCount(out var count);
            if (hr != 0)
            {
                return result;
            }

            for (var i = 0; i < count; i++)
            {
                IMMDevice? device = null;
                try
                {
                    if (collection.Item(i, out device) != 0 || device is null)
                    {
                        continue;
                    }

                    if (device.GetId(out var endpointId) != 0 || string.IsNullOrWhiteSpace(endpointId))
                    {
                        continue;
                    }

                    var name = ReadStringProperty(device, PropertyKeys.DeviceFriendlyName)
                               ?? ReadStringProperty(device, PropertyKeys.DeviceDescription)
                               ?? "音频设备";
                    var adapter = ReadStringProperty(device, PropertyKeys.DeviceInterfaceFriendlyName) ?? string.Empty;
                    var formFactor = ReadFormFactor(device);
                    var containerId = ReadGuidProperty(device, PropertyKeys.DeviceContainerId);

                    result.Add(new AudioEndpoint(
                        endpointId,
                        Sanitize(name),
                        Sanitize(adapter),
                        formFactor,
                        string.Equals(endpointId, defaultId, StringComparison.OrdinalIgnoreCase))
                    {
                        ContainerId = containerId,
                    });
                }
                finally
                {
                    Release(device);
                }
            }
        }
        catch (COMException)
        {
            // Returning an empty list lets the UI present a useful "no device" state.
        }
        finally
        {
            Release(collection);
            Release(enumerator);
        }

        // Keep the UI stable when two endpoints share a friendly name.
        var duplicates = result
            .GroupBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        if (duplicates.Count > 0)
        {
            result = result.Select(endpoint =>
            {
                if (!duplicates.Contains(endpoint.Name) || string.IsNullOrWhiteSpace(endpoint.AdapterName))
                {
                    return endpoint;
                }

                return endpoint with { Name = $"{endpoint.Name} · {endpoint.AdapterName}" };
            }).ToList();
        }

        // Windows shows both endpoints of a USB headset with the headset glyph,
        // even when the game endpoint reports itself as Speakers. The PnP
        // container is the common, machine-independent source for that fact.
        var headsetContainers = result
            .Where(x => x.ContainerId is { } id && id != Guid.Empty &&
                        x.FormFactor == EndpointFormFactor.Headset)
            .Select(x => x.ContainerId!.Value)
            .ToHashSet();
        var headphoneContainers = result
            .Where(x => x.ContainerId is { } id && id != Guid.Empty &&
                        x.FormFactor == EndpointFormFactor.Headphones)
            .Select(x => x.ContainerId!.Value)
            .ToHashSet();

        result = result.Select(endpoint => endpoint with
        {
            IsHeadsetContainer = endpoint.ContainerId is { } id &&
                                 id != Guid.Empty &&
                                 headsetContainers.Contains(id),
            IsHeadphoneContainer = endpoint.ContainerId is { } id2 &&
                                   id2 != Guid.Empty &&
                                   headphoneContainers.Contains(id2),
        }).ToList();

        // Keep the list order stable when the default device changes; only the
        // "current" badge should move. Sorting by default first made the whole
        // settings list jump on every switch.
        return result
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IMMDevice? GetDefaultDevice(IMMDeviceEnumerator enumerator, out string? id)
    {
        id = null;
        if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Console, out var device) != 0 ||
            device is null)
        {
            return null;
        }

        if (device.GetId(out var deviceId) == 0)
        {
            id = deviceId;
        }

        return device;
    }

    private static string? ReadStringProperty(IMMDevice device, PropertyKey key)
    {
        IPropertyStore? store = null;
        try
        {
            if (device.OpenPropertyStore(StorageAccessMode.Read, out store) != 0 || store is null)
            {
                return null;
            }

            var localKey = key;
            if (store.GetValue(ref localKey, out var value) != 0)
            {
                return null;
            }

            try
            {
                return PropVariantValues.GetString(ref value);
            }
            finally
            {
                CoreAudioNative.PropVariantClear(ref value);
            }
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            Release(store);
        }
    }

    private static Guid? ReadGuidProperty(IMMDevice device, PropertyKey key)
    {
        IPropertyStore? store = null;
        try
        {
            if (device.OpenPropertyStore(StorageAccessMode.Read, out store) != 0 || store is null)
            {
                return null;
            }

            var localKey = key;
            if (store.GetValue(ref localKey, out var value) != 0)
            {
                return null;
            }

            try
            {
                return PropVariantValues.GetGuid(ref value);
            }
            finally
            {
                CoreAudioNative.PropVariantClear(ref value);
            }
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            Release(store);
        }
    }

    private static EndpointFormFactor ReadFormFactor(IMMDevice device)
    {
        IPropertyStore? store = null;
        try
        {
            if (device.OpenPropertyStore(StorageAccessMode.Read, out store) != 0 || store is null)
            {
                return EndpointFormFactor.Unknown;
            }

            var key = PropertyKeys.AudioEndpointFormFactor;
            if (store.GetValue(ref key, out var value) != 0)
            {
                return EndpointFormFactor.Unknown;
            }

            try
            {
                var number = PropVariantValues.GetUInt32(ref value);
                if (number is null)
                {
                    return EndpointFormFactor.Unknown;
                }

                return Enum.IsDefined(typeof(EndpointFormFactor), (int)number.Value)
                    ? (EndpointFormFactor)number.Value
                    : EndpointFormFactor.Unknown;
            }
            finally
            {
                CoreAudioNative.PropVariantClear(ref value);
            }
        }
        catch (COMException)
        {
            return EndpointFormFactor.Unknown;
        }
        finally
        {
            Release(store);
        }
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (!char.IsControl(ch))
            {
                builder.Append(ch);
            }
        }

        return builder.ToString().Trim();
    }

    private static string DescribeHResult(int hr)
    {
        var text = Marshal.GetExceptionForHR(hr)?.Message;
        return string.IsNullOrWhiteSpace(text)
            ? $"Windows 返回错误 0x{hr:X8}。"
            : $"切换失败：{text}";
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            try
            {
                Marshal.ReleaseComObject(comObject);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidComObjectException)
            {
                // Already released.
            }
        }
    }
}
