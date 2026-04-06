using System;
using System.IO;
using System.Text.Json;
using HearthSwing.Models;

namespace HearthSwing.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _settingsPath;

    public AppSettings Current { get; private set; } = new();

    public SettingsService()
    {
        var exeDir = AppContext.BaseDirectory;
        _settingsPath = Path.Combine(exeDir, "AppSettings.json");
    }

    public void Load()
    {
        if (!File.Exists(_settingsPath))
        {
            Current = CreateDefaults();
            Save();
            return;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            Current =
                JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? CreateDefaults();
        }
        catch
        {
            Current = CreateDefaults();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    private static AppSettings CreateDefaults()
    {
        // Try to auto-detect the game path: look for WowClassic.exe near the exe
        var exeDir = AppContext.BaseDirectory;

        // Walk up looking for WowClassic.exe (the app lives inside the game folder)
        var dir = exeDir;
        string gamePath = string.Empty;
        for (var i = 0; i < 5; i++)
        {
            if (dir is null)
                break;
            if (File.Exists(Path.Combine(dir, "WowClassic.exe")))
            {
                gamePath = dir;
                break;
            }
            dir = Path.GetDirectoryName(dir);
        }

        return new AppSettings
        {
            GamePath = gamePath,
            ProfilesPath = Path.Combine(exeDir, "Profiles"),
        };
    }
}
