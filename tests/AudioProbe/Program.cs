using System.Text;
using AudioSourceSelect.Core.Audio;
using AudioSourceSelect.Core.Settings;

Console.OutputEncoding = Encoding.UTF8;

var command = args.Length == 0 ? "list" : args[0].ToLowerInvariant();

try
{
    switch (command)
    {
        case "list":
            PrintDevices();
            break;

        case "switch":
            if (args.Length < 2)
            {
                Console.Error.WriteLine("用法: AudioProbe switch <设备序号>");
                return 2;
            }

            return SwitchByIndex(args[1]);

        case "toggle":
            return Toggle();

        case "roundtrip":
            return RoundTrip();

        case "monitor":
            Monitor();
            break;

        case "settings":
            return SettingsSmokeTest();

        default:
            Console.Error.WriteLine("命令: list | switch <n> | toggle | roundtrip | monitor | settings");
            return 2;
    }

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"错误: {ex.GetType().Name}: {ex.Message}");
    return 1;
}

static IReadOnlyList<AudioEndpoint> GetDevices()
{
    var devices = AudioDeviceCatalog.GetRenderEndpoints();
    if (devices.Count == 0)
    {
        Console.WriteLine("未找到活动的输出设备。");
    }

    return devices;
}

static void PrintDevices()
{
    var devices = GetDevices();
    Console.WriteLine($"活动输出设备: {devices.Count}");
    for (var i = 0; i < devices.Count; i++)
    {
        var d = devices[i];
        Console.WriteLine($"  [{i + 1}] {(d.IsDefault ? "* " : "  ")}{d.Name}");
        Console.WriteLine($"      类型: {d.FormFactorLabel} ({d.FormFactor})  适配器: {d.AdapterName}");
        Console.WriteLine($"      ID: {d.Id}");
    }

    Console.WriteLine($"当前默认: {devices.FirstOrDefault(x => x.IsDefault)?.Name ?? AudioDeviceCatalog.GetDefaultRenderEndpointId() ?? "(无)"}");
}

static int SwitchByIndex(string text)
{
    var devices = GetDevices();
    if (!int.TryParse(text, out var number) || number < 1 || number > devices.Count)
    {
        Console.Error.WriteLine($"设备序号必须在 1..{devices.Count} 之间。");
        return 2;
    }

    var target = devices[number - 1];
    Console.WriteLine($"正在切换到: {target.Name}");
    var result = AudioDeviceCatalog.SwitchDefault(target.Id);
    Console.WriteLine(result.Success ? "切换成功。" : $"切换失败: {result.ErrorMessage}");

    var current = AudioDeviceCatalog.GetDefaultRenderEndpointId();
    var currentName = AudioDeviceCatalog.GetRenderEndpoints().FirstOrDefault(x => x.IsDefault)?.Name;
    Console.WriteLine($"当前默认: {currentName ?? current ?? "(无)"}");
    return result.Success ? 0 : 1;
}

static int Toggle()
{
    var devices = GetDevices();
    if (devices.Count < 2)
    {
        Console.Error.WriteLine("至少需要两个活动输出设备。");
        return 1;
    }

    var current = devices.FirstOrDefault(x => x.IsDefault);
    var target = devices.FirstOrDefault(x => !x.IsDefault);
    if (target is null)
    {
        Console.Error.WriteLine("没有可切换的第二台设备。");
        return 1;
    }

    Console.WriteLine($"当前: {current?.Name ?? "(未知)"}");
    Console.WriteLine($"切换: {target.Name}");
    var result = AudioDeviceCatalog.SwitchDefault(target.Id);
    Console.WriteLine(result.Success ? "切换成功。" : $"切换失败: {result.ErrorMessage}");
    return result.Success ? 0 : 1;
}

static int RoundTrip()
{
    var devices = GetDevices();
    if (devices.Count < 2)
    {
        Console.Error.WriteLine("至少需要两个活动输出设备。");
        return 1;
    }

    var original = devices.First(x => x.IsDefault);
    var other = devices.First(x => !x.IsDefault);

    Console.WriteLine($"原始设备: {original.Name}");
    Console.WriteLine($"切换到: {other.Name}");
    var first = AudioDeviceCatalog.SwitchDefault(other.Id);
    if (!first.Success)
    {
        Console.Error.WriteLine($"第一次切换失败: {first.ErrorMessage}");
        return 1;
    }

    Console.WriteLine("已完成第一次切换，正在切回…");
    var second = AudioDeviceCatalog.SwitchDefault(original.Id);
    if (!second.Success)
    {
        Console.Error.WriteLine($"切回失败: {second.ErrorMessage}");
        return 1;
    }

    Console.WriteLine("往返切换成功，已恢复原始设备。");
    return 0;
}

static void Monitor()
{
    using var monitor = new AudioDeviceMonitor();
    monitor.DefaultDeviceChanged += (_, e) =>
        Console.WriteLine($"[默认设备变更] {e.DeviceId}");
    monitor.DevicesChanged += (_, _) =>
        Console.WriteLine("[设备列表变更]");

    Console.WriteLine("正在监听音频设备通知，按 Ctrl+C 退出…");
    using var stop = new ManualResetEventSlim(false);
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        stop.Set();
    };
    stop.Wait();
}

static int SettingsSmokeTest()
{
    var temp = Path.Combine(Path.GetTempPath(), $"audioorb-settings-{Guid.NewGuid():N}.json");
    try
    {
        var store = new SettingsStore(temp);
        var settings = new AppSettings
        {
            WidgetWidth = 1000,
            SwitchDeviceIds = ["a", "a", "b", ""],
            Theme = "Neon",
            BallPosition = new BallPosition { X = 120, Y = 340 },
        };

        store.Save(settings);
        var loaded = store.Load();

        Console.WriteLine($"File: {temp}");
        Console.WriteLine($"WidgetWidth={loaded.WidgetWidth}");
        Console.WriteLine($"Ids={string.Join(',', loaded.SwitchDeviceIds)} Theme={loaded.Theme}");
        Console.WriteLine($"Position={loaded.BallPosition?.X},{loaded.BallPosition?.Y}");

        var valid = loaded.WidgetWidth == 300 &&
                    loaded.SwitchDeviceIds.SequenceEqual(["a", "b"]) &&
                    loaded.Theme == "System";
        Console.WriteLine(valid ? "设置归一化测试通过。" : "设置归一化测试失败。");
        return valid ? 0 : 1;
    }
    finally
    {
        if (File.Exists(temp))
        {
            File.Delete(temp);
        }
    }
}
