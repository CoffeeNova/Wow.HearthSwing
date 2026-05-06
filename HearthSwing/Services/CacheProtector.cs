using System.IO;
using Microsoft.Extensions.Logging;

namespace HearthSwing.Services;

public sealed class CacheProtector : ICacheProtector
{
    private static readonly string[] CachePatterns =
    [
        "bindings-cache.wtf",
        "config-cache.wtf",
        "macros-cache.txt",
        "edit-mode-cache-account.txt",
        "edit-mode-cache-character.txt",
        "tts-cache-account.txt",
        "tts-cache-character.txt",
        "chat-cache.txt",
        "chat-frontend-cache.txt",
        "flagged-cache-account.txt",
        "layout-local.txt",
        "cache.md5",
    ];

    private readonly IFileSystem _fs;
    private readonly ILogger<CacheProtector> _logger;
    private readonly Dictionary<string, byte[]> _backups = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = [];
    private bool _locked;
    private bool _disposed;

    private string? _currentAccountName;

    public CacheProtector(IFileSystem fileSystem, ILogger<CacheProtector> logger)
    {
        _fs = fileSystem;
        _logger = logger;
    }

    public bool IsLocked => _locked;
    public int ProtectedFileCount => _backups.Count;

    public List<string> CollectCacheFiles(string wtfPath, string? accountName = null)
    {
        var result = new List<string>();

        if (!string.IsNullOrWhiteSpace(accountName))
        {
            CollectFromDirectory(
                Path.Combine(wtfPath, "Account", accountName),
                SearchOption.AllDirectories,
                result
            );
        }
        else
        {
            CollectFromDirectory(wtfPath, SearchOption.AllDirectories, result);
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Creates in-memory backups of all scoped cache files, touches timestamps so the WoW
    /// client considers local data newer than server data, sets read-only, and starts
    /// FileSystemWatchers to restore files if WoW overwrites them.
    /// </summary>
    public void Lock(string wtfPath, string? accountName = null)
    {
        if (_locked)
        {
            _logger.LogInformation("Refreshing cache protection for {WtfPath}.", wtfPath);
            Unlock();
        }

        _currentAccountName = accountName;

        var files = CollectCacheFiles(wtfPath, accountName);
        _backups.Clear();

        var now = DateTime.Now;
        BackupAndProtectFiles(files, now);
        TouchOldCompanions(wtfPath, now);
        StartWatchers(wtfPath, accountName);
        _locked = true;
        _logger.LogInformation(
            "Locked {Count} cache files (read-only + timestamps touched).",
            _backups.Count
        );
    }

    public void Unlock()
    {
        if (!_locked)
            return;

        StopWatchers();
        RemoveReadOnlyFromBackups();
        _backups.Clear();
        ClearScopeState();
        _locked = false;
        _logger.LogInformation("Cache files unlocked.");
    }

    /// <summary>
    /// Overwrites all protected cache files from in-memory backups, touches their
    /// timestamps so the client re-reads local data after a /reload in game.
    /// </summary>
    public void ForceRestore(string wtfPath)
    {
        if (_backups.Count == 0)
        {
            SnapshotCurrentState(wtfPath);
            return;
        }

        var now = DateTime.Now;
        StopWatchers();
        var restored = RestoreAllFromBackups(now);
        TouchOldCompanions(wtfPath, now);
        StartWatchers(wtfPath, _currentAccountName);
        _locked = true;

        _logger.LogInformation(
            "Force-restored {Count} cache files with fresh timestamps. Type /reload in WoW.",
            restored
        );
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_locked)
            Unlock();
        StopWatchers();
    }

    private void ClearScopeState()
    {
        _currentAccountName = null;
    }

    private void CollectFromDirectory(
        string directory,
        SearchOption searchOption,
        List<string> result
    )
    {
        if (!_fs.DirectoryExists(directory))
            return;

        foreach (var pattern in CachePatterns)
        {
            try
            {
                result.AddRange(_fs.GetFiles(directory, pattern, searchOption));
            }
            catch (UnauthorizedAccessException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private void BackupAndProtectFiles(List<string> files, DateTime now)
    {
        foreach (var file in files)
        {
            try
            {
                _backups[file] = _fs.ReadAllBytes(file);
                TouchTimestamp(file, now);
                SetReadOnly(file, true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Could not back up {FileName}: {Error}",
                    Path.GetFileName(file),
                    ex.Message
                );
            }
        }
    }

    private void RemoveReadOnlyFromBackups()
    {
        foreach (var file in _backups.Keys)
        {
            try
            {
                if (_fs.FileExists(file))
                    SetReadOnly(file, false);
            }
            catch
            { /* best effort */
            }
        }
    }

    private void SnapshotCurrentState(string wtfPath)
    {
        var files = CollectCacheFiles(wtfPath, _currentAccountName);
        foreach (var file in files)
        {
            try
            {
                _backups[file] = _fs.ReadAllBytes(file);
            }
            catch
            { /* skip */
            }
        }
        _logger.LogInformation("Snapshot: {Count} files captured for restore.", _backups.Count);
    }

    private int RestoreAllFromBackups(DateTime now)
    {
        var restored = 0;
        foreach (var (file, backup) in _backups)
        {
            try
            {
                SetReadOnly(file, false);
                _fs.WriteAllBytes(file, backup);
                TouchTimestamp(file, now);
                SetReadOnly(file, true);
                restored++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    "Could not restore {FileName}: {Error}",
                    Path.GetFileName(file),
                    ex.Message
                );
            }
        }
        return restored;
    }

    private void StartWatchers(string wtfPath, string? accountName)
    {
        foreach (var (dir, includeSubdirs) in BuildWatchTargets(wtfPath, accountName))
            TryAddWatcher(dir, includeSubdirs);
    }

    private IEnumerable<(string directory, bool includeSubdirectories)> BuildWatchTargets(
        string wtfPath,
        string? accountName
    )
    {
        return !string.IsNullOrWhiteSpace(accountName)
            ? [(Path.Combine(wtfPath, "Account", accountName), true)]
            : [(wtfPath, true)];
    }

    private void TryAddWatcher(string directory, bool includeSubdirectories)
    {
        if (!_fs.DirectoryExists(directory))
            return;

        try
        {
            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = includeSubdirectories,
                NotifyFilter =
                    NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
            };
            watcher.Changed += OnCacheFileChanged;
            watcher.Created += OnCacheFileChanged;
            _watchers.Add(watcher);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Could not create watcher for {Directory}: {Error}",
                directory,
                ex.Message
            );
        }
    }

    private void StopWatchers()
    {
        foreach (var w in _watchers)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
        _watchers.Clear();
    }

    private void OnCacheFileChanged(object sender, FileSystemEventArgs e)
    {
        if (!_locked)
            return;
        if (!_backups.TryGetValue(e.FullPath, out var backup))
            return;

        try
        {
            SetReadOnly(e.FullPath, false);
            _fs.WriteAllBytes(e.FullPath, backup);
            SetReadOnly(e.FullPath, true);
            _logger.LogInformation(
                "Restored {FileName} from backup (sync attempt blocked).",
                Path.GetFileName(e.FullPath)
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Could not restore {FileName}: {Error}",
                Path.GetFileName(e.FullPath),
                ex.Message
            );
        }
    }

    private void TouchTimestamp(string filePath, DateTime when)
    {
        if (!_fs.FileExists(filePath))
            return;

        var attrs = _fs.GetAttributes(filePath);
        var wasReadOnly = (attrs & FileAttributes.ReadOnly) != 0;
        if (wasReadOnly)
            _fs.SetAttributes(filePath, attrs & ~FileAttributes.ReadOnly);

        _fs.SetLastWriteTime(filePath, when);
        _fs.SetLastWriteTimeUtc(filePath, when.ToUniversalTime());

        if (wasReadOnly)
            _fs.SetAttributes(filePath, attrs);
    }

    /// <summary>
    /// Touch .old/.bak companion files so WoW can't use them as older timestamp
    /// reference points to justify re-syncing from the server.
    /// </summary>
    private void TouchOldCompanions(string wtfPath, DateTime when)
    {
        if (!_fs.DirectoryExists(wtfPath))
            return;

        string[] oldPatterns = ["*.old", "*.bak"];
        foreach (var pattern in oldPatterns)
        {
            try
            {
                foreach (var file in _fs.GetFiles(wtfPath, pattern, SearchOption.AllDirectories))
                    TouchTimestamp(file, when);
            }
            catch
            { /* best effort */
            }
        }
    }

    private void SetReadOnly(string filePath, bool readOnly)
    {
        if (!_fs.FileExists(filePath))
            return;
        var attrs = _fs.GetAttributes(filePath);
        if (readOnly)
            _fs.SetAttributes(filePath, attrs | FileAttributes.ReadOnly);
        else
            _fs.SetAttributes(filePath, attrs & ~FileAttributes.ReadOnly);
    }
}
