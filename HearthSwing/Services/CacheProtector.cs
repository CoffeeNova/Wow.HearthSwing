using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HearthSwing.Services;

public sealed class CacheProtector : IDisposable
{
    // Patterns that match server-synced cache files
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

    private readonly Dictionary<string, byte[]> _backups = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = [];
    private bool _locked;
    private bool _disposed;

    public bool IsLocked => _locked;
    public int ProtectedFileCount => _backups.Count;

    public event Action<string>? Log;

    /// <summary>
    /// Scans the WTF folder tree and returns all cache file paths that match known sync patterns.
    /// </summary>
    public static List<string> CollectCacheFiles(string wtfPath)
    {
        var result = new List<string>();
        if (!Directory.Exists(wtfPath))
            return result;

        foreach (var pattern in CachePatterns)
        {
            try
            {
                result.AddRange(Directory.GetFiles(wtfPath, pattern, SearchOption.AllDirectories));
            }
            catch (UnauthorizedAccessException) { }
            catch (DirectoryNotFoundException) { }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Creates in-memory backups of all cache files and sets them read-only.
    /// Starts FileSystemWatchers to restore files if WoW manages to overwrite them.
    /// </summary>
    public void Lock(string wtfPath)
    {
        if (_locked)
            return;

        var files = CollectCacheFiles(wtfPath);
        _backups.Clear();

        // Touch timestamps FIRST — make local files appear newer than server data.
        // WoW compares local file LastWriteTime vs server timestamp; if local is
        // newer the client should prefer local data and skip the server payload.
        var now = DateTime.Now;
        foreach (var file in files)
        {
            try
            {
                _backups[file] = File.ReadAllBytes(file);
                TouchTimestamp(file, now);
                SetReadOnly(file, true);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Warning: could not back up {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        // Also touch .old companion files so the client can't use them as a
        // fallback "older" reference point.
        TouchOldCompanions(wtfPath, now);

        StartWatchers(wtfPath);
        _locked = true;
        Log?.Invoke($"Locked {_backups.Count} cache files (read-only + timestamps touched).");
    }

    /// <summary>
    /// Removes read-only flags and stops watchers. Call after sync window has passed.
    /// </summary>
    public void Unlock()
    {
        if (!_locked)
            return;

        StopWatchers();

        foreach (var file in _backups.Keys)
        {
            try
            {
                if (File.Exists(file))
                    SetReadOnly(file, false);
            }
            catch
            { /* best effort */
            }
        }

        _backups.Clear();
        _locked = false;
        Log?.Invoke("Cache files unlocked.");
    }

    private void StartWatchers(string wtfPath)
    {
        // Watch each Account subfolder recursively
        var accountDir = Path.Combine(wtfPath, "Account");
        var dirsToWatch = new List<string> { wtfPath };
        if (Directory.Exists(accountDir))
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

        // Restore the file from backup
        try
        {
            SetReadOnly(e.FullPath, false);
            File.WriteAllBytes(e.FullPath, backup);
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

    /// <summary>
    /// Overwrites all protected cache files from in-memory backups, touches their
    /// timestamps to NOW, and re-locks them. Use this AFTER WoW has already synced
    /// from the server — the user should then type /reload in game to force the
    /// client to re-read from disk.
    /// </summary>
    public void ForceRestore(string wtfPath)
    {
        if (_backups.Count == 0)
        {
            // No backups loaded — re-collect and snapshot current state
            var files = CollectCacheFiles(wtfPath);
            foreach (var file in files)
            {
                try
                {
                    _backups[file] = File.ReadAllBytes(file);
                }
                catch
                { /* skip */
                }
            }
            Log?.Invoke($"Snapshot: {_backups.Count} files captured for restore.");
            return;
        }

        var now = DateTime.Now;
        var restored = 0;

        StopWatchers();

        foreach (var (file, backup) in _backups)
        {
            try
            {
                SetReadOnly(file, false);
                File.WriteAllBytes(file, backup);
                TouchTimestamp(file, now);
                SetReadOnly(file, true);
                restored++;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Warning: could not restore {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        TouchOldCompanions(wtfPath, now);
        StartWatchers(wtfPath);
        _locked = true;

        Log?.Invoke(
            $"Force-restored {restored} cache files with fresh timestamps. Type /reload in WoW."
        );
    }

    private static void TouchTimestamp(string filePath, DateTime when)
    {
        if (!File.Exists(filePath))
            return;
        // Remove read-only temporarily if needed
        var attrs = File.GetAttributes(filePath);
        var wasReadOnly = (attrs & FileAttributes.ReadOnly) != 0;
        if (wasReadOnly)
            File.SetAttributes(filePath, attrs & ~FileAttributes.ReadOnly);

        File.SetLastWriteTime(filePath, when);
        File.SetLastWriteTimeUtc(filePath, when.ToUniversalTime());

        if (wasReadOnly)
            File.SetAttributes(filePath, attrs);
    }

    private static void TouchOldCompanions(string wtfPath, DateTime when)
    {
        if (!Directory.Exists(wtfPath))
            return;
        // Touch all .old files so WoW doesn't use them as timestamp references
        string[] oldPatterns = ["*.old", "*.bak"];
        foreach (var pattern in oldPatterns)
        {
            try
            {
                foreach (
                    var file in Directory.GetFiles(wtfPath, pattern, SearchOption.AllDirectories)
                )
                    TouchTimestamp(file, when);
            }
            catch
            { /* best effort */
            }
        }
    }

    private static void SetReadOnly(string filePath, bool readOnly)
    {
        if (!File.Exists(filePath))
            return;
        var attrs = File.GetAttributes(filePath);
        if (readOnly)
            File.SetAttributes(filePath, attrs | FileAttributes.ReadOnly);
        else
            File.SetAttributes(filePath, attrs & ~FileAttributes.ReadOnly);
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
}
