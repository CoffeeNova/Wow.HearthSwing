using HearthSwing.Models.Accounts;
using HearthSwing.Models.WoW;

namespace HearthSwing.Services;

public interface ISwitchingOrchestrator
{
    event Action<string>? Log;

    bool IsCacheLocked { get; }
    int ProtectedFileCount { get; }

    /// <summary>
    /// Unlocks any active cache protection then applies the selected saved account.
    /// </summary>
    void SwitchTo(SavedAccountSummary target);

    /// <summary>
    /// Unlocks cache protection. No-op if cache is not currently locked.
    /// </summary>
    void UnlockCache();

    /// <summary>
    /// Unlocks any active protection, then locks the current WTF folder for WoW launch.
    /// Returns the number of files protected, or 0 if the WTF folder was not found.
    /// </summary>
    int LockForLaunch();

    /// <summary>
    /// Seeds missing cache files from the current profile snapshot into WTF, then
    /// forces the cache protector to restore all in-memory backups to disk.
    /// Intended for use while WoW is running.
    /// </summary>
    void ForceRestoreCache();

    /// <summary>
    /// Unlocks cache protection and restores the active profile from its last snapshot.
    /// Intended for use while WoW is not running.
    /// </summary>
    void RestoreFromSaved();

    /// <summary>
    /// Optionally creates a version of the existing saved account, then persists the selected
    /// slices of the live account into saved-account storage.
    /// </summary>
    Task<SavedAccountSummary?> SaveAccountAsync(
        WowAccount liveAccount,
        AccountSavePlan savePlan,
        bool versioningEnabled,
        CancellationToken ct = default
    );

    /// <summary>
    /// Waits for WoW to exit, waits an additional <paramref name="postExitDelayMs"/>
    /// milliseconds for write flushing, then unlocks cache protection.
    /// Completes silently on cancellation.
    /// </summary>
    Task WaitForWowExitAndCleanupAsync(int postExitDelayMs, CancellationToken ct);
}
