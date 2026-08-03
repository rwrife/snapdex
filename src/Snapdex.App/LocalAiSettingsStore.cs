using System;
using System.IO;
using System.Text.Json;
using SnapdexCore.LocalAi;

namespace Snapdex.App;

internal sealed class LocalAiSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;

    public LocalAiSettingsStore(string settingsPath)
    {
        _settingsPath = settingsPath ?? throw new ArgumentNullException(nameof(settingsPath));
    }

    public LocalAiSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return LocalAiSettings.Default;
            }

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<LocalAiSettings>(json, JsonOptions);
            return settings?.Normalize() ?? LocalAiSettings.Default;
        }
        catch
        {
            return LocalAiSettings.Default;
        }
    }

    public void Save(LocalAiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalized = settings.Normalize();
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(normalized, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }
}
