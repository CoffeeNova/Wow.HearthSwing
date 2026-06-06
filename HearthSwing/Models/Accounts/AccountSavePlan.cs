namespace HearthSwing.Models.Accounts;

/// <summary>
/// Describes which slices of a live WoW account should be persisted into a saved snapshot.
/// </summary>
public sealed record AccountSavePlan
{
    /// <summary>
    /// Live WoW account name that the plan targets.
    /// </summary>
    public required string AccountName { get; init; }

    /// <summary>
    /// True when account-level settings should be updated.
    /// </summary>
    public bool SaveAccountSettings { get; init; }

    /// <summary>
    /// Character folders selected for update.
    /// </summary>
    public IReadOnlyList<CharacterSaveSelection> SelectedCharacters { get; init; } = [];

    /// <summary>
    /// True when the plan would update at least one slice of the account snapshot.
    /// </summary>
    public bool HasSelections => SaveAccountSettings || SelectedCharacters.Count > 0;
}
