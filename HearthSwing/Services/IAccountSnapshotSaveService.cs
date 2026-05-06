using HearthSwing.Models.Accounts;
using HearthSwing.Models.WoW;

namespace HearthSwing.Services;

/// <summary>
/// Persists full or selective account snapshots into saved-account storage.
/// </summary>
public interface IAccountSnapshotSaveService
{
    /// <summary>
    /// Saves the selected slices of the live account into the corresponding saved snapshot.
    /// A brand-new saved account is always captured in full.
    /// </summary>
    SavedAccountSummary Save(WowAccount liveAccount, AccountSavePlan savePlan);
}