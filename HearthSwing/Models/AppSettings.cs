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

    public int MaxHistoryEntriesPerTarget { get; set; } = 20;

    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double? WindowWidth { get; set; }

    public double? WindowHeight { get; set; }

    public bool StartMaximized { get; set; }
}
