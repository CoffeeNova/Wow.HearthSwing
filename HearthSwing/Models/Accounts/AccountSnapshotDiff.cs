namespace HearthSwing.Models.Accounts;

/// <summary>
/// Neutral diff model for planning a selective save of a live WoW account.
/// </summary>
public sealed record AccountSnapshotDiff
{
    /// <summary>
    /// Live WoW account name.
    /// </summary>
    public required string AccountName { get; init; }

    /// <summary>
    /// True when there is no existing saved snapshot for the account.
    /// </summary>
    public bool IsNewAccount { get; init; }

    /// <summary>
    /// Diff status for account-level settings such as root files and SavedVariables.
    /// </summary>
    public required AccountSnapshotDiffStatus AccountSettingsStatus { get; init; }

    /// <summary>
    /// Per-realm diff results for live characters.
    /// </summary>
    public IReadOnlyList<RealmSnapshotDiff> Realms { get; init; } = [];

    /// <summary>
    /// True when at least one selectable node differs from the saved snapshot.
    /// </summary>
    public bool HasChanges =>
        AccountSettingsStatus != AccountSnapshotDiffStatus.Unchanged
        || Realms.Any(realm => realm.Status != AccountSnapshotDiffStatus.Unchanged);
}
