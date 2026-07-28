# Plan: remove "accounts", make templates the only transfer method, and add WTF change history

This document describes the next major HearthSwing redesign. This is a **plan** -
the code is written separately, incrementally, with tests at each step. This
document contains only the target model, affected file list, and phases.

## Implementation status (sync as of 2026-07-28)

- Phases 1-4 are implemented.
- Phase 5 is functionally implemented (including optional cleanup of legacy data)
  and polished for text/UX.
- The document is preserved as a target specification and decision log; sections
  below describe the model and reasons behind the decisions.

---

## 0. Answers to user questions (fixed decisions)

**"Why do we need the account concept and what changes on Switch?"**
Currently, a "saved account" is a snapshot of subtree `WTF/Account/{name}`, and
the **Switch** button physically swaps that account folder in live `WTF` entirely
(rollback-aware replacement) and updates marker `.active-account.json`. This was
originally the app core (switching profiles of multiple people on one PC). But in
practice templates already cover settings transfer, so accounts and templates
duplicate each other. **Decision: remove accounts and Switch completely.**

**"The Settings button in filter is unclear and conflicts with bottom Settings."**
After account removal, only templates remain in the list, and segmented filter
`All / Accounts / Characters / Settings` loses meaning. **Decision: remove the
filter entirely**; replace with clear top switch **`Templates | History`**.
Bottom **Settings** link (app settings) remains the only "Settings".

**Fixed answers (do not re-ask):**
1. **Completely remove** saved accounts and Switch. Settings transfer is only
   through templates. Past-state recovery is through WTF change history.
2. **What to archive before changing WTF:** the full affected account/character
   subtree.
3. **Restore behavior:** overwrite all files from archive, i.e. a pointwise
   (subtree-level) rollback.
4. **History storage:** per-object point limit, default **20**,
   **configurable** in settings.
5. **Templates:** one common **Templates** list, without account/character split in
   UI (internal `TemplateKind` is kept for correct apply behavior, but the list is
   unified with a small type badge).

---

## 1. Target conceptual model

Was (4 overlapping entities): **saved accounts**, **templates**,
**versions** (`.tar.gz` snapshots of account/template), **archives** (low-level tar.gz).

Becomes (2 clear entities + 1 utility):

1. **Templates** - the only way to *capture* and *transfer* settings.
   Capture from a live character/account, apply to any target character/account.
   One flat list.
2. **History (WTF change history)** - before *any* operation that overwrites
   `WTF` content, affected subtree (character folder or account-scoped part)
   is automatically archived into history. Any point can be restored
   (subtree overwrite from archive). Per-object point limit is configurable.
3. **Archive utility** (`IArchiveService` / `TarGzArchiveService`) remains as
   low-level tar.gz and is reused by history.

Key invariant: **no writes to `WTF` happen without a prior snapshot of the
affected subtree in History.**

---

## 2. New service: WTF change history

Introduce a unified snapshot/history service that replaces
`IProfileVersionService` **and** `ITemplateVersionService` (to avoid two version
systems). Working name: `IChangeHistoryService` (confirm during implementation).

```csharp
public enum HistoryTargetKind
{
    WtfCharacter,   // WTF/Account/{acc}/{realm}/{char}
    WtfAccount,     // account-scoped part: SavedVariables + account top-level files
    Template,       // snapshot of template store before Update (internal revision)
}

public sealed record HistoryEntry
{
    public required string TargetKey { get; init; }        // logical target key
    public required HistoryTargetKind Kind { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required string Description { get; init; }       // "Applied template X", "Update template"
    public required string ArchivePath { get; init; }
    public long SizeBytes { get; init; }
    // Logical descriptor to restore live path (account/realm/character name)
    // so we do not store absolute path and can survive GamePath/ProfilesPath changes.
    public string? AccountName { get; init; }
    public string? RealmName { get; init; }
    public string? CharacterName { get; init; }
}

public interface IChangeHistoryService
{
    event Action<string>? Log;

    // Capture subtree snapshot BEFORE overwrite. Returns entry. Trims to limit.
    HistoryEntry Snapshot(string targetKey, HistoryTargetKind kind, string sourceFolder, string description);

    IReadOnlyList<HistoryEntry> List(string targetKey);
    IReadOnlyList<HistoryEntry> ListAll();

    // Pointwise rollback: overwrite subtree (live path is resolved by descriptor) from archive.
    Task RestoreAsync(HistoryEntry entry, CancellationToken ct = default);

    Task DeleteAsync(HistoryEntry entry, CancellationToken ct = default);
}
```

**On-disk storage:**

```
{ProfilesPath}/
  .history/
    {sanitizedTargetKey}/
      index.json                 <- list of HistoryEntry for this target
      20260727_141355.tar.gz     <- subtree snapshot
      ...
  .templates/                    <- unchanged (template store)
```

**`TargetKey` formation** (stable and folder-name-safe):
- character: `wtf/char/{acc}/{realm}/{char}`;
- account-scoped: `wtf/account/{acc}`;
- template revision: `template/{templateId}`.

**Trimming:** after `Snapshot`, keep at most `MaxHistoryEntriesPerTarget` entries
per `TargetKey`, deleting oldest ones (archive + `index.json` entry).

**Restore:** from `AccountName/RealmName/CharacterName` in entry, re-resolve
current live path via `IWtfInspector`; unpack archive into temp folder; replace
live subtree with `IDirectoryReplacer.ReplaceDirectory` (rollback-aware). Before
the restore itself, also snapshot current state (so rollback-of-rollback is possible).

**Service dependencies:** `IArchiveService`, `IFileSystem`, `IDirectoryReplacer`,
`IWtfInspector`, limit from `AppSettings`.

---

## 3. Where to insert snapshot (WTF mutation points)

Full list of current `WTF` mutators (see code audit) and actions:

| Mutator | What it does | Plan |
|---|---|---|
| `AccountSwitchService.SwitchTo/RestoreActiveAccount` | swaps `WTF/Account/{acc}` | **remove** (accounts are removed) |
| `AccountSnapshotSaveService.Save` | saves live account to store | **remove** |
| `TemplateApplyService.ApplyCharacterTemplate` | replaces character folder (+ shared) | **snapshot before call** (in orchestrator) |
| `TemplateApplyService.ApplyAccountTemplate` / `ApplySharedAccountTemplate` | replaces `SavedVariables` + top-level | **snapshot before call** |
| `DirectoryReplacer.ReplaceDirectory` | atomic folder replacement | keep as mechanism (including restore) |
| `CacheProtector.Lock/Unlock/ForceRestore` | read-only + in-memory cache backup | keep; no history required (not user-data overwrite, but live-session protection) |

History snapshotting is done **in the orchestrator** (`TemplateRestoreOrchestrator`),
not inside `TemplateApplyService`, so apply service stays clean/testable and the
"snapshot-before-change" policy lives in one place.

For character template with `includeAccountScoped=true`, capture **two** entries:
`WtfCharacter` (character folder) and `WtfAccount` (account-scoped part), because
both subtrees are overwritten.

---

## 4. Orchestrator changes

### 4.1 `TemplateRestoreOrchestrator`
- Replace current `CreateTargetVersionAsync` call (saved-account version) with
  `IChangeHistoryService.Snapshot(...)` of affected subtree(s) **before**
  `ApplyCharacterTemplate/ApplyAccountTemplate`.
- Remove dependency on `IProfileVersionService`.
- Keep branching by `IsWowRunning` (running: no folder-swap, use
  `CacheProtector` + `/reload`; closed: full apply) and `CancellationToken`.
- `TemplateRestoreOptions.CreateVersionBeforeRestore` -> rename to
  `CreateHistoryPointBeforeRestore` (or remove flag: snapshot is always mandatory -
  see decision 0.1; preferred: **always snapshot**).

### 4.2 `SwitchingOrchestrator` -> reduce to launch/cache
- Remove `SwitchTo`, `RestoreFromSaved`, `SaveAccountAsync` (account operations).
- Keep `LockForLaunch`, `UnlockCache`, `ForceRestoreCache`,
  `WaitForWowExitAndCleanupAsync`.
- Rename interface to `ILaunchCacheOrchestrator` (or keep name but remove account part).
- **Behavior change:** `ForceRestoreCache` currently "tops up" missing cache files
  from account snapshot. With no accounts, this top-up is removed; cache restore
  works only from `CacheProtector` in-memory backups (captured during `Lock`).
  Lock this down in tests and user hint text.

---

## 5. What is removed (account stack)

Services/interfaces:
- `IAccountSwitchService` / `AccountSwitchService`
- `ISavedAccountCatalog` / `SavedAccountCatalog` (+ `.active-account.json` marker)
- `IAccountSnapshotSaveService` / `AccountSnapshotSaveService`
- `IAccountSnapshotDiffService` / `AccountSnapshotDiffService`
- `IAccountSnapshotLayout` / `AccountSnapshotLayout`
- `IProfileVersionService` / `ProfileVersionService` -> replaced by `IChangeHistoryService`
- `ITemplateVersionService` / `TemplateVersionService` -> absorbed by `IChangeHistoryService` (`Template` kind)

Models/types:
- `SavedAccountSummary`, `SavedAccountMetadata`, `ActiveAccountState`
- `Models/Accounts/*` (`AccountSavePlan`, `RealmSaveSelection`, etc.)
- `ProfileFilter` (filter no longer needed)

DI (`App.xaml.cs`): remove registrations above, add `IChangeHistoryService`.

Watch for dangling references: `ISwitchingOrchestrator` uses
`Models.Accounts`; `MainViewModel` uses `SavedAccounts`,
`SwitchSavedAccountCommand`, `SaveAccountCommand`, `AccountSavePlan`,
`IsAccountsMode`; XAML includes dead accounts/templates panels and footer
`Versions` link (`ToggleVersionHistoryCommand`).

---

## 6. What remains

- Full templates stack: `ITemplateCatalog`, `ITemplateCaptureService`,
  `ITemplateApplyService`, `ITemplateTokenizer`, `ITemplateFileClassifier`,
  `TemplateLayout`, `ITemplateRestoreOrchestrator`.
- `ICacheProtector` / `CacheProtector`, `CacheFilePatterns`.
- `IDirectoryReplacer` / `DirectoryReplacer` (used by history too).
- `IArchiveService` / `TarGzArchiveService`.
- `IWtfInspector`, `IProcessMonitor`, `IProcessManager`, `IFileSystem`,
  `ISettingsService`, `IUpdateService`, `IDialogService`, `IUiDispatcher`.

---

## 7. UI: `Templates | History`

### 7.1 Top switcher
Instead of segmented filter, two states: **Templates** and **History**
(`AppMode` replaced with enum `{ Templates, History }` or a pair of bools).

### 7.2 Templates screen
- Main actions: `🚀 Launch WoW`, `🔓 Unlock` (conditional), `➕ New Template`.
- Unified list of template cards (no type split; small Account/Character badge
  stays for clarity).
- Template card actions: **`Apply`** (currently "Restore" - rename),
  `Update`, **`History`** (template revisions through `IChangeHistoryService`,
  `Template` kind), `Rename`, `Delete`.
- Apply dialog UX:
  - `Find target ...` is an **optional filter**, not mandatory manual target input;
  - target itself is selected from list/tree below;
  - if exactly one option remains after filtering, it is auto-selected.
- Empty state: "No templates. Create one from a live character/account".

### 7.3 History screen
- List of restore points grouped by target (character/account):
  time, operation description, size, **Restore** button (and `Delete`).
- Restore is pointwise subtree rollback (see section 2), with confirmation and
  its own snapshot of current state before rollback.
- Empty state: "History is empty. It is populated automatically when templates are
  applied".

### 7.4 Fate of the Restore button
- Existing `ForceRestore` (in-game cache restore, `/reload`) stays as an action in
  launch/status area (only while game is running).
- Existing closed-game "Restore from active account" is removed; its role is
  **History -> Restore**.

### 7.5 Save overlay and account screens
- Remove account save overlay, `AccountSavePlan` selection, saved accounts list,
  dead accounts/templates boards, and footer `Versions` link.
- Template creation (`New Template`) remains the entry point for "saving"
  settings in portable form.

### 7.6 MainViewModel
- Remove: `SavedAccounts`, `SwitchSavedAccountCommand`, `SaveAccountCommand`,
  `AccountSavePlan` state, `IsAccountsMode/IsTemplatesMode`, `ProfileFilter` and
  `SetProfileFilterCommand`, `IsFilter*`.
- Add: `Templates|History` switch, history groups collection
  (`HistoryGroups`) as UI projection, `RestoreHistoryEntryCommand`,
  `DeleteHistoryEntryCommand`, `RefreshHistory`.
- `ProfileCardViewModel` -> rename to `TemplateCardViewModel` (or keep name but
  remove account branch), remove account cards from `RefreshProfiles`.

---

## 8. Settings

`AppSettings`:
- Add `MaxHistoryEntriesPerTarget` (int, default **20**).
- `MaxVersionsPerProfile` -> rename/remove (absorbed by new field).
- `VersioningEnabled`: per decision 0.1, history is **always on**; field is either
  removed, or kept as an advanced on/off toggle (default true).
  Preferred: remove toggle, keep only limit.
- Add numeric field in Settings overlay: "History: points per object".

---

## 9. Migration/cleanup of old data

Code is not in production yet; real data is local only. Minimal plan:
- New catalog: `.history/`. Old `.versions/`, `.template-versions/`,
  `.active-account.json`, and saved account folders under `ProfilesPath`
  become unused.
- No dedicated migration required; optional one-time cleanup of obsolete
  catalogs with confirmation. Default behavior: ignore old data.

---

## 10. Tests (NUnit + AutoFixture(AutoNSubstitute) + NSubstitute + Shouldly, AAA)

- `ChangeHistoryServiceTests`: `Snapshot` creates archive+entry; trimming to N
  (default 20 and arbitrary limit); `RestoreAsync` overwrites subtree;
  live path resolution by descriptor; snapshot-before-rollback.
- `TemplateRestoreOrchestratorTests`: `Snapshot` is called before apply
  (for character with account-scoped part: two entries); running/closed branches;
  CT cancellation.
- `LaunchCacheOrchestratorTests`: launch/lock/unlock/force-restore without
  account top-up.
- Remove/rewrite account tests (switch/save/diff/layout/profile-version).
- `MainViewModelTests`: `Templates|History` switch, `Apply`,
  `RestoreHistoryEntryCommand`, absence of account commands and filter.

---

## 11. Risks and edge cases

1. **Large account-stack deletion** -> clean DI, VM, XAML thoroughly
   (`IsAccountsMode`, `Versions` link, save overlay) so project builds.
2. **Cache Restore behavior** changes (no account top-up) - document this.
3. **Path resolve during rollback**: if account/realm/character was renamed or
   missing in live WTF, show clear error, do not crash.
4. **Disk usage**: trimming is mandatory; account for size (character subtree
   tar.gz is usually small, account-scoped is bigger due to SavedVariables).
5. **Atomicity**: history snapshot must complete **before** mutation; run
   synchronously before `ReplaceDirectory`, wrap in try/rollback.
6. **Game running**: subtree snapshot is safe (read), but writes are pointwise
   only (no folder-swap), same as current running path.

---

## 12. Implementation phases

Each phase includes tests; `dotnet build`/`dotnet test` are green; manual smoke
on real `WTF`.

**Phase 1 - History (backend).**
1. `IChangeHistoryService` + `ChangeHistoryService` (`Snapshot`/`List`/`Restore`/
   `Delete`/trimming), models `HistoryEntry`/`HistoryTargetKind`, `.history/` store.
2. `AppSettings.MaxHistoryEntriesPerTarget` + limit consumption. DI registration.
3. Service tests.

**Phase 2 - Embed history into template apply.**
4. `TemplateRestoreOrchestrator`: snapshot affected subtrees before apply;
   remove `IProfileVersionService`. Tests.

**Phase 3 - Remove account stack.**
5. Remove account services/models/DI; reduce `SwitchingOrchestrator`
   (-> launch/cache) and absorb `ITemplateVersionService`. Fix all references.
   Build green, tests updated.

**Phase 4 - UI: `Templates | History`.**
6. Switcher, Templates screen (Apply/Update/History/Rename/Delete),
   History screen (Restore/Delete), remove account UI and filter.
7. History limit field in Settings overlay.

**Phase 5 - Polishing.**
8. Empty states, confirmations, toasts, hint texts for the new model.
9. Optional cleanup of obsolete catalogs; update `CLAUDE.md`/
   `copilot-instructions.md` for the new conceptual model.

---

## 13. Out of scope (for now)

- Multi-user profile switching (intentionally removed).
- Export/import of templates and history between machines.
- Merge settings instead of overwrite.
- Full live transfer of `SavedVariables` without relog (WoW limitation).

---

## 14. Open small items (status)

1. History service name is fixed: `IChangeHistoryService`.
2. Template history is confirmed in UI as `History` action on template card.
3. History works as always-on; settings control the points-per-object limit.
