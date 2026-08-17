
# Continuation Prompt: Remove Accounts, Use Templates as the Only Transfer Method, and Add WTF Change History

Copy this text into a new chat to start the implementation.

---

Continue work on **HearthSwing**, a WPF application (.NET 10, MVVM, win-x64) for
managing World of Warcraft Classic Anniversary settings in the `WTF` folder.
Complete the large refactor according to the prepared plan.

## Read First
1. `.ai/docs/wtf-history-redesign-plan.md` is the complete plan and source of truth. Focus on section 0 (fixed decisions), section 2 (the history service), sections 3-4 (mutation points and orchestrators), sections 5-6 (what to remove and retain), section 7 (UI), and section 12 (phases).
2. `AGENTS.md` (repo root) is the entry point; `.ai/CONTEXT.md` and `.ai/ARCHITECTURE.md` define the C# style, MVVM, DI, and testing conventions. Follow them strictly.

## Task Summary
Remove saved accounts and switching entirely. Transfer settings **only through
templates**. Add **WTF change history**: before *any* overwrite of `WTF` content,
affected subtrees (a character or account-scoped area) are automatically archived
and any point can be restored. Replace the segmented filter in the UI with the
**`Templates | History`** switcher.

## Fixed Decisions (Do Not Revisit)
1. Accounts and Switch are **removed completely**. Restore a prior state through History.
2. Archive the **entire affected account or character subtree** before every mutation.
3. Restore means overwrite all files from the archive, yielding a targeted subtree rollback.
4. The default history limit is **20 entries per target** and it is configurable in Settings.
5. Templates are one shared Templates list with no UI-level type split. `TemplateKind` remains internal for correct application, with an optional small badge.
6. Invariant: **no write to WTF occurs without a preceding History snapshot.**

## Key Architectural Facts (Already Verified in Code; Do Not Re-Research)
- **WTF mutators**: `AccountSwitchService` and `AccountSnapshotSaveService` are to be removed. `TemplateApplyService.ApplyCharacterTemplate`, `ApplyAccountTemplate`, and `ApplySharedAccountTemplate` must receive a history snapshot in the orchestrator before they run. `DirectoryReplacer.ReplaceDirectory` remains, including for restore. `CacheProtector.Lock`, `Unlock`, and `ForceRestore` remain and do not require history.
- **Orchestrators**: `TemplateRestoreOrchestrator` already branches on `IsWowRunning`: no folder swap while running, using `CacheProtector` and `/reload`; full apply while closed. Replace its target-versioning call before apply with a History snapshot. Reduce `SwitchingOrchestrator` to launch and cache operations (`LockForLaunch`, `UnlockCache`, `ForceRestoreCache`, `WaitForWowExitAndCleanupAsync`); remove `SwitchTo`, `RestoreFromSaved`, and `SaveAccountAsync`. Important: `ForceRestoreCache` currently supplements cache from an account snapshot. Remove that supplement so cache restore uses only in-memory `CacheProtector` backups.
- **Archives**: reuse `IArchiveService` / `TarGzArchiveService` (tar.gz) in the history service. `IDirectoryReplacer` is the rollback-aware directory replacement mechanism.
- **WoW models** are records under `Models/WoW/`: `WowInstallation -> WowAccount -> WowRealm -> WowCharacter`, returned by `IWtfInspector.Inspect(gamePath)`. Use them to resolve the live path by account, realm, and character name during restore.
- **Storage under `ProfilesPath`**: templates are `.templates/{id}/`; history is `.history/{sanitizedTargetKey}/` with archives and `index.json`. `.versions/`, `.template-versions/`, `.active-account.json`, and saved-account folders become unused; no migration is needed (see section 9 of the plan).
- All file I/O must use `IFileSystem`. Services are `public sealed class` types with interfaces and are registered as DI singletons in `App.xaml.cs` through `ConfigureServices()`. Logging uses `event Action<string>? Log`, forwarded to `MainViewModel.AppendLog`.
- Add `MaxHistoryEntriesPerTarget` (default 20) to `AppSettings` in `Models/AppSettings.cs`; it supersedes `MaxVersionsPerProfile` and `VersioningEnabled`.

## Hard Constraints
- Never use folder swap while the game is running. Use targeted writes and route cache files through `CacheProtector`.
- A History snapshot must finish before the mutation, synchronously and inside the transaction/rollback boundary.
- Tests use NUnit, AutoFixture with AutoNSubstitute, NSubstitute, and Shouldly. Follow Arrange / Act / Assert and avoid real I/O by mocking `IFileSystem`, `IArchiveService`, `IWtfInspector`, and related abstractions.
- Do not break the remaining template, cache-protection, and launch mechanisms. Add the new behavior through a separate service.

## Start With Phase 1: History Backend
Implement sections 2 and 12 of the plan exactly:
1. Add logic-free models `HistoryEntry` and `HistoryTargetKind` under `Models/`.
2. Add `Services/IChangeHistoryService.cs` and `ChangeHistoryService.cs`:
  `Snapshot(targetKey, kind, sourceFolder, description)` creates an archive, writes `index.json`, and trims to the configured limit; add `List`, `ListAll`, `RestoreAsync`, and `DeleteAsync`. `RestoreAsync` resolves the live path from the descriptor, extracts to a temporary location, and calls `ReplaceDirectory`; it must snapshot the current state before rollback. Dependencies are `IArchiveService`, `IFileSystem`, `IDirectoryReplacer`, `IWtfInspector`, and the limit from `AppSettings`.
3. Add and load `AppSettings.MaxHistoryEntriesPerTarget` with a default of 20.
4. Register `IChangeHistoryService` in `App.xaml.cs`.
5. Add `ChangeHistoryServiceTests`: snapshot creates the archive and index record; trimming respects 20 and a custom limit; `RestoreAsync` overwrites the subtree; live-path resolution works; restore takes a snapshot before rollback.

Then complete phase 2 (integrate History into `TemplateRestoreOrchestrator`), phase 3 (remove the account stack, absorb `ITemplateVersionService`, and reduce `SwitchingOrchestrator`), phase 4 (the `Templates | History` UI), and phase 5 (polish), following section 12 of the plan.

## Commands
- Build: `dotnet build HearthSwing.slnx -c Release`
- Test: `dotnet test HearthSwing.slnx -c Release`

Confirm that you have read the plan, then start phase 1: add the history models,
`IChangeHistoryService` / `ChangeHistoryService`, the settings field, DI, and
tests, then run the tests.
