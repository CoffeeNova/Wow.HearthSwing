using HearthSwing.Models;
using HearthSwing.Models.Profiles;

namespace HearthSwing.Services;

public interface ISwitchingOrchestrator
{
    event Action<string>? Log;

    bool IsCacheLocked { get; }
    int ProtectedFileCount { get; }

    /// <summary>
    /// Unlocks any active cache protection then delegates to <see cref="IProfileManager.SwitchTo"/>.
    /// </summary>
    void SwitchTo(ProfileInfo target);

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
    /// Optionally creates a version snapshot of the current legacy profile, then saves the
    /// current WTF state under the given profile ID.
    /// </summary>
    Task SaveWithVersioningAsync(
        string profileId,
        bool versioningEnabled,
        CancellationToken ct = default
    );

    /// <summary>
    /// Optionally creates a scope-aware version snapshot, then saves the current WTF state
    /// using the given <paramref name="descriptor"/> (PerAccount or PerCharacter granularity).
    /// </summary>
    Task SaveWithVersioningAsync(
        ProfileDescriptor descriptor,
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
