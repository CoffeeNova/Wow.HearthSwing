namespace HearthSwing.Models;

public sealed class ProfileInfo
{
    /// <summary>
    /// Profile identifier — same as the subfolder name inside ProfilesPath.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Display name shown in the UI (defaults to Id).
    /// </summary>
    public string DisplayName => Id;

    /// <summary>
    /// Full path to the profile folder inside ProfilesPath.
    /// </summary>
    public required string FolderPath { get; set; }

    /// <summary>
    /// True when this profile is currently active (its data is in the WTF folder).
    /// </summary>
    public bool IsActive { get; set; }
}
