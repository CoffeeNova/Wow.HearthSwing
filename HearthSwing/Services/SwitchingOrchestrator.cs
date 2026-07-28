using System.IO;

namespace HearthSwing.Services;

public sealed class SwitchingOrchestrator : ISwitchingOrchestrator
{
    private const string WtfFolderName = "WTF";

    private readonly ISettingsService _settingsService;
    private readonly ICacheProtector _cacheProtector;
    private readonly IProcessMonitor _processMonitor;
    private readonly IFileSystem _fs;

    public bool IsCacheLocked => _cacheProtector.IsLocked;
    public int ProtectedFileCount => _cacheProtector.ProtectedFileCount;

    public SwitchingOrchestrator(
        ISettingsService settingsService,
        ICacheProtector cacheProtector,
        IProcessMonitor processMonitor,
        IFileSystem fileSystem
    )
    {
        _settingsService = settingsService;
        _cacheProtector = cacheProtector;
        _processMonitor = processMonitor;
        _fs = fileSystem;
    }

    public void UnlockCache()
    {
        if (!_cacheProtector.IsLocked)
            return;

        _cacheProtector.Unlock();
    }

    public int LockForLaunch()
    {
        var wtfPath = GetWtfPath();
        if (!_fs.DirectoryExists(wtfPath))
            return 0;

        UnlockCache();
        _cacheProtector.Lock(wtfPath);

        return _cacheProtector.ProtectedFileCount;
    }

    public void ForceRestoreCache()
    {
        var wtfPath = GetWtfPath();
        _cacheProtector.ForceRestore(wtfPath);
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

    private string GetWtfPath()
    {
        var gamePath = _settingsService.Current.GamePath;
        if (string.IsNullOrWhiteSpace(gamePath))
            return string.Empty;

        return Path.Combine(gamePath, WtfFolderName);
    }

}
