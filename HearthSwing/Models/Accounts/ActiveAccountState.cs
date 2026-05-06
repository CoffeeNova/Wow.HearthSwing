namespace HearthSwing.Models.Accounts;

/// <summary>
/// Identifies the saved account snapshot that is currently active in the live WTF folder.
/// </summary>
public sealed record ActiveAccountState
{
    /// <summary>
    /// Stable storage identifier of the saved account.
    /// </summary>
    public required string SavedAccountId { get; init; }

    /// <summary>
    /// Live WoW account name contained by the saved snapshot.
    /// </summary>
    public required string AccountName { get; init; }
}