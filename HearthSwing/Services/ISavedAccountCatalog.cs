using HearthSwing.Models.Accounts;

namespace HearthSwing.Services;

/// <summary>
/// Manages saved-account metadata stored under the configured snapshot root.
/// </summary>
public interface ISavedAccountCatalog
{
    /// <summary>
    /// Discovers all saved accounts in storage.
    /// </summary>
    List<SavedAccountSummary> DiscoverAccounts();

    /// <summary>
    /// Gets a saved account by its stable storage identifier.
    /// </summary>
    SavedAccountSummary? GetById(string savedAccountId);

    /// <summary>
    /// Finds a saved account by its live WoW account name.
    /// </summary>
    SavedAccountSummary? FindByAccountName(string accountName);

    /// <summary>
    /// Ensures metadata and storage folders exist for the given live account name.
    /// </summary>
    SavedAccountSummary EnsureAccount(string accountName);

    /// <summary>
    /// Updates the last-saved timestamp for the given saved account.
    /// </summary>
    void UpdateLastSaved(string savedAccountId, DateTimeOffset savedAtUtc);

    /// <summary>
    /// Reads the currently active saved account, if any.
    /// </summary>
    ActiveAccountState? GetActiveAccount();

    /// <summary>
    /// Persists the currently active saved account.
    /// </summary>
    void SetActiveAccount(ActiveAccountState activeAccount);

    /// <summary>
    /// Clears the active saved account marker.
    /// </summary>
    void ClearActiveAccount();
}
