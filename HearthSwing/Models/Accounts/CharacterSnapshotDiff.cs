namespace HearthSwing.Models.Accounts;

/// <summary>
/// Diff status for a single live character folder under a WoW account.
/// </summary>
public sealed record CharacterSnapshotDiff
{
    /// <summary>
    /// Name of the realm that contains the character.
    /// </summary>
    public required string RealmName { get; init; }

    /// <summary>
    /// Character name.
    /// </summary>
    public required string CharacterName { get; init; }

    /// <summary>
    /// Absolute path to the live character directory.
    /// </summary>
    public required string FolderPath { get; init; }

    /// <summary>
    /// Diff status relative to the saved snapshot.
    /// </summary>
    public required AccountSnapshotDiffStatus Status { get; init; }
}