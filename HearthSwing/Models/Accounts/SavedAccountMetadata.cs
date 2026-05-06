namespace HearthSwing.Models.Accounts;

/// <summary>
/// Metadata persisted beside a saved account snapshot.
/// </summary>
public sealed record SavedAccountMetadata
{
    /// <summary>
    /// Stable storage identifier of the saved account.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Live WoW account name contained by the saved snapshot.
    /// </summary>
    public required string AccountName { get; init; }

    /// <summary>
    /// UTC timestamp when the saved account entry was first created.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>
    /// UTC timestamp of the latest successful save into this snapshot, if any.
    /// </summary>
    public DateTimeOffset? LastSavedUtc { get; init; }
}
