using System.Text.Json;

namespace AudioSourceSelect.Core.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly object _gate = new();

    public SettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AudioOrb",
            "settings.json");
    }

    public string FilePath { get; }

    public AppSettings Load()
    {
        lock (_gate)
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            try
            {
                var json = File.ReadAllText(FilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new AppSettings();
                }

                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                settings.Normalize();
                return settings;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                TryBackupCorruptFile();
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        lock (_gate)
        {
            settings.Normalize();
            var directory = Path.GetDirectoryName(FilePath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException("设置文件路径无效。");
            }

            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            var temporaryPath = FilePath + ".tmp";
            File.WriteAllText(temporaryPath, json);

            try
            {
                File.Move(temporaryPath, FilePath, overwrite: true);
            }
            catch
            {
                // A previous instance may briefly hold the destination. Retry
                // once after a short pause before surfacing the failure.
                Thread.Sleep(30);
                File.Move(temporaryPath, FilePath, overwrite: true);
            }
        }
    }

    private void TryBackupCorruptFile()
    {
        try
        {
            var backup = FilePath + $".invalid-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(FilePath, backup, overwrite: false);
        }
        catch
        {
            // A read-only or locked file should not prevent the app from starting.
        }
    }
}
