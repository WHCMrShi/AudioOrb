namespace AudioSourceSelect.Core.Settings;

public sealed class BallPosition
{
    public int X { get; set; }

    public int Y { get; set; }
}

public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Ordered list of endpoint ids the ball cycles through.</summary>
    public List<string> SwitchDeviceIds { get; set; } = [];

    /// <summary>System, Light, or Dark.</summary>
    public string Theme { get; set; } = "System";

    /// <summary>
    /// One slider controls both footprint and layout: 56 = icon square,
    /// 200 = name + volume, 236 = standard, 280 = wide.
    /// </summary>
    public double WidgetWidth { get; set; } = 236;

    /// <summary>"SwitchDevice" cycles outputs; "Volume" adjusts system volume.</summary>
    public string WheelAction { get; set; } = "Volume";

    public bool LaunchAtStartup { get; set; }

    public bool ShowTrayIcon { get; set; } = true;

    public bool HideInFullscreen { get; set; } = true;

    public bool Animations { get; set; } = true;

    public bool HasCompletedFirstRun { get; set; }

    public BallPosition? BallPosition { get; set; }

    public void Normalize()
    {
        SchemaVersion = CurrentSchemaVersion;

        SwitchDeviceIds = (SwitchDeviceIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        Theme = Theme is "Light" or "Dark" ? Theme : "System";
        WidgetWidth = Math.Clamp(double.IsFinite(WidgetWidth) ? WidgetWidth : 236, 56, 300);
        WheelAction = string.Equals(WheelAction, "SwitchDevice", StringComparison.OrdinalIgnoreCase)
            ? "SwitchDevice"
            : "Volume";

        if (BallPosition is not null)
        {
            BallPosition.X = Math.Clamp(BallPosition.X, -32000, 32000);
            BallPosition.Y = Math.Clamp(BallPosition.Y, -32000, 32000);
        }
    }
}
