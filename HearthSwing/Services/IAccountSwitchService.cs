using HearthSwing.Models.Accounts;

namespace HearthSwing.Services;

/// <summary>
/// Applies saved account snapshots into the live WTF folder.
/// </summary>
public interface IAccountSwitchService
{
    /// <summary>
    /// Absolute path to the live WTF folder.
    /// </summary>
    string WtfPath { get; }

    /// <summary>
    /// Applies the given saved account to the live WTF folder and marks it active.
    /// </summary>
    void SwitchTo(SavedAccountSummary savedAccount);

    /// <summary>
    /// Reapplies the currently active saved account to the live WTF folder.
    /// </summary>
    void RestoreActiveAccount();
}
