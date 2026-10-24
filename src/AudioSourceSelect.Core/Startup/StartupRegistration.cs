using Microsoft.Win32;

namespace AudioSourceSelect.Core.Startup;

/// <summary>
/// Manages the current-user Run entry used for launch-at-sign-in.
/// </summary>
public static class StartupRegistration
{
    public const string ValueName = "AudioOrb";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string BuildCommand(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        return $"\"{executablePath}\" --startup";
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(ValueName) as string;
        return !string.IsNullOrWhiteSpace(value);
    }

    public static void SetEnabled(bool enabled, string executablePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                        ?? throw new InvalidOperationException("无法写入开机启动注册表项。");

        if (enabled)
        {
            key.SetValue(ValueName, BuildCommand(executablePath), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// If startup was previously enabled, follow the executable when the app is
    /// moved to a different folder.
    /// </summary>
    public static void SyncCurrentPath(string executablePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            return;
        }

        var current = key.GetValue(ValueName) as string;
        if (string.IsNullOrWhiteSpace(current))
        {
            return;
        }

        var expected = BuildCommand(executablePath);
        if (!string.Equals(current.Trim(), expected, StringComparison.OrdinalIgnoreCase))
        {
            key.SetValue(ValueName, expected, RegistryValueKind.String);
        }
    }
}
