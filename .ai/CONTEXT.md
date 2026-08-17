# HearthSwing — Context (for AI agents)

> Source of truth: this directory (`.ai/`). Entry point: `AGENTS.md` at the repo root.
> This file is the main source of context when working on the app. **Read `ARCHITECTURE.md` before changing any code.**

**Current status:** v1.0. WPF desktop application for capturing, applying, and
recovering World of Warcraft Classic Anniversary settings in the live `WTF`
folder. Templates are the only settings-transfer mechanism; History records
recoverable snapshots before templates change live content. The top-level UI
has exactly two modes: `Templates` and `History`.

---

## What the app does

HearthSwing is a WPF desktop application (.NET 10, `win-x64`) for managing WoW
Classic Anniversary `WTF` settings:

1. **Templates** — capture settings from a live account or character and apply
   them to another target. Character templates replace donor character and
   realm values with target values where supported.
2. **History** — automatically archive affected targets before a template
   overwrites them, then restore or delete history points from the app.
3. **Cache protection** — lock and monitor cache files so server
   synchronization cannot overwrite local settings after launch.
4. **Direct launch** — start WoW from HearthSwing with cache protection enabled.
5. **Flexible storage** — the game folder and the Profiles folder are configured
   independently; HearthSwing can be installed anywhere.

### What templates transfer

Account templates capture account-level files, including top-level configuration
and `SavedVariables`. Character templates capture the selected character folder
and shared account settings (applying a character template can optionally apply
its shared account settings too). This includes:

- **Macros** (`macros-cache.txt`)
- **Keybindings** (`bindings-cache.wtf`)
- **Edit Mode layout** (`edit-mode-cache-*.txt`, `layout-local.txt`)
- **Addon settings** (`SavedVariables/`)
- **Client config** (`config-cache.wtf`, `Config.wtf`)

**Action bars are not stored in `WTF` and are not transferred by HearthSwing.**
Use ActionBarSaver: Reloaded or another compatible addon for action-bar layouts.

### Important to understand

- **Templates are the only transfer mechanism.** There are no saved accounts,
  no profile switching, and no profile filter. The UI has exactly two top-level
  modes: `Templates` and `History`.
- **History is mandatory.** Every write that overwrites live `WTF` content must
  complete an `IChangeHistoryService` snapshot of every affected target first.
  This is a non-optional invariant — never bypass it.
- **WoW running vs. closed behave differently.** While WoW is running, templates
  must never use `IDirectoryReplacer` or folder swap; files are written in
  place, cache protection is released then re-established, and the user is
  prompted to run `/reload`. History restore is an offline operation only.

---

## Project structure

```
HearthSwing/
├── AGENTS.md                 # Agent entry point → read .ai/ (this directory)
├── HearthSwing.slnx          # Solution file
├── README.md                 # Human-facing description (users) — not technical docs
├── .ai/                      # Agent documentation & instructions (source of truth)
│   ├── CONTEXT.md            # This file: context, conventions, gotchas
│   ├── ARCHITECTURE.md       # Architecture: MVVM, DI, services, invariants
│   ├── agents/               # Agent definitions (hearthswing-developer, ...)
│   ├── skills/               # Skills (nunit-testing, cache-protection, ...)
│   ├── prompts/              # Prompt templates (phase-start, bugfix)
│   ├── tools/                # PowerShell tools (build, test, publish)
│   ├── docs/                 # Development plans & implementation prompts
│   └── memories/             # Verified outcomes (repo memory)
├── HearthSwing/              # WPF app (net10.0-windows, win-x64)
│   ├── App.xaml(.cs)         # DI container wiring (App.ConfigureServices)
│   ├── MainWindow.xaml(.cs)  # View (code-behind is visual-tree only)
│   ├── Models/               # HearthSwing.Models — data models
│   ├── Services/             # HearthSwing.Services — business logic & infra
│   ├── ViewModels/           # HearthSwing.ViewModels — MVVM state & commands
│   └── app.ico               # Icon (pack://application:,,,/app.ico)
└── HearthSwing.Tests/        # NUnit tests (mirrors the source structure)
```

| Folder | Namespace | Role |
|--------|-----------|------|
| `Models/` | `HearthSwing.Models` | Data models: `AppSettings`, `HistoryEntry`, templates, and WoW targets |
| `Services/` | `HearthSwing.Services` | Template capture/apply, change history, cache protection, process management, and settings I/O |
| `ViewModels/` | `HearthSwing.ViewModels` | MVVM view models with CommunityToolkit.Mvvm source generators |
| Root (`*.xaml`) | `HearthSwing` | WPF views: `MainWindow.xaml`, `App.xaml` |

---

## Development environment

- **.NET 10 SDK** required (`dotnet --version` → 10.x).
- The WoW client and its `WTF` folder are NOT part of this repo. The
  `content/` folder contains fixture data used by the app/tests (never treat it
  as a real WoW installation).
- `AppSettings.json` is stored next to the executable. `GamePath` is
  auto-detected by walking up directories looking for `WowClassic.exe`.

---

## Code conventions

### MVVM (CommunityToolkit.Mvvm)

- ViewModel inherits `ObservableObject`. Use `[ObservableProperty]` for bindable
  fields and `[RelayCommand]` for commands — source generators create the public
  properties and `ICommand` wrappers.
- Private backing fields follow `_camelCase`: `[ObservableProperty] private string _currentProfileName = "";`
  generates `CurrentProfileName`.
- `ObservableCollection<T>` for list bindings.
- View code-behind (`MainWindow.xaml.cs`) is allowed for visual-tree manipulation
  (button highlighting, scroll-to-end) — keep business logic out.
- `DataContext` is set in `MainWindow` constructor via DI container; ViewModel
  never creates its own services with `new`.

### Dependency injection

- `Microsoft.Extensions.DependencyInjection` is the IoC container.
- `App.ConfigureServices()` registers all application services as singletons,
  including file/process/settings infrastructure, template services, change
  history, cache protection, orchestration, `MainViewModel`, and `MainWindow`.
- Services depend on interfaces, not concrete implementations.
- `MainViewModel` receives dependencies through constructor injection. It
  coordinates UI state and commands but does not contain template, history,
  filesystem, or process business logic.

### Service layer

- Services are `public sealed class` types implementing their interfaces,
  including `ITemplateCatalog`, `ITemplateCaptureService`, `ITemplateApplyService`,
  `ITemplateRestoreOrchestrator`, `IChangeHistoryService`, `ICacheProtector`, and
  `IProcessMonitor`.
- Filesystem I/O is abstracted behind `IFileSystem` (interface in `Services/`).
  The production implementation `FileSystem` delegates to `System.IO`. All
  services accept `IFileSystem` via constructor — never call `File.*` /
  `Directory.*` statics directly in service code.
- Process management is abstracted behind `IProcessManager` for the same reason.
- `TemplateCatalog` stores template metadata and content under
  `<ProfilesPath>/.templates/<id>/`.
- `TemplateCaptureService` captures account templates and character templates.
  Character templates include a tokenized character tree plus account-scoped
  data under `Shared/`.
- `TemplateApplyService` applies a template with `TemplateApplyScope.Full` or
  `TemplateApplyScope.CacheOnly`. It uses a rollback-aware directory replacement
  only when WoW is closed; when WoW is running it writes targeted files in place.
- `TemplateRestoreOrchestrator` snapshots every affected live target through
  `IChangeHistoryService` before applying a template. While WoW is running, it
  follows `Unlock -> apply -> Lock -> ForceRestore` and then prompts for `/reload`.
- `ChangeHistoryService` stores bounded tar.gz history archives and `index.json`
  records under `<ProfilesPath>/.history/<target-key>/`. Restoring a history
  entry snapshots the current target first.
- `CacheProtector` protects WoW cache files from server sync via read-only
  attributes, `FileSystemWatcher` backup/restore, and timestamp touching.
  Implements `IDisposable`.
- `SwitchingOrchestrator` is cache and launch only: lock for launch, unlock,
  force-restore protected cache, and cleanup after WoW exits. It must not regain
  account-switching responsibilities.
- `ProcessMonitor` detects/launches `WowClassic.exe` and monitors process exit.
- `SettingsService` loads/saves `AppSettings.json` next to the executable.
  Auto-detects `GamePath` by walking up directories looking for `WowClassic.exe`.

### Templates and history

- Templates are the only transfer mechanism. Do not reintroduce saved accounts,
  profile switching, or a profile filter.
- Templates live under `<ProfilesPath>/.templates/<id>/`. Account templates hold
  account-scoped files. Character templates hold tokenized character files and
  an optional `Shared/` account-scoped payload.
- `TemplateApplyScope.Full` transfers all applicable files.
  `TemplateApplyScope.CacheOnly` transfers the cache-backed subset only.
- **Invariant**: every write that overwrites live `WTF` content must have a
  successfully completed `IChangeHistoryService` snapshot of every affected
  target first. This is non-optional.
- Character restore with `IncludeAccountScoped` requires separate snapshots of
  the character subtree and account subtree because shared account data affects
  every character on that account.
- History lives under `<ProfilesPath>/.history/<target-key>/` as tar.gz archives
  and `index.json`. `MaxHistoryEntriesPerTarget` defaults to 20 and is
  configurable through `AppSettings`.
- History restore is an offline operation: it resolves the current live target,
  snapshots it again, then uses `IDirectoryReplacer` to restore the archive. The
  UI must require WoW to be closed for this operation.
- Clear read-only attributes before overwriting live files or deleting staging
  folders. Preserve the existing rollback behavior for closed-game directory
  replacement.

### Cache protection

1. **Read-only lock**: set `FileAttributes.ReadOnly` on protected cache files.
2. **In-memory backup**: retain the original cache content while protection is active.
3. **FileSystemWatcher recovery**: restore protected content if external writes occur.
4. **Timestamp touch**: update `LastWriteTime` so WoW prefers the local file after restore.

`CacheFilePatterns` is the single source of truth for protected cache files.

---

## Formatting

- File-scoped namespaces: `namespace X.Y;` (one-liner, no braces).
- `ImplicitUsings` and `Nullable` are enabled globally. Do not add
  `using System;` or `using System.Collections.Generic;`.
- Explicit `using` only for non-global namespaces (`System.IO`, `System.Linq`,
  `System.Diagnostics`, etc.). Remove unused `using` directives.
- Never use `#region` / `#endregion`. Prefer well-named methods and small classes.
- Prefer collection expressions (`[]`) over `Array.Empty<T>()`, `new List<T>()`, etc.
- Prefer method groups over lambda wrappers when the signatures match:
  `_cacheProtector.Log += AppendLog;` not `_cacheProtector.Log += msg => AppendLog(msg);`.
- Do not use the `async` keyword on a method that never `await`s anything. Return
  `Task.CompletedTask` or the inner task directly.
- Prefer async overloads of BCL/framework methods when available (e.g.,
  `ReadAllTextAsync`, `WriteAllTextAsync`).
- Use `string.Empty` instead of `""` for empty string literals.

## Naming

- Classes: `PascalCase`. Models use `sealed class` with properties.
- Private fields: `_camelCase` with underscore prefix.
- Constants: `PascalCase` as `private const` or `private static readonly` inside
  the owning class.
- XAML resource keys: `PascalCase` (`CardBg`, `TextPrimary`, `ProfileBtn`).
- Event handlers: `On*` prefix in code-behind (`OnViewModelPropertyChanged`).

## Access modifiers

- Services: `public sealed class`.
- Models: `public sealed class` with `required` keyword on mandatory properties.
  Use `{ get; init; }` by default; use `{ get; set; }` only when the property must
  be mutated after construction (e.g., `AppSettings` properties bound to UI or
  deserialized with `System.Text.Json`).
- ViewModel: `public partial class` (required for source generators).
- View code-behind helpers: `private` or `private static`.

## Patterns

- Constructor injection with explicit field assignment (no primary constructors).
- `sealed` on all leaf classes (services, models).
- `event Action<string>? Log` for cross-service logging that flows to the
  ViewModel's `AppendLog()`.
- `CancellationToken` for async operations. `CancellationTokenSource` managed by
  the ViewModel for unlock countdown and process monitoring.
- **Fire-and-forget** via discard: `_ = RunUnlockCountdownAsync(delay, ct);` —
  intentional pattern for background tasks that manage their own cancellation.
  Do not `await` these in command methods.
- **Dispatcher** for cross-thread UI updates:
  `Application.Current?.Dispatcher.Invoke(() => { ... });`. Use
  `Dispatcher.CheckAccess()` to detect if already on UI thread.
- Error handling: `try/catch` with user-visible `MessageBox.Show()` for critical
  failures; `AppendLog()` for non-critical warnings.
- **Rollback pattern**: `DirectoryReplacer.ReplaceDirectory()` is rollback-aware
  for closed-game directory replacement. New multi-step filesystem mutations must
  preserve rollback behavior and must take required History snapshots before
  touching live `WTF` content.
- `IDisposable` on classes managing unmanaged resources (`CacheProtector` owns
  `FileSystemWatcher` instances).
- **Threading**: `FileSystemWatcher` callbacks (`OnCacheFileChanged`) execute on
  a threadpool thread, not the UI thread. Keep handler logic IO-only — no UI
  calls inside watchers.

## Comments policy

- Comments explain **"why"**, never **"how"**. If a comment describes what the
  next lines do, extract those lines into a well-named private method instead.
- XML `<summary>` on public API is allowed for non-obvious contracts.
- No step-numbering comments (`// Step 1`, `// Step 2`). Extract each step into
  a named method.
- "Why" comments that explain domain-specific WoW client behaviour are valuable
  — keep them.
- Remove dead/obvious comments.

## Logging

- In-app log via `AppendLog()` in `MainViewModel`. Format: `[HH:mm:ss] message\n`.
- Services use `event Action<string>? Log` — ViewModel subscribes in constructor
  via method group: `_cacheProtector.Log += AppendLog;`.
- Use plain message strings (no structured logging). Prefix errors with
  `"ERROR: "`, warnings with `"Warning: "`.

## JSON serialization

- `System.Text.Json` only (no Newtonsoft).
- `JsonSerializerOptions` with `WriteIndented = true` and
  `PropertyNameCaseInsensitive = true` for settings file.

## WPF / XAML

- Dark theme: background `#1a1a2e`, panel `#16213e`, card `#0f3460`.
- Named `SolidColorBrush` resources in `Window.Resources`.
- Custom button styles (`ProfileBtn`, `ActionBtn`, `LinkBtn`, `SegmentBtn`) use
  `ControlTemplate` and triggers. Reuse them rather than introducing parallel
  styles.
- The top-level mode switch is `Templates | History`; preserve its existing
  segmented-button bindings and refresh the selected collection when a mode
  changes.
- `BooleanToVisibilityConverter` declared in `App.xaml` as `BoolToVis`.
- `InverseBooleanToVisibilityConverter` declared in `App.xaml` as `InverseBoolToVis`.
- Icon via `pack://application:,,,/app.ico` with `<Resource Include="app.ico" />`
  in csproj (required for single-file publish).
- **Settings overlay**: full-grid-span `Border` with semi-transparent background
  (`#ee1a1a2e`) and `Visibility` bound to `IsSettingsVisible`. Toggled via `LinkBtn`.
- Template and History overlays use the established tree search,
  `RelativeSource` command bindings, confirmation dialogs, and toast patterns.
  Keep business decisions in the ViewModel.

---

## Key mechanics and gotchas

1. **Snapshot-before-overwrite is non-optional.** `TemplateRestoreOrchestrator`
   snapshots every affected live target through `IChangeHistoryService` before
   applying. Do not add a ViewModel-to-filesystem path that bypasses it.
2. **WoW running ⇒ in-place writes only.** Never use `IDirectoryReplacer` or a
   folder swap while WoW is running. Follow
   `Unlock -> apply -> Lock -> ForceRestore` and prompt for `/reload`.
3. **History restore is offline.** Resolve the live target, snapshot it again,
   restore with `IDirectoryReplacer`. The UI must require WoW to be closed.
4. **Clear read-only attributes** before overwriting live files or deleting
   staging folders. Cache-protected files are read-only by design.
5. **`CacheFilePatterns` is the single source of truth** for protected cache
   files — add new protected patterns there, not ad hoc.
6. **`FileSystemWatcher` callbacks run on a threadpool thread.** Keep them
   IO-only; never touch UI inside watchers.
7. **Character restore with account-scoped data** needs separate snapshots of
   the character subtree AND the account subtree — shared account data affects
   every character on that account.
8. **`SwitchingOrchestrator` is cache and launch only** — lock for launch,
   unlock, force-restore protected cache, cleanup after exit. Do not reintroduce
   account switching there.
9. **`MessageBox.Show()` is never called from ViewModel directly.** UI dialogs
   are abstracted behind an `Action` delegate or `IMessageDialog` interface so
   the ViewModel stays fully testable.

---

## Testing

- **NUnit** as test framework. `[Test]` for single-case tests, `[TestCase]` for
  parameterized.
- **AutoFixture** + **AutoNSubstitute** for automatic mocking and test data.
- **NSubstitute** for mocking (`Substitute.For<T>()`, `Arg.Any<T>()`,
  `.Returns()`, `.Throws()`).
- **Shouldly** for assertions (`result.ShouldBe(expected)`,
  `action.ShouldThrow<T>()`).
- **Arrange / Act / Assert** pattern with explicit `// Arrange`, `// Act`,
  `// Assert` comments.
- Test project structure mirrors the source project folders (`Services/`,
  `ViewModels/`, `Models/`).
- Test classes: `{ClassUnderTest}Tests` (e.g., `TemplateRestoreOrchestratorTests`,
  `ChangeHistoryServiceTests`, `CacheProtectorTests`).
- Mocks are created with `_fixture.Freeze<T>()` — frozen in `[SetUp]`, arranged
  in test methods.
- SUT (System Under Test) is constructed in `[SetUp]` with all dependencies injected.
- `IFileSystem`, `IProcessManager`, `IArchiveService`, `IWtfInspector`, and the
  service dependencies of the SUT are substituted via NSubstitute in tests — no
  real filesystem or archive I/O in unit tests.
- Template restore tests must verify that required History snapshots complete
  before apply, that running-WoW paths avoid directory replacement, and that
  cache protection is re-established after a live apply.
- Read the `nunit-testing` skill (`.ai/skills/nunit-testing/SKILL.md`) before
  writing or running tests.

---

## Build & publish

- Solution file: `HearthSwing.slnx`
- Build: `dotnet build HearthSwing.slnx -c Release`
- Test: `dotnet test HearthSwing.slnx -c Release`
- Publish: `dotnet publish HearthSwing/HearthSwing.csproj -c Release` (produces
  single-file self-contained exe, ~140 MB).
- Target: `net10.0-windows`, `win-x64`, `PublishSingleFile=true`,
  `SelfContained=true`, `IncludeNativeLibrariesForSelfExtract=true`.

---

## Adding new functionality

1. **Model**: create a logic-free `sealed class`, record, or enum in `Models/`
   when the feature needs a domain type. Use `required` properties where appropriate.
2. **Service**: add a focused `public sealed class` behind an interface in
   `Services/`, register it in `App.ConfigureServices()`, and expose logging
   through `event Action<string>? Log` when needed.
3. **Live WTF mutations**: route template application through
   `ITemplateRestoreOrchestrator` and take all required History snapshots before
   the mutation. Do not add a direct ViewModel-to-filesystem path.
4. **ViewModel and View**: add `[ObservableProperty]` fields and `[RelayCommand]`
   methods in `MainViewModel`, then bind them in `MainWindow.xaml` using the
   existing dark-theme styles, overlays, confirmation dialogs, and toast patterns.
5. **Tests**: add matching test coverage with NUnit, AutoFixture, NSubstitute,
   Shouldly, and the Arrange / Act / Assert pattern.

Follow the `phase-workflow` skill: contract-first (update `.ai/` before code),
todo list, implement, verify (build + tests), document, hand back.
