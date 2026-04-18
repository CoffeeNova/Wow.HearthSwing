namespace HearthSwing.Models;

public sealed class AppSettings
{
    public string GamePath { get; set; } = string.Empty;

    /// <summary>
    /// Directory that holds profile subfolders (each subfolder = one WTF snapshot).
    /// Default: "Profiles" next to the exe. Can be any absolute path.
    /// </summary>
    public string ProfilesPath { get; set; } = string.Empty;

    public int UnlockDelaySeconds { get; set; } = 120;

    public bool VersioningEnabled { get; set; } = true;

    public int MaxVersionsPerProfile { get; set; } = 5;

    public bool SaveOnExitEnabled { get; set; } = true;

    public bool AutoSaveOnExit { get; set; }
}
