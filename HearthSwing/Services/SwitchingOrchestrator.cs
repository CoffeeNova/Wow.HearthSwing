using System.IO;
using HearthSwing.Models;
using HearthSwing.Models.Profiles;

namespace HearthSwing.Services;

public sealed class SwitchingOrchestrator : ISwitchingOrchestrator
{
    private readonly IProfileManager _profileManager;
    private readonly ICacheProtector _cacheProtector;
    private readonly IProcessMonitor _processMonitor;
    private readonly IFileSystem _fs;
    private readonly IProfileVersionService _versionService;

    public event Action<string>? Log;

    public bool IsCacheLocked => _cacheProtector.IsLocked;
    public int ProtectedFileCount => _cacheProtector.ProtectedFileCount;

    public SwitchingOrchestrator(
        IProfileManager profileManager,
        ICacheProtector cacheProtector,
        IProcessMonitor processMonitor,
        IFileSystem fileSystem,
        IProfileVersionService versionService
    )
    {
        _profileManager = profileManager;
        _cacheProtector = cacheProtector;
        _processMonitor = processMonitor;
        _fs = fileSystem;
        _versionService = versionService;
    }

    public void SwitchTo(ProfileInfo target)
    {
        UnlockCache();
        _profileManager.SwitchTo(target);
    }

    public void UnlockCache()
    {
        if (!_cacheProtector.IsLocked)
            return;

        _cacheProtector.Unlock();
    }

    public int LockForLaunch()
    {
        var wtfPath = _profileManager.WtfPath;
        if (!_fs.DirectoryExists(wtfPath))
            return 0;

        UnlockCache();
        _cacheProtector.Lock(wtfPath);
        return _cacheProtector.ProtectedFileCount;
    }

    public void ForceRestoreCache()
    {
        var wtfPath = _profileManager.WtfPath;
        SeedMissingCacheFiles(wtfPath);
        _cacheProtector.ForceRestore(wtfPath);
    }

    public void RestoreFromSaved()
    {
        UnlockCache();
        _profileManager.RestoreActiveProfile();
    }

    public async Task SaveWithVersioningAsync(
        string profileId,
        bool versioningEnabled,
        CancellationToken ct = default
    )
    {
        if (!_fs.DirectoryExists(_profileManager.WtfPath))
        {
            Log?.Invoke("Warning: WTF folder not found — skipping save.");
            return;
        }

        UnlockCache();

        var profilePath = Path.Combine(_profileManager.ProfilesPath, profileId);
        if (versioningEnabled && _fs.DirectoryExists(profilePath))
            await _versionService.CreateVersionAsync(profileId);

        _profileManager.SaveCurrentAsProfile(profileId);
    }

    public async Task SaveWithVersioningAsync(
        ProfileDescriptor descriptor,
        bool versioningEnabled,
        CancellationToken ct = default
    )
    {
        if (!_fs.DirectoryExists(_profileManager.WtfPath))
        {
            Log?.Invoke("Warning: WTF folder not found — skipping save.");
            return;
        }

        UnlockCache();

        if (versioningEnabled && _fs.DirectoryExists(descriptor.SnapshotPath))
            await _versionService.CreateVersionAsync(descriptor);

        _profileManager.SaveCurrentAsProfile(descriptor);
    }

    public async Task WaitForWowExitAndCleanupAsync(int postExitDelayMs, CancellationToken ct)
    {
        try
        {
            await _processMonitor.WaitForExitAsync(ct);
            await Task.Delay(postExitDelayMs, ct);
            UnlockCache();
        }
        catch (OperationCanceledException) { }
    }

    private void SeedMissingCacheFiles(string wtfPath)
    {
        var profile = _profileManager.DetectCurrentProfile();
        if (profile is null)
            return;

        var profilePath = Path.Combine(_profileManager.ProfilesPath, profile.Id);
        if (!_fs.DirectoryExists(profilePath))
            return;

        var profileCacheFiles = _cacheProtector.CollectCacheFiles(profilePath);
        var seeded = 0;

        foreach (var profileFile in profileCacheFiles)
        {
            var relativePath = Path.GetRelativePath(profilePath, profileFile);
            var wtfFile = Path.Combine(wtfPath, relativePath);

            if (_fs.FileExists(wtfFile))
                continue;

            var dir = Path.GetDirectoryName(wtfFile);
            if (dir is not null && !_fs.DirectoryExists(dir))
                _fs.CreateDirectory(dir);

            _fs.CopyFile(profileFile, wtfFile);
            seeded++;
        }

        if (seeded > 0)
            Log?.Invoke($"Restored {seeded} missing cache file(s) from saved profile.");
    }
}
