namespace HearthSwing.Models.Accounts;

/// <summary>
/// Identifies a live character folder selected for persistence.
/// </summary>
public sealed record CharacterSaveSelection
{
    /// <summary>
    /// Realm name.
    /// </summary>
    public required string RealmName { get; init; }

    /// <summary>
    /// Character name.
    /// </summary>
    public required string CharacterName { get; init; }
}
