namespace HearthSwing.Services;

public interface ISwitchingOrchestrator
{
    bool IsCacheLocked { get; }
    int ProtectedFileCount { get; }

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
    /// Forces the cache protector to restore in-memory cache backups to WTF.
    /// Intended for use while WoW is running after live edits.
    /// </summary>
    void ForceRestoreCache();

    /// <summary>
    /// Waits for WoW to exit, waits an additional <paramref name="postExitDelayMs"/>
    /// milliseconds for write flushing, then unlocks cache protection.
    /// Completes silently on cancellation.
    /// </summary>
    Task WaitForWowExitAndCleanupAsync(int postExitDelayMs, CancellationToken ct);
}
