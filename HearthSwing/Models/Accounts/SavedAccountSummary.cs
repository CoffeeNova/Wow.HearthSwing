namespace HearthSwing.Models.Accounts;

/// <summary>
/// Describes a saved account snapshot stored under the configured storage root.
/// </summary>
public sealed record SavedAccountSummary
{
    /// <summary>
    /// Stable storage identifier of the saved account.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Live WoW account name represented by this saved snapshot.
    /// </summary>
    public required string AccountName { get; init; }

    /// <summary>
    /// Absolute path to the saved account root directory.
    /// </summary>
    public required string RootPath { get; init; }

    /// <summary>
    /// Absolute path to the saved live account subtree inside the snapshot.
    /// </summary>
    public required string SnapshotPath { get; init; }

    /// <summary>
    /// UTC timestamp when the saved account entry was first created.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>
    /// UTC timestamp of the latest successful save into this snapshot, if any.
    /// </summary>
    public DateTimeOffset? LastSavedUtc { get; init; }

    /// <summary>
    /// True when this saved account is currently active in the live WTF folder.
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// User-facing display name for the saved account.
    /// </summary>
    public string DisplayName => AccountName;
}
