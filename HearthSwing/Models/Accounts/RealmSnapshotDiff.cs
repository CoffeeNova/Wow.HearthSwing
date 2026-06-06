namespace HearthSwing.Models.Accounts;

/// <summary>
/// Diff status for all live characters under a realm.
/// </summary>
public sealed record RealmSnapshotDiff
{
    /// <summary>
    /// Realm name.
    /// </summary>
    public required string RealmName { get; init; }

    /// <summary>
    /// Aggregate diff status of the realm's live characters.
    /// </summary>
    public required AccountSnapshotDiffStatus Status { get; init; }

    /// <summary>
    /// Per-character diff results for the realm.
    /// </summary>
    public IReadOnlyList<CharacterSnapshotDiff> Characters { get; init; } = [];
}
