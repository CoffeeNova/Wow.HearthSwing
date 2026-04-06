using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
    private readonly Dictionary<string, byte[]> _backups = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = [];
    private bool _locked;
    private bool _disposed;

    public CacheProtector(IFileSystem fileSystem)
    {
        _fs = fileSystem;
    }

    public bool IsLocked => _locked;
    public int ProtectedFileCount => _backups.Count;

    public event Action<string>? Log;

    public List<string> CollectCacheFiles(string wtfPath)
    {
        var result = new List<string>();
        if (!_fs.DirectoryExists(wtfPath))
            return result;

        foreach (var pattern in CachePatterns)
        {
            try
            {
                result.AddRange(_fs.GetFiles(wtfPath, pattern, SearchOption.AllDirectories));
            }
            catch (UnauthorizedAccessException) { }
            catch (DirectoryNotFoundException) { }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Creates in-memory backups of all cache files, touches timestamps so the WoW
    /// client considers local data newer than server data, sets read-only, and starts
    /// FileSystemWatchers to restore files if WoW overwrites them.
    /// </summary>
    public void Lock(string wtfPath)
    {
        if (_locked)
            return;

        var files = CollectCacheFiles(wtfPath);
        _backups.Clear();

        var now = DateTime.Now;
        BackupAndProtectFiles(files, now);
        TouchOldCompanions(wtfPath, now);
        StartWatchers(wtfPath);
        _locked = true;
        Log?.Invoke($"Locked {_backups.Count} cache files (read-only + timestamps touched).");
    }

    public void Unlock()
    {
        if (!_locked)
            return;

        StopWatchers();
        RemoveReadOnlyFromBackups();
        _backups.Clear();
        _locked = false;
        Log?.Invoke("Cache files unlocked.");
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
        StartWatchers(wtfPath);
        _locked = true;

        Log?.Invoke(
            $"Force-restored {restored} cache files with fresh timestamps. Type /reload in WoW."
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
                Log?.Invoke($"Warning: could not back up {Path.GetFileName(file)}: {ex.Message}");
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
        var files = CollectCacheFiles(wtfPath);
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
        Log?.Invoke($"Snapshot: {_backups.Count} files captured for restore.");
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
                Log?.Invoke($"Warning: could not restore {Path.GetFileName(file)}: {ex.Message}");
            }
        }
        return restored;
    }

    private void StartWatchers(string wtfPath)
    {
        var accountDir = Path.Combine(wtfPath, "Account");
        var dirsToWatch = new List<string> { wtfPath };
        if (_fs.DirectoryExists(accountDir))
            dirsToWatch.Add(accountDir);

        foreach (var dir in dirsToWatch)
        {
            try
            {
                var watcher = new FileSystemWatcher(dir)
                {
                    IncludeSubdirectories = true,
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
                Log?.Invoke($"Warning: could not create watcher for {dir}: {ex.Message}");
            }
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
            Log?.Invoke(
                $"Restored {Path.GetFileName(e.FullPath)} from backup (sync attempt blocked)."
            );
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Warning: could not restore {Path.GetFileName(e.FullPath)}: {ex.Message}");
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
