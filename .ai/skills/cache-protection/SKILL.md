---
name: cache-protection
description: The CacheProtector domain patterns for HearthSwing — the four protection layers, CacheFilePatterns as the single source of truth, and the live-apply Unlock -> apply -> Lock -> ForceRestore sequence. Use before touching CacheProtector, ICacheProtector, cache file handling, or any live WTF apply.
---

# Skill: Cache Protection

Use when working on `CacheProtector` / `ICacheProtector`, or any code that
writes protected cache files while WoW is running.

## What it does

WoW and the server synchronize `WTF` cache files (`bindings-cache.wtf`,
`config-cache.wtf`, macros, edit-mode layout, etc.) — server sync can overwrite
the local settings the user just applied. `CacheProtector` stops that.

## The four protection layers

1. **Read-only lock** — `FileAttributes.ReadOnly` set on each protected file.
   WoW/OS writes then fail or are ignored.
2. **In-memory backup** — the original file bytes are kept in `_backups`
   (`Dictionary<string, byte[]>` keyed by path, `OrdinalIgnoreCase`).
3. **FileSystemWatcher recovery** — a watcher on the WTF tree fires on
   `Changed`/`Created`; the handler clears read-only, writes the backup bytes
   back, and re-sets read-only.
4. **Timestamp touch** — `LastWriteTime` updated so WoW prefers the local file
   after restore.

## Public surface

```csharp
public interface ICacheProtector : IDisposable
{
    bool IsLocked { get; }
    int ProtectedFileCount { get; }
    List<string> CollectCacheFiles(string wtfPath, string? accountName = null);
    void Lock(string wtfPath, string? accountName = null);
    void Unlock();
    void ForceRestore(string wtfPath);
}
```

- `CollectCacheFiles` — gathers files matching `CacheFilePatterns.All` under the
  WTF tree (account-scoped subtree when `accountName` is given, else whole tree).
- `Lock` — if already locked, `Unlock` first (refresh semantics), then back up +
  protect + start watchers.
- `Unlock` — stop watchers, clear read-only, clear backups, reset scope.
- `ForceRestore` — overwrite all protected files from backups (used before a
  `/reload` so the client re-reads the local settings). If no backups exist it
  snapshots current state instead.
- `Dispose` — unlock if locked, stop watchers.

## `CacheFilePatterns` is the single source of truth

Add/change protected patterns ONLY in `CacheFilePatterns`:

```csharp
public static readonly string[] All = [ "bindings-cache.wtf", "config-cache.wtf", ... ];
public static readonly string[] Tokenizable = [ ... ];
public static bool IsTokenizableCacheFileName(string fileName);
```

`All` = full protected set (includes `cache.md5`).
`Tokenizable` = the subset that templates tokenize (character/realm values);
`cache.md5` is intentionally NOT tokenizable.

## Live apply sequence (running WoW)

When a template is applied while WoW is running, the orchestrator follows
`Unlock -> apply -> Lock -> ForceRestore`:

1. `Unlock()` — release read-only so targeted writes succeed.
2. Apply files in place (never `IDirectoryReplacer` / folder swap).
3. `Lock()` — re-establish protection.
4. `ForceRestore()` — push protected cache files back from the in-memory
   backups, then prompt the user to run `/reload`.

## Threading

`FileSystemWatcher` callbacks (`OnCacheFileChanged`) run on a **threadpool
thread**, never the UI thread. Keep handlers IO-only: `SetReadOnly` +
`WriteAllBytes` + logging. Never touch WPF controls inside watchers.

## Gotchas

- Always clear read-only before overwriting a protected file or deleting a
  staging folder — otherwise writes silently fail.
- `SetReadOnly` uses `IFileSystem.GetAttributes`/`SetAttributes` — never raw
  `File`/`Directory` statics.
- Watchers are created per directory (whole WTF tree or the account subtree)
  with `IncludeSubdirectories = true`, filter `LastWrite | Size | CreationTime`.
- `ForceRestore` with zero backups snapshots current state instead of restoring
  nothing (guarded path).

## Related tests

`CacheProtectorTests.cs` verifies lock/unlock/force-restore/collect behavior
with a substituted `IFileSystem` — no real watchers, no real files.