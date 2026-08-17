# Continuation Prompt: Implement the Unified Profiles Mode and Live Template Restore

Copy this text into a new chat to start the implementation.

---

Continue work on **HearthSwing**, a WPF application (.NET 10, MVVM, win-x64) for
switching World of Warcraft Classic Anniversary WTF profiles. Implement the
feature according to the prepared plan.

## Read First
1. `.ai/docs/templates-ux-improvement-plan.md` is the complete plan and source of truth. Focus on section 3 (live restore), section 4 (the unified Profiles tab), section 5 (architecture and signatures), and section 8 (phases).
2. `AGENTS.md` (repo root) is the entry point; `.ai/CONTEXT.md` and `.ai/ARCHITECTURE.md` define the C# style, MVVM, DI, and testing conventions. Follow them strictly.

## Task Summary
Make templates the primary way to transfer settings and merge the Accounts and
Templates tabs into one **Profiles** tab. The key capability is to **apply a
template live while the game is running** through `/reload`, rather than only
when the game is closed.

## Fixed Decisions (Do Not Revisit)
- Deliver the **unified Profiles tab** directly; two tabs are not the end state.
- **Automatically version the target** before restore by default, with a toggle to disable it.
- The default scope is **`Full`** (transfer everything); `CacheOnly` is optional.
- The write mechanism is selected automatically from `IsWowRunning`: **when the game is running, do not swap folders**. Use targeted writes and route the cache subset through `CacheProtector`; use the existing full apply when the game is closed.
- **Character-complete transfer** is required in phase 3: transfer character settings together with related account files, including keybindings, general macros, and account edit-mode settings.

## Key Architectural Facts (Verified in Code)
- **Cache protection** (`ICacheProtector` / `CacheProtector`): `Lock(wtf, account)` backs cache files up in memory, marks them read-only, and starts a `FileSystemWatcher`; `ForceRestore(wtf)` rewrites backups and updates timestamps for `/reload`; `Unlock()` releases all protection. The cache patterns are currently a private static `CachePatterns` member of `CacheProtector`; move them into shared `Services/CacheFilePatterns`.
- **In-game account restore** is `SwitchingOrchestrator.ForceRestoreCache()` -> `SeedMissingCacheFiles` + `CacheProtector.ForceRestore`. It is the reference flow for live template restore.
- **Templates**: `ITemplateApplyService.ApplyAccountTemplate(t, WowAccount)` and `ApplyCharacterTemplate(t, WowCharacter)` currently perform a full replacement (folder swap for a character and overlay for an account), expanding tokens through `ITemplateTokenizer`. Related services include `ITemplateCaptureService` for account/character create and update, `ITemplateCatalog`, `ITemplateVersionService`, `IDirectoryReplacer`, and `ITemplateFileClassifier`.
- All file I/O must use `IFileSystem`. Services are `public sealed class` types with interfaces, registered as DI singletons in `App.xaml.cs` through `ConfigureServices()`, and logged through `ILogger<T>`.
- WoW models are records in `Models/WoW/`: `WowInstallation -> WowAccount -> WowRealm -> WowCharacter`, returned by `IWtfInspector.Inspect(gamePath)`.
- Tokens are `{{CHAR}}` and `{{REALM}}` in file content, and `__CHAR__` and `__REALM__` in folder names. Token matching is ordinal and case-sensitive.
- The current UI is `AppMode { Accounts, Templates }` in the top switcher. Template cards expose Apply, Update, Versions, Rename, and Delete. The target picker is a TreeView of account -> realm -> character plus search; selection is handled by `SelectedItemChanged` in `MainWindow.xaml.cs` code-behind.

## Hard Constraints
- Do not break the existing account switch, full apply, save, cache protection, or versioning mechanisms. Add the new functionality through a separate service path.
- Never use folder swap (`ReplaceDirectory`) while WoW is running. Use only targeted writes and route cache files through `CacheProtector`.
- Tests use NUnit, AutoFixture with AutoNSubstitute, NSubstitute, and Shouldly. Follow Arrange / Act / Assert and avoid real I/O.

## Start With Phase 1: Live Restore Backend
1. Add `Services/CacheFilePatterns.cs`: move the cache-pattern list out of `CacheProtector` and reuse it there.
2. Add `TemplateApplyScope { Full, CacheOnly }` under `Models/Templates/`. Extend `ITemplateApplyService` with a `scope` parameter: `CacheOnly` and `Full` while the game is running must use targeted writes without folder swap; closed-game `Full` keeps current behavior. Update existing calls to pass `Full`. Add tests.
3. Add `TemplateRestoreOptions` (`Scope = Full`, `CreateVersionBeforeRestore = true`) and `ITemplateRestoreOrchestrator` / `TemplateRestoreOrchestrator` under `Services/`. Branch on `IsWowRunning`: running means `Unlock` -> targeted write -> `Lock` -> `ForceRestore` -> `/reload` prompt; closed means full apply. Create the automatic version when enabled. Reuse `ICacheProtector`, `IProcessMonitor`, `IAccountSwitchService.WtfPath`, `ITemplateApplyService`, and the version services. Add tests.
4. Register the services in `App.xaml.cs`.

Then implement phase 2 (unified Profiles tab, Restore From Template button, shared card and picker), phase 3 (character-complete `Shared/`), and phase 4 (Save to template and polish) according to section 8 of the plan.

## Commands
- Build: `dotnet build HearthSwing.slnx -c Release`
- Test: `dotnet test HearthSwing.slnx -c Release`

Confirm that you have read the plan, then start phase 1: add `CacheFilePatterns` and
`TemplateApplyScope` to the apply service with tests, and run the tests.
