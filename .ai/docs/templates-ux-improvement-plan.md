# Plan: Unified Profiles mode and on-the-fly template application

This document describes the target HearthSwing enhancement: **on-the-fly template
application directly in-game** (via `/reload`) and **merging the Accounts and
Templates tabs into a single Profiles mode**. This is a plan; the code is written
separately, incrementally, with tests at every step.

## 0. Actual implementation status

Current status of the repository code:

1. **Phase 1**: completed.
2. **Phase 2**: completed (Profiles mode and restore flow are available).
3. **Phase 3**: completed (`Shared/` participates in character restore; a full transfer toggle for the account-scoped part was added).
4. **Phase 4**: completed:
  - account/character template save options were added to the save overlay;
  - post-save template capture/update was implemented;
  - inline rename was implemented for Profiles cards;
  - short toast notifications were added;
  - key confirmation texts were unified via constants;
  - window size/position persistence was added.
5. **Post-audit adjustments**: completed:
  - when the game is running, `Full` restore runs without folder-swap (pointwise write);
  - restore is no longer blocked by the entry-point guard;
  - the restore overlay now provides `Scope` selection (`Full` / `CacheOnly`) and an auto-version flag;
  - a `CancellationToken` from ViewModel is passed into restore;
  - guaranteed `Lock + ForceRestore` in `finally` was added for the running branch;
  - `Lock` runs with `accountName`;
  - post-restore status now accounts for game state (`/reload` hint only in the running scenario);
  - `writeScope` tautology was removed in the apply service;
  - when saved-account mapping is missing for auto-version, a warning is raised in the UI log.

### Nuances of the current implementation

1. In Profiles mode, inline rename works on a template card; the legacy rename overlay is preserved for old Templates mode (compatibility).
2. "Also save templates" options in the save overlay are shown in the changed realms/characters selection block.
3. For account template post-save capture, execution happens only if `SaveAccountSettings` is selected; otherwise the operation is skipped and logged.
4. Restore from a template card and from `Restore From Template` is available both when the game is closed and running; the running scenario uses the in-game-safe path without folder-swap.

> **Decision status (answers to open questions).**
> 1. We target **a single unified Profiles tab immediately** (do not keep two tabs as the end state).
> 2. **Auto-versioning of the target** before restore is **enabled by default**, and can be disabled.
> 3. **Default apply scope is `Full`** (transfer everything), `CacheOnly` is optional.
> 4. **Full transfer "like a character"** (character + related account files) is **mandatory**; the exact implementation timing is up to delivery (within phases below).

---

## 1. Original intent and current pain

**Intent.** Templates are like profile saving (which already exists for
accounts), but not "a full save tied to one specific character". Instead, they
are **portable anonymized templates** applied "on the fly" to different accounts
and characters. This should become the **primary** way to work with settings.

**Pain.** Right now, you cannot apply a template to a character/account **while
the game is running** to restore macros/settings and see the result via
`/reload`. Users have to do this "trick":

| Step | Now |
|---|---|
| 0 | Two characters on different accounts; settings must be moved from char1 to char2 |
| 1 | WoW closed -> create/update template from char1 |
| 2 | Apply template to char2 |
| 3 | Save char2 profile through the Accounts tab |
| 4 | Launch game, log in as char2, press Restore, then run `/reload` in-game |

Four manual steps plus a game restart just to transfer macros.

---

## 2. Why on-the-fly does not work now (code analysis)

The key is understanding **why account Restore-during-game works, but template
application does not.**

Account Restore works through `ICacheProtector` and `ISwitchingOrchestrator`:

- On game launch, `LockForLaunch()` -> `CacheProtector.Lock(wtf, account)` makes
  **in-memory backups of cache files**, sets them read-only, and starts
  `FileSystemWatcher` so server sync cannot overwrite local files.
- `ForceRestoreCache()` (Restore button while game is running) ->
  `CacheProtector.ForceRestore(wtf)` **rewrites cache files from backups to disk
  and updates timestamps**, after which the player runs `/reload` and WoW rereads
  local files.
- Protected file set is fixed in `CacheProtector.CachePatterns`:
  `bindings-cache.wtf`, `config-cache.wtf`, `macros-cache.txt`,
  `edit-mode-cache-account.txt`, `edit-mode-cache-character.txt`,
  `tts-cache-*.txt`, `chat-cache.txt`, `chat-frontend-cache.txt`,
  `flagged-cache-account.txt`, `layout-local.txt`, `cache.md5`.

**What template application does now** (`ITemplateApplyService`):

- `ApplyCharacterTemplate` is a **full replacement of the character folder**
  (`IDirectoryReplacer.ReplaceDirectory`) with token expansion.
- `ApplyAccountTemplate` is an account settings overlay (SavedVariables as a whole
  + top-level files).
- In `MainViewModel`, application is **blocked by `GuardWowRunning`** - only when
  the game is closed.
- **No integration with `CacheProtector`**: files are written outside protection.
  If the game is running, changes may not be picked up by `/reload` (backups and
  timestamps are not refreshed, watcher may roll back edits), or may conflict with
  open file handles.

**Conclusion.** Template application must go through the same pipeline as account
Restore: cache files via `CacheProtector` (refresh backups, timestamps,
`/reload`), while full transfer during gameplay must use pointwise writes without
folder-swap.

---

## 3. Key mechanism: Restore From Template (on-the-fly application)

### 3.1 Apply scope (`TemplateApplyScope`) - default is `Full`

The user chooses transfer volume; default is **`Full`**:

| Scope | What we transfer | When |
|---|---|---|
| **`Full`** (default) | All template files (SavedVariables, config, cache, etc.) | Full settings transfer |
| **`CacheOnly`** (option) | Only `CacheFilePatterns` (macros, keybinds, edit-mode, layout...) | Fast/safe transfer of what `/reload` picks up |

```csharp
public enum TemplateApplyScope
{
    Full,       // all files (default)
    CacheOnly,  // cache patterns only (what is read via /reload)
}
```

### 3.2 Adapting the mechanism to game state

Write mechanism is chosen **automatically by `IsWowRunning`** - the user only
sets `scope`:

| Scope x State | Mechanism |
|---|---|
| `Full` + **WoW closed** | Full replacement: `ReplaceDirectory` (character) / overlay (account). As now. |
| `Full` + **WoW running** | **No folder-swap**: pointwise overwrite of **all** template files into the live folder; cache subset goes through `CacheProtector` (for `/reload`). Non-cache (`SavedVariables`) applies on next **login/relog**. |
| `CacheOnly` + any | Write cache subset only; when running, through `CacheProtector` + `/reload`. |

**Why folder-swap is not allowed while running.** `ReplaceDirectory` deletes and
recreates a character folder - dangerous with game-open files (handles,
overwrite-on-logout). Therefore in-game: pointwise file writes only.

### 3.3 Cache subset pipeline while game is running

```
RestoreLive(cacheFiles):
  wtf = AccountSwitchService.WtfPath
  1. CacheProtector.Unlock()                       // remove read-only, stop watcher, clear backups
  2. write expanded cache files to the live folder // pointwise, no folder-swap
  3. CacheProtector.Lock(wtf, target.AccountName)  // re-create backups (now = template content), read-only, watcher
  4. CacheProtector.ForceRestore(wtf)              // rewrite from backups + refresh timestamps
  5. Log("Done - type /reload in WoW")
```

Steps 1/3/4 **already exist** in `CacheProtector`/`SwitchingOrchestrator` - we
reuse them. New part is only pointwise write (step 2) and, for `Full`-while-
running, additional writes of non-cache files to disk (apply at next login).

### 3.4 Shared cache pattern list

Currently `CachePatterns` is a private `static` in `CacheProtector`. Move it to
shared `CacheFilePatterns` (static class in `Services/`) so `CacheProtector`,
`TemplateApplyService` (`CacheOnly` / `Full`-while-running modes), and optionally
`ITemplateFileClassifier` use **one source of truth**.

### 3.5 New orchestrator

To avoid bloating `ISwitchingOrchestrator` (it is account-focused), introduce a
thin `ITemplateRestoreOrchestrator` that reuses `ICacheProtector`,
`IProcessMonitor`, `IAccountSwitchService` (for `WtfPath`),
`ITemplateApplyService`, and (for auto-versioning) `ITemplateVersionService`:

```csharp
public sealed record TemplateRestoreOptions
{
    public TemplateApplyScope Scope { get; init; } = TemplateApplyScope.Full; // default Full
    public bool CreateVersionBeforeRestore { get; init; } = true;             // auto-version ON by default
}

public interface ITemplateRestoreOrchestrator
{
    event Action<string>? Log;

    // Branching by IsWowRunning inside:
    //   running -> pointwise write (+ cache via Unlock/Lock/ForceRestore) + /reload hint
    //   closed  -> full apply (folder-swap/overlay) for Full
    Task RestoreCharacterTemplateAsync(TemplateSummary template, WowCharacter target, TemplateRestoreOptions options);
    Task RestoreAccountTemplateAsync(TemplateSummary template, WowAccount target, TemplateRestoreOptions options);
}
```

Auto-versioning of the target before restore (ON by default): capture a target
version before writing. For account -
`IProfileVersionService.CreateVersionAsync(savedAccountId)` (if the target is a
saved account) or a snapshot of current state; for template -
`ITemplateVersionService`. Detail during implementation; the interface already
includes `CreateVersionBeforeRestore`.

### 3.6 How this removes the user trick

| Was (4 steps + restart) | Becomes |
|---|---|
| 1. Create/update template from char1 | 1. (one-time) Create/update template from char1 |
| 2. Apply to char2 | 2. **In-game on char2**: `Restore From Template` -> choose char1 template -> `/reload` |
| 3. Save char2 profile | - |
| 4. Launch, login, Restore, /reload | - |

One in-game step instead of four with a restart.

---

## 4. Target model: single **Profiles** tab

We target a unified mode immediately (Decision #1). Tab switcher
`Accounts | Templates` is replaced by a single **Profiles** list.

### 4.1 What the user sees

- One card list with **filter**: `All | Accounts | Characters | Account settings`
  (saved accounts / character templates / account templates).
- **Main screen actions** (always available): `Launch WoW`, `Restore`
  (cache from active account - as now), **`Restore From Template`** (new), `Save`.
- **Card actions** depend on type:
  - **Account** (saved): `Switch` (activate), `Save/Update`, `Versions`, `Rename`, `Delete`.
  - **Template** (character/account): `Restore` (= apply, in-game-aware), `Update`, `Versions`, `Rename`, `Delete`.

### 4.2 Storage: do not merge physically, unify representation

To reduce risk, **two physical stores remain** (saved accounts in
`<ProfilesPath>/<id>/`, templates in `<ProfilesPath>/.templates/<id>/`). We unify
**UX and action set**, not disk format. For the list, introduce a common card VM
type:

```csharp
public enum ProfileCardKind { Account, CharacterTemplate, AccountTemplate }

public sealed class ProfileCardViewModel // projection for the unified list
{
    public required string Id { get; init; }
    public required ProfileCardKind Kind { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }        // "source" / activity
    public DateTimeOffset? UpdatedAt { get; init; }
    // + available action flags/badges
}
```

### 4.3 `Save` also saves template(s)

In account save overlay, add checkboxes:
"Also save **account template**" and "Also save **character template** for:
[character selection from tree]". Technically, after
`AccountSnapshotSaveService.Save(...)`, call
`TemplateCaptureService.Create/Update*Template(...)` for selected entities.
Default is off (avoid implicit template proliferation), but easy to enable.

### 4.4 Full transfer "like a character" (mandatory, Decision #4)

Problem: character settings are physically split between **character folder**
(char macros, edit-mode-character, layout, character config) and **account
folder** (keybinds `bindings-cache.wtf`, General macros, edit-mode-account,
tts-account). To "make it exactly like char1", both levels must be transferred.

**Solution: `character-complete` template:** when creating a character template,
optionally capture **account-scoped cache files** used by that character and put
them into template subfolder `Shared/` (anonymized where applicable). On restore:

1. character files -> target character folder;
2. `Shared/` account files -> target account folder (**with warning**:
   overwrites account-wide data shared by all account characters - known risk);
3. cache subsets of both levels -> through `CacheProtector` (`/reload`).

Flag "Full transfer (including account keybinds/General macros)" in create/
restore overlay. It can be suggested as enabled by default for character
templates, since this matches the expectation to "transfer everything".

---

## 5. Architecture and code changes

### 5.1 Services

| File | Change |
|---|---|
| `Services/CacheFilePatterns.cs` (new) | Extract cache pattern list; use in `CacheProtector` and `TemplateApplyService`. |
| `Services/ICacheProtector.cs` / `CacheProtector.cs` | Use `CacheFilePatterns`. Re-expose `Unlock`/`Lock`/`ForceRestore`. Optional `Rebaseline(wtf, account)` = `Unlock`+`Lock`. |
| `Services/ITemplateApplyService.cs` / `TemplateApplyService.cs` | Add `TemplateApplyScope` parameter; `CacheOnly` and `Full`-while-running perform pointwise writes (no folder-swap); `Full`-closed behaves as now. |
| `Services/ITemplateCaptureService.cs` / `TemplateCaptureService.cs` | Optional capture of account-scoped cache files into `Shared/` (`character-complete`). |
| `Services/ITemplateRestoreOrchestrator.cs` / `TemplateRestoreOrchestrator.cs` (new) | Branching by `IsWowRunning`; auto-versioning; reuses `CacheProtector` + `AccountSwitchService.WtfPath` + `ProcessMonitor` + version services. |
| `App.xaml.cs` | DI registration for new services (singleton). |

### 5.2 Models

- `TemplateApplyScope` (enum): `Full` / `CacheOnly`.
- `TemplateRestoreOptions` (record): `Scope`, `CreateVersionBeforeRestore`, (optional) `IncludeAccountScoped`.
- `ProfileCardKind` (enum) + `ProfileCardViewModel` (for unified list).

### 5.3 ViewModel (`MainViewModel`)

- Replace `AppMode { Accounts, Templates }` with a unified Profiles list with
  filter (preserve backward command compatibility where possible).
- New `RestoreFromTemplateCommand`: choose template -> target (tree+search) ->
  `scope` (default Full) -> auto-version (ON, toggle) -> confirm ->
  `ITemplateRestoreOrchestrator`.
- **Remove WoW-running guard** for on-the-fly restore (in-game is a primary scenario).
- Subscribe to new orchestrator `Log` through `AppendLog`.
- Reuse `TargetCharacterTree` / `TargetAccounts` / `TargetSearchText`.

### 5.4 View (`MainWindow.xaml`) and shared UI components

- Unified Profiles list + filter; **Restore From Template** button on the main
  screen (next to Launch/Restore).
- Move card and target picker (tree+search) into **shared components**
  (`UserControl` / `DataTemplate` in `App.xaml`).
- Keep dark theme and existing styles (`ActionBtn`, `CardBg`, `BoolToVis`,
  `HierarchicalDataTemplate`).

### 5.5 Tests (NUnit + AutoFixture(AutoNSubstitute) + NSubstitute + Shouldly, AAA)

- `TemplateApplyServiceTests`: `Full`-closed -> folder-swap/overlay;
  `Full`-while-running and `CacheOnly` -> pointwise write without swap; cache
  subset is correct.
- `TemplateRestoreOrchestratorTests`: "running" branch ->
  `Unlock`->write->`Lock`->`ForceRestore`; "closed" -> full apply;
  auto-version is called when `CreateVersionBeforeRestore=true`.
- `TemplateCaptureServiceTests`: `character-complete` captures `Shared/`.
- `MainViewModelTests`: `RestoreFromTemplateCommand` (picker, orchestrator call,
  not blocked when `IsWowRunning`, default `Scope=Full`, auto-version ON),
  Profiles filter, no regressions in legacy commands.

---

## 6. General UI/UX improvements

1. **Unified target picker** (account->realm->character tree + search) for all
   scenarios: save selection, create/update template, restore-from-template.
2. **Unified card** for accounts and templates: type badge, name, "source",
   "updated", unified button set and color logic.
3. **Explicit states**: active account, "cache locked", "WoW running"; short
   toasts instead of long log entries for frequent actions.
4. **Inline rename** instead of modal overlay.
5. **Empty states** with next-step guidance.
6. **Persist window size/position**; list search for large card counts.
7. **Contextual How to use** already switches by mode; update for Profiles.
8. **Unified confirmation texts** (overwriting account settings, etc.).

---

## 7. Risks and edge cases

1. **`/reload` vs SavedVariables.** `/reload` reliably rereads **cache files**
   (macros, keybinds, edit-mode, layout). `SavedVariables/*.lua` apply on
   **login/relog**, not on every `/reload`. Therefore for `Full` while running:
   macros/keybinds/edit-mode apply immediately via `/reload`, the rest on next
   login. Show this in UI hint.
2. **Account scope vs character scope.** Keybinds and General macros are account-
   level; char macros/edit-mode-character/layout are character-level.
   `character-complete` (Section 4.4) transfers both levels; account-level
   overwrite affects all account characters - warn explicitly.
3. **Files open by game.** While running, pointwise writes only (no folder-swap);
   wrap in try/rollback, remove read-only before write.
4. **Cross-volume / read-only.** Reuse existing techniques
   (`ClearReadOnlyAttributes`, copy instead of move across volumes).
5. **Tokenization of account macros.** Known limitation: character name in
   General macros (account level) is not tokenized (an account has no single
   character). For `character-complete`, `Shared/` donor is a specific character,
   so tokenization is valid there; `[@player]` idiom is recommended.
6. **Compatibility.** Do not break old mechanisms (switch, full apply, save,
   cache protection); new functionality goes in a separate service branch.

---

## 8. Phased implementation plan

All phases lead to a single Profiles tab; each step includes tests;
`dotnet build`/`dotnet test` are green; manual smoke on real WTF.

**Phase 1 - core: on-the-fly restore (backend).** ✅ Completed
1. `CacheFilePatterns` (extract from `CacheProtector`, reuse).
2. `TemplateApplyService` + `TemplateApplyScope` (`Full`-while-running and `CacheOnly` are pointwise) + tests.
3. `ITemplateRestoreOrchestrator` (+ `TemplateRestoreOptions`: `Full` by default, auto-version ON) + tests.
4. DI registration.

**Phase 2 - unified Profiles tab (UI).** ✅ Completed
5. `MainViewModel`: Profiles list + filter; `RestoreFromTemplateCommand`/`OpenApplyTemplateCommand`; tests.
6. `MainWindow.xaml`: unified Profiles list, **Restore From Template** button on main screen, shared card control + shared target picker.
7. Smoke scenarios are covered by automated restore/apply/capture flow tests.

**Phase 3 - `character-complete` transfer (mandatory).** ✅ Completed
8. Capture account-scoped cache into `Shared/` when creating/updating character template (+ tests).
9. Restore applies char + `Shared/` (with account overwrite warning); full transfer toggle for account-scoped part added.

**Phase 4 - Save also stores template(s) and polishing.** ✅ Completed
10. Checkboxes in save overlay (account template / character template(s)) + post-save capture/update.
11. Inline rename (in Profiles), toasts, unified confirmations, window persistence.

---

## 9. Out of scope (for now)

- Full live transfer of `SavedVariables` without relog (WoW limitation).
- Physical merge of account and template stores (we unify UX only).
- Export/import of profiles and templates across machines.
- Merge settings instead of overwrite.

---

## 10. Confirmed decisions

1. **Single Profiles tab** is the target state (not two tabs).
2. **Target auto-versioning** before restore is **enabled by default**, switchable off.
3. **Default scope is `Full`**; `CacheOnly` is optional. Write mechanism is
   auto-selected by `IsWowRunning` (in-game: no folder-swap).
4. **`character-complete` transfer** (character + account-scoped dependencies) is
   mandatory; implemented in Phase 3.
