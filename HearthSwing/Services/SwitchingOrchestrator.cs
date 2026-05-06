using System.IO;
using HearthSwing.Models.Accounts;
using HearthSwing.Models.WoW;

namespace HearthSwing.Services;

public sealed class SwitchingOrchestrator : ISwitchingOrchestrator
{
    private readonly ISavedAccountCatalog _savedAccountCatalog;
    private readonly IAccountSnapshotSaveService _accountSnapshotSaveService;
    private readonly IAccountSwitchService _accountSwitchService;
    private readonly ICacheProtector _cacheProtector;
    private readonly IProcessMonitor _processMonitor;
    private readonly IFileSystem _fs;
    private readonly IProfileVersionService _versionService;

    public event Action<string>? Log;

    public bool IsCacheLocked => _cacheProtector.IsLocked;
    public int ProtectedFileCount => _cacheProtector.ProtectedFileCount;

    public SwitchingOrchestrator(
        ISavedAccountCatalog savedAccountCatalog,
        IAccountSnapshotSaveService accountSnapshotSaveService,
        IAccountSwitchService accountSwitchService,
        ICacheProtector cacheProtector,
        IProcessMonitor processMonitor,
        IFileSystem fileSystem,
        IProfileVersionService versionService
    )
    {
        _savedAccountCatalog = savedAccountCatalog;
        _accountSnapshotSaveService = accountSnapshotSaveService;
        _accountSwitchService = accountSwitchService;
        _cacheProtector = cacheProtector;
        _processMonitor = processMonitor;
        _fs = fileSystem;
        _versionService = versionService;
    }

    public void SwitchTo(SavedAccountSummary target)
    {
        UnlockCache();
        _accountSwitchService.SwitchTo(target);
    }

    public void UnlockCache()
    {
        if (!_cacheProtector.IsLocked)
            return;

        _cacheProtector.Unlock();
    }

    public int LockForLaunch()
    {
        var wtfPath = _accountSwitchService.WtfPath;
        if (!_fs.DirectoryExists(wtfPath))
            return 0;

        UnlockCache();
        var activeAccount = _savedAccountCatalog.GetActiveAccount();
        if (activeAccount is null)
        {
            _cacheProtector.Lock(wtfPath);
        }
        else
        {
            _cacheProtector.Lock(wtfPath, activeAccount.AccountName);
        }

        return _cacheProtector.ProtectedFileCount;
    }

    public void ForceRestoreCache()
    {
        var wtfPath = _accountSwitchService.WtfPath;
        SeedMissingCacheFiles(wtfPath);
        _cacheProtector.ForceRestore(wtfPath);
    }

    public void RestoreFromSaved()
    {
        UnlockCache();
        if (_savedAccountCatalog.GetActiveAccount() is null)
        {
            Log?.Invoke("Warning: No active saved account to restore.");
            return;
        }

        _accountSwitchService.RestoreActiveAccount();
    }

    public async Task<SavedAccountSummary?> SaveAccountAsync(
        WowAccount liveAccount,
        AccountSavePlan savePlan,
        bool versioningEnabled,
        CancellationToken ct = default
    )
    {
        if (!_fs.DirectoryExists(_accountSwitchService.WtfPath))
        {
            Log?.Invoke("Warning: WTF folder not found — skipping save.");
            return null;
        }

        UnlockCache();

        var existingSavedAccount = _savedAccountCatalog.FindByAccountName(liveAccount.AccountName);
        if (
            versioningEnabled
            && existingSavedAccount is not null
            && _fs.DirectoryExists(existingSavedAccount.RootPath)
        )
            await _versionService.CreateVersionAsync(existingSavedAccount.Id);

        return _accountSnapshotSaveService.Save(liveAccount, savePlan);
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
        var activeAccount = _savedAccountCatalog.GetActiveAccount();
        if (activeAccount is null)
            return;

        SeedMissingCacheFilesFromSavedAccount(wtfPath, activeAccount);
    }

    private void SeedMissingCacheFilesFromSavedAccount(
        string wtfPath,
        ActiveAccountState activeAccount
    )
    {
        var savedAccount = _savedAccountCatalog.GetById(activeAccount.SavedAccountId);
        if (savedAccount is null || !_fs.DirectoryExists(savedAccount.RootPath))
            return;

        var accountCacheFiles = _cacheProtector.CollectCacheFiles(
            savedAccount.RootPath,
            savedAccount.AccountName
        );

        var seeded = 0;
        foreach (var snapshotFile in accountCacheFiles)
        {
            var relativePath = Path.GetRelativePath(savedAccount.RootPath, snapshotFile);
            var liveFile = Path.Combine(wtfPath, relativePath);

            if (_fs.FileExists(liveFile))
                continue;

            var liveDirectory = Path.GetDirectoryName(liveFile);
            if (liveDirectory is not null && !_fs.DirectoryExists(liveDirectory))
                _fs.CreateDirectory(liveDirectory);

            _fs.CopyFile(snapshotFile, liveFile);
            seeded++;
        }

        if (seeded > 0)
            Log?.Invoke($"Restored {seeded} missing cache file(s) from saved account.");
    }
}
