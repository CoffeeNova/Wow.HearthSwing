using System.IO;
using System.Text.Json;
using HearthSwing.Models;

namespace HearthSwing.Services;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IFileSystem _fs;
    private readonly string _settingsPath;

    public AppSettings Current { get; private set; } = new();

    public SettingsService(IFileSystem fileSystem, string? settingsPath = null)
    {
        _fs = fileSystem;
        _settingsPath = settingsPath ?? Path.Combine(AppContext.BaseDirectory, "AppSettings.json");
    }

    public void Load()
    {
        if (!_fs.FileExists(_settingsPath))
        {
            Current = CreateDefaults();
            Save();
            return;
        }

        try
        {
            var json = _fs.ReadAllText(_settingsPath);
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
        _fs.WriteAllText(_settingsPath, json);
    }

    private AppSettings CreateDefaults()
    {
        var exeDir = AppContext.BaseDirectory;
        var gamePath = DetectGamePath(exeDir);

        return new AppSettings
        {
            GamePath = gamePath,
            ProfilesPath = Path.Combine(exeDir, "Profiles"),
        };
    }

    /// <summary>
    /// Walk up from the exe directory looking for WowClassic.exe — the app typically
    /// lives inside the game folder so the exe is a few levels below the game root.
    /// </summary>
    private string DetectGamePath(string startDir)
    {
        var dir = startDir;
        for (var i = 0; i < 5; i++)
        {
            if (dir is null)
                break;
            if (_fs.FileExists(Path.Combine(dir, "WowClassic.exe")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return string.Empty;
    }
}
