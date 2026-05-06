using HearthSwing.Models.Accounts;
using HearthSwing.Models.WoW;

namespace HearthSwing.Services;

/// <summary>
/// Compares a live WoW account with an existing saved snapshot.
/// </summary>
public interface IAccountSnapshotDiffService
{
    /// <summary>
    /// Builds a neutral diff model for a live account against the saved snapshot, if any.
    /// </summary>
    AccountSnapshotDiff BuildDiff(WowAccount liveAccount, SavedAccountSummary? savedAccount);
}
