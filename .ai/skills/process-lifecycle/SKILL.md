---
name: process-lifecycle
description: External process lifecycle patterns for HearthSwing — the IProcessManager abstraction, ProcessMonitor (detect/launch WowClassic.exe, wait for exit), and the launch-with-cache-protection sequence. Use when working with ProcessMonitor, SystemProcessManager, or any WoW process logic.
---

# Skill: External Process Lifecycle Management

Use when working with `ProcessMonitor` / `SystemProcessManager` or managing any
external process (WoW) in .NET.

## The abstraction seam (IProcessManager / IFileSystem)

All process access goes through `IProcessManager`:

```csharp
public interface IProcessManager
{
    Process[] GetProcessesByName(string name);
    Process? Start(ProcessStartInfo startInfo);
}
```

- Production implementation: `SystemProcessManager` (delegates to
  `Process.GetProcessesByName` / `Process.Start`).
- Unit tests substitute `IProcessManager` with NSubstitute — never start real
  processes in tests.
- `ProcessMonitor` also depends on `IFileSystem` (to verify the exe exists) and
  `ILogger<ProcessMonitor>`.

## ProcessMonitor

Constants: `WowProcessName = "WowClassic"`, `WowExeName = "WowClassic.exe"`.

- `IsWowRunning()` — `GetProcessesByName("WowClassic").Length > 0`.
- `LaunchWow(gamePath)` — validates `Path.Combine(gamePath, "WowClassic.exe")`
  exists (throw `FileNotFoundException` otherwise), then:
  ```csharp
  _processManager.Start(new ProcessStartInfo
  {
      FileName = exePath,
      WorkingDirectory = gamePath,
      UseShellExecute = true,   // required: launch the game normally, no redirect
  });
  ```
- `WaitForExitAsync(ct)` — poll loop: while not cancelled, check
  `GetProcessesByName`; if none, break; otherwise dispose the handles and
  `await Task.Delay(2000, ct)`. Cancellation-aware.

## The launch-with-cache-protection sequence (SwitchingOrchestrator)

Launching WoW from the app follows:

1. `CacheProtector.Lock(wtfPath, accountName)` — protect cache files for launch.
2. `ProcessMonitor.LaunchWow(gamePath)`.
3. Unlock (protection released once the game owns the files).
4. `CacheProtector.ForceRestore(wtfPath)` — restore protected cache from backups
   so the local settings win over server sync.
5. Cleanup after WoW exits (`WaitForExitAsync`).

`SwitchingOrchestrator` is **cache and launch only** — do not reintroduce
account switching responsibilities there.

## Key gotchas

- `UseShellExecute = true` is required to launch WoW as a normal game process;
  redirect flags are not used here.
- Process objects returned by `GetProcessesByName` should be disposed after use.
- The exit wait is a polling loop with `Task.Delay` — do not subscribe to
  `Process.Exited` unless you own the `Process` instance (this codebase does not
  keep long-lived process handles).
- Tests must not depend on real processes — substitute `IProcessManager`.

## Related tests

See `ProcessMonitorTests.cs` and `SwitchingOrchestratorTests.cs` for the
substitute-based patterns (verify `Start` called with correct `ProcessStartInfo`,
verify lock/unlock ordering).