# Plan: Character Template Mode

This document describes adding a new global mode to HearthSwing—cross-account
anonymized character templates—while preserving the current account mode unchanged.

## 1. Goal and Feature Essence

A player uses the same class (e.g., warlock) across multiple accounts, realms,
and characters. They want a single **anonymized template** ("Warlock - TBC") that:

- is created from an existing source character;
- becomes detached from the character name and realm name (anonymization);
- can be applied to any other character (different account / realm / name),
  substituting the target's name and realm everywhere they appear—in folder paths
  and **inside file contents** (macros, SavedVariables, cache, etc.).

Result: unified settings (keybinds, macros, edit-mode/layout, addon settings)
sync across characters and realms.

## 2. Locked Decisions

| Topic | Decision |
|---|---|
| UI | Top-level mode toggle "Accounts \| Templates" in main window (existing screen moves to "Accounts" tab without behavior changes) |
| Anonymization | Tokenization during capture: original name/realm are replaced with `{{CHAR}}` / `{{REALM}}` |
| What We Anonymize | Character name **and** realm name |
| Where We Replace Content | Known allowlist of text files + all `*.lua` in SavedVariables at both character **and** account levels |
| Template Scope | Account settings (top-level files from `Account/<ACC>/` + `SavedVariables/`) **+** one character folder `<REALM>/<CHAR>/` |
| Storage | `<ProfilesPath>/.templates/<templateId>/` |
| Versioning | Yes, `.tar.gz` archives, same as accounts |

## 3. Key Difference from Current Code

Currently, all code operates **at the account level** and copies files **byte-by-byte**
(`CopyFile`, `SequenceEqual`)—WoW file content is never read or rewritten. The new
feature introduces two fundamentally new capabilities:

1. **Character-level granularity.**
2. **Name/realm substitution inside text file contents** (tokenization).

Therefore, the "Templates" mode is implemented as a **separate service branch**,
and existing services (`AccountSnapshotSaveService`, `AccountSwitchService`,
`SavedAccountCatalog`, `ProfileVersionService`, `SwitchingOrchestrator`) **remain
unchanged**.

## 4. Code Reuse

| Existing | How We Reuse |
|---|---|
| `IFileSystem` | All file I/O in new code uses only this abstraction (testability) |
| `IWtfInspector` | Already provides the tree `WowInstallation → WowAccount → WowRealm → WowCharacter`—source for choosing source and target characters |
| `IAccountSnapshotLayout` | `CollectAccountSettingsRelativePaths` and `CollectCharacterRelativePaths`—ready-made classification of "account settings" vs "character files". Reuse as-is |
| `IArchiveService` (tar.gz) | Template versioning |
| Rollback-copy pattern | Extract into new shared helper `IDirectoryReplacer` (existing services untouched; they continue using their own private copies) |
| Dot-prefix ignored when walking accounts (`SavedAccountCatalog`) | `.templates/` and `.template-versions/` won't appear in account enumeration automatically |
| `IProcessMonitor` | Block template application while WoW is running (same as switch) |
| `IDialogService` | Confirmations and warnings (account settings overwrite, `/reload`) |
| `ProfileVersionService` | Serves as reference; create parallel `TemplateVersionService` for templates (without modifying existing) |

## 5. Disk Storage

```
<ProfilesPath>/
  .templates/
    <templateId>/
      template.json                         → TemplateMetadata
      Account/                              → tokenized account settings
        <top-level files>                   (top-level Account/<ACC>/*)
        SavedVariables/*.lua                (tokenized)
      Character/
        __REALM__/__CHAR__/                 → tokenized character folder
          SavedVariables/*.lua
          macros-cache.txt, ... (tokenized)
  .template-versions/
    <templateId>/<yyyyMMdd_HHmmss>.tar.gz   → versions (same as accounts)
```

- Tokens in **folder names**: `__CHAR__`, `__REALM__` (filesystem-safe).
- Tokens in **file contents**: `{{CHAR}}`, `{{REALM}}`.
- `templateId` is generated from template name using the same sanitization rules as
  `savedAccountId` in `SavedAccountCatalog` (invalid chars → `_`, spaces → `-`,
  collisions → suffix `-2`, `-3`, ...).

## 6. New Models (`Models/Templates/`)

`TemplateMetadata` (persisted in `template.json`):

- `string Id` (required init)
- `string Name` (required init) — "Warlock - TBC"
- `string SourceAccountName` (required init) — provenance
- `string SourceRealmName` (required init)
- `string SourceCharacterName` (required init)
- `DateTimeOffset CreatedAtUtc`
- `DateTimeOffset? LastUpdatedUtc`
- `int SchemaVersion` — for future use (token format migrations)

`TemplateSummary` (in-memory projection, not persisted):

- `string Id`, `string Name`, `string RootPath`
- `string SourceAccountName`, `string SourceRealmName`, `string SourceCharacterName`
- `DateTimeOffset CreatedAtUtc`, `DateTimeOffset? LastUpdatedUtc`
- `string DisplayName => Name` (computed)

`TemplateApplyOptions`:

- `bool IncludeAccountSettings` — whether to apply account-level settings (default: **false**, see risk in §11)
- `bool CreateVersionBeforeApply` — create a version of the target before applying

All models are `public sealed class`, with `required` + `init`, file-scoped namespace
`HearthSwing.Models.Templates`.

## 7. New Services (`Services/`)

All are `public sealed class`, implement an interface, singleton DI registration in
`App.xaml.cs ConfigureServices()`. Logging via `event Action<string>? Log` where the
service should report progress.

### 7.1 `ITemplateTokenizer` / `TemplateTokenizer`

Core of anonymization. No file I/O—pure string operations.

- `string Tokenize(string content, string charName, string realmName)`
- `string Expand(string content, string charName, string realmName)`
- Replacement order (important to minimize false matches):
  1. compound key `"<char> - <realm>"` → `"{{CHAR}} - {{REALM}}"`;
  2. then `realmName` → `{{REALM}}`;
  3. then `charName` → `{{CHAR}}`.
- v1: ordinal (case-sensitive), since folder names match SV keys.
- Extract replacement pairs into a structure (list of origin strings) to later add
  realm variants (with/without spaces) without rewriting logic.

### 7.2 `ITemplateFileClassifier` / `TemplateFileClassifier`

Determines whether to tokenize a file or copy byte-by-byte.

- `bool ShouldTokenize(string relativePath)` — true for:
  - any `*.lua` (SavedVariables at both levels);
  - allowlist of cache files: `macros-cache.txt`, `bindings-cache.wtf`,
    `config-cache.wtf`, `chat-cache.txt`, `chat-frontend-cache.txt`,
    `edit-mode-cache-account.txt`, `edit-mode-cache-character.txt`,
    `tts-cache-account.txt`, `tts-cache-character.txt`,
    `flagged-cache-account.txt`, `layout-local.txt`.
- Explicitly **exclude** `cache.md5` from tokenization (it's a checksum; WoW
  recalculates it—copy as-is or don't include).
- Keep pattern constants nearby (like `CachePatterns` in `CacheProtector`).

### 7.3 `IDirectoryReplacer` / `DirectoryReplacer`

General rollback-copy helper (new; existing services untouched).

- `void ReplaceDirectory(string sourcePath, string destinationPath)` — copy
  destination to `.rollback-<guid>`, clear read-only, delete, copy source;
  on error—rollback; `finally`—delete rollback folder. Repeats the proven pattern
  from `AccountSnapshotSaveService`.
- `void CopyDirectory(...)`, `void ClearReadOnlyAttributes(...)` — helpers.

### 7.4 `ITemplateCatalog` / `TemplateCatalog`

Analogous to `SavedAccountCatalog`, but for `.templates/`.

- `List<TemplateSummary> DiscoverTemplates()` — walk `.templates/*/`, read
  `template.json`, sort by `Name` (OrdinalIgnoreCase).
- `TemplateSummary? GetById(string templateId)`
- `TemplateSummary Create(string name, ...)` — create folder + `template.json`.
- `void UpdateLastUpdated(string templateId, DateTimeOffset)`
- `void Rename(string templateId, string newName)`
- `void Delete(string templateId)` — delete template folder and its `.template-versions/<id>`.
- `StorageRoot = Path.Combine(AppSettings.ProfilesPath, ".templates")`.

### 7.5 `ITemplateCaptureService` / `TemplateCaptureService`

Creating a template from a live character.

- `TemplateSummary CreateTemplate(WowCharacter source, string templateName)`:
  1. `Create` entry in catalog;
  2. collect source account settings via
     `IAccountSnapshotLayout.CollectAccountSettingsRelativePaths(Account/<srcAcc>)`
     → write to `<template>/Account/`, tokenizing text files;
  3. collect character files via `CollectCharacterRelativePaths(<srcChar>)`
     → write to `<template>/Character/__REALM__/__CHAR__/`, tokenizing;
  4. `UpdateLastUpdated`.
- Tokenization: for each file—if `ShouldTokenize` → read as UTF-8,
  `Tokenizer.Tokenize(text, source.CharacterName, source.RealmName)`, write;
  else `CopyFile` byte-by-byte.
- `event Action<string>? Log`.

### 7.6 `ITemplateApplyService` / `TemplateApplyService`

Applying a template to a target character.

- `void ApplyTemplate(TemplateSummary template, WowCharacter target, TemplateApplyOptions options)`:
  1. prepare temporary "expanded" copy: walk `<template>/Character/...`,
     for text files `Tokenizer.Expand(text, target.CharacterName, target.RealmName)`,
     replace token folder names with real `<targetRealm>/<targetChar>`;
  2. `DirectoryReplacer.ReplaceDirectory(expandedChar, WTF/Account/<tgtAcc>/<tgtRealm>/<tgtChar>)`;
  3. if `options.IncludeAccountSettings` — similarly expand `<template>/Account/`
     and apply to `WTF/Account/<tgtAcc>/` (**with warning**, see §11);
  4. rollback on error at each step.
- `event Action<string>? Log`.

### 7.7 `ITemplateVersionService` / `TemplateVersionService`

Parallel to `ProfileVersionService`, root = `<ProfilesPath>/.template-versions`.

- `Task CreateVersionAsync(string templateId)` — archive template folder to
  `<root>/<templateId>/<timestamp>.tar.gz`, `PruneVersions(...)`.
- `List<ProfileVersion> GetVersions(string templateId)`
- `Task RestoreVersionAsync(ProfileVersion)`
- `void DeleteVersion(ProfileVersion)`
- `void PruneVersions(string templateId, int maxVersions)`
- Reuses `ProfileVersion` model and `IArchiveService`.
- Trigger: version template before its rewrite (recreate/update).

## 8. ViewModel

The existing `MainViewModel` is extended **additively** (existing properties/commands
unchanged).

New mode (agreed—tabs in style of existing Visibility bindings):

- `public enum AppMode { Accounts, Templates }` (in `HearthSwing.ViewModels` or Models).
- `[ObservableProperty] AppMode _activeMode = AppMode.Accounts;`
  with computed `bool IsAccountsMode`/`bool IsTemplatesMode` (or converter) for
  Visibility of two root panels.
- `[RelayCommand] void ShowAccountsMode()` / `ShowTemplatesMode()`.

Template state:

- `ObservableCollection<TemplateSummary> Templates`
- `ObservableCollection<ProfileVersion> TemplateVersions`
- source/target selection: reuse the tree from `_wtfInspector.Inspect(GamePath)`
  (`LiveAccounts` already built; add realm/character projections similar to
  `RealmSaveSelectionViewModel` / `CharacterSaveSelectionViewModel`).

Commands:

- `CreateTemplateAsync()` — select source character + enter name → `CaptureService`.
- `ApplyTemplateAsync(string templateId)` — select target character + options →
  warning (if `IncludeAccountSettings`) → `ApplyService`; suggest `/reload`.
- `RenameTemplate(string templateId)`, `DeleteTemplate(string templateId)`.
- `ToggleTemplateVersionHistory(string templateId)`,
  `RestoreTemplateVersionAsync(...)`, `DeleteTemplateVersion(...)`.
- Guards: `IsBusy`, `IsWowRunning` (application blocked while WoW is running).

Subscribe to `Log` from new services—via `AppendLog` (method group), like existing
services.

## 9. View (XAML)

- In `MainWindow.xaml` add top-level mode toggle (segmented control /
  two tab buttons in dark theme, `ProfileBtn`/`LinkBtn` styles).
- Wrap **existing** content in an "Accounts" panel (Visibility ← `IsAccountsMode`)
  — move markup without changing bindings/behavior.
- New "Templates" panel (Visibility ← `IsTemplatesMode`): template list
  (`ItemsControl` + `WrapPanel`, like profiles), buttons for "Create Template" /
  "Apply" / "Version History" / "Delete", source and target selection dialogs.
- Reuse existing dark theme resources (`CardBg`, `TextPrimary`, `BoolToVis`) and
  overlay-panel pattern for character selection.

## 10. Tests (`HearthSwing.Tests/`)

Structure mirrors source. NUnit + AutoFixture(AutoNSubstitute) + NSubstitute +
Shouldly, AAA, `Freeze<T>()`, no real I/O.

- `Services/TemplateTokenizerTests` — round-trip tokenize/expand; compound key
  `"Name - Realm"`; realm with spaces; no false substring replacement (doc as
  known limitation); idempotency.
- `Services/TemplateFileClassifierTests` — `.lua` yes; `cache.md5` no; allowlist.
- `Services/DirectoryReplacerTests` — success and rollback on copy error.
- `Services/TemplateCatalogTests` — discover/create/rename/delete, id sanitization,
  collisions, ignore dot folders.
- `Services/TemplateCaptureServiceTests` — text tokenization, binary byte-copy,
  structure `Account/` + `Character/__REALM__/__CHAR__/`.
- `Services/TemplateApplyServiceTests` — token expansion, placement at target path,
  rollback, `IncludeAccountSettings` behavior.
- `Services/TemplateVersionServiceTests` — create/list/restore/prune under
  `.template-versions`.
- `ViewModels/MainViewModelTests` — new commands, guards (`IsWowRunning`),
  `AppMode` toggle, no regression in old commands.

## 11. Risks and Edge Cases

1. **Account settings overwrite affects all account characters.**
   `Account/SavedVariables` is shared across all characters on an account. Applying
   account settings from a template will overwrite data for other characters.
   Mitigations: `IncludeAccountSettings = false` by default; explicit warning via
   `IDialogService`; auto-version target before apply (`CreateVersionBeforeApply`).
2. **False matches during replacement.** Character/realm name may be a substring of
   another word. Mitigations: replace only in allowlist files and `*.lua`; order of
   replacements (compound key → realm → name); document as v1 known limitation.
3. **Realm name mismatch (folder vs SV key, spaces).** Replacement mechanism—list of
   origin-string pairs, expandable to realm variants without logic rewrite.
4. **Encoding.** WoW SavedVariables—UTF-8; read/write strictly UTF-8 (no BOM).
5. **`cache.md5` becomes stale** after tokenization—exclude from tokenization; WoW
   recalculates; recommend `/reload` after apply.
6. **Apply while WoW running** — block (guard `IsWowRunning`), like switch.
7. **Hard requirement:** existing services and their behavior unchanged; new code is
   a separate branch, using only `IFileSystem`/`IArchiveService`/layout.

## 12. Implementation Order (Incrementally)

1. Models `Models/Templates/` (`TemplateMetadata`, `TemplateSummary`, `TemplateApplyOptions`).
2. `TemplateTokenizer` + tests (core, highest risk—validate first).
3. `TemplateFileClassifier` + tests.
4. `DirectoryReplacer` + tests.
5. `TemplateCatalog` + tests.
6. `TemplateCaptureService` + tests.
7. `TemplateApplyService` + tests.
8. `TemplateVersionService` + tests.
9. DI registration in `App.xaml.cs`.
10. `MainViewModel`: `AppMode`, collections, commands + tests.
11. `MainWindow.xaml`: mode toggle, "Templates" panel, selection dialogs.
12. Run `dotnet build HearthSwing.slnx -c Release` and
    `dotnet test HearthSwing.slnx -c Release`; manual smoke test on real WTF.

## 13. Explicitly Out of Scope v1

- Extracting per-character entries from account `*.lua` by key `"Name - Realm"`
  (requires Lua parsing)—separate future phase.
- Merging (merge) account settings instead of overwrite.
- Case-insensitive/heuristic name replacement.
- Export/import templates between machines (can add on top of `IArchiveService`).
