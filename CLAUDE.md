# HearthSwing — Claude Code Instructions

## Project Overview

WPF desktop application (.NET 10, `win-x64`) for capturing, applying, and
recovering World of Warcraft Classic Anniversary settings in the live `WTF`
folder. Templates are the only settings-transfer mechanism; History records
recoverable snapshots before templates change live content.

Single-project solution with MVVM architecture: **Models -> Services -> ViewModels -> Views**.

| Folder | Namespace | Role |
|--------|-----------|------|
| `Models/` | `HearthSwing.Models` | Data models: `AppSettings`, `HistoryEntry`, templates, and WoW targets |
| `Services/` | `HearthSwing.Services` | Template capture/apply, change history, cache protection, process management, and settings I/O |
| `ViewModels/` | `HearthSwing.ViewModels` | MVVM view models with CommunityToolkit.Mvvm source generators |
| Root (`*.xaml`) | `HearthSwing` | WPF views: `MainWindow.xaml`, `App.xaml` |

## Architecture Conventions

### MVVM (CommunityToolkit.Mvvm)

- ViewModel inherits `ObservableObject`. Use `[ObservableProperty]` for bindable fields and `[RelayCommand]` for commands — source generators create the public properties and `ICommand` wrappers.
- Private backing fields follow `_camelCase` convention: `[ObservableProperty] private string _currentProfileName = "";` generates `CurrentProfileName`.
- `ObservableCollection<T>` for list bindings.
- View code-behind (`MainWindow.xaml.cs`) is allowed for visual-tree manipulation (button highlighting, scroll-to-end) — keep business logic out.
- `DataContext` is set in `MainWindow` constructor via DI container; ViewModel never creates its own services with `new`.

### Dependency Injection

- `Microsoft.Extensions.DependencyInjection` is used as the IoC container.
- `App.ConfigureServices()` registers all application services as singletons, including file/process/settings infrastructure, template services, change history, cache protection, orchestration, `MainViewModel`, and `MainWindow`.
- Services depend on interfaces, not concrete implementations. Preserve the existing dependency boundaries rather than creating services in ViewModels.
- `MainViewModel` receives its dependencies through constructor injection. It coordinates UI state and commands but does not contain template, history, filesystem, or process business logic.

### Service Layer

- Services are `public sealed class` types implementing their interfaces, including `ITemplateCatalog`, `ITemplateCaptureService`, `ITemplateApplyService`, `ITemplateRestoreOrchestrator`, `IChangeHistoryService`, `ICacheProtector`, and `IProcessMonitor`.
- Filesystem I/O is abstracted behind `IFileSystem` (interface in `Services/`) to enable unit testing. The production implementation `FileSystem` delegates to `System.IO`. All services accept `IFileSystem` via constructor — never call `File.*` / `Directory.*` statics directly in service code.
- Process management is abstracted behind `IProcessManager` for the same reason.
- `TemplateCatalog` stores template metadata and content under `<ProfilesPath>/.templates/<id>/`.
- `TemplateCaptureService` captures account templates and character templates. Character templates include a tokenized character tree plus account-scoped data under `Shared/`.
- `TemplateApplyService` applies a template with `TemplateApplyScope.Full` or `TemplateApplyScope.CacheOnly`. It uses a rollback-aware directory replacement only when WoW is closed; when WoW is running it writes targeted files in place.
- `TemplateRestoreOrchestrator` snapshots every affected live target through `IChangeHistoryService` before applying a template. While WoW is running, it follows `Unlock -> apply -> Lock -> ForceRestore` and then prompts for `/reload`.
- `ChangeHistoryService` stores bounded tar.gz history archives and `index.json` records under `<ProfilesPath>/.history/<target-key>/`. Restoring a history entry snapshots the current target first.
- `CacheProtector` — protects WoW cache files from server sync via read-only attributes, `FileSystemWatcher` backup/restore, and timestamp touching. Implements `IDisposable`.
- `SwitchingOrchestrator` is now cache and launch only: lock for launch, unlock, force-restore protected cache, and cleanup after WoW exits. It must not regain account-switching responsibilities.
- `ProcessMonitor` — detects/launches `WowClassic.exe`, monitors process exit.
- `SettingsService` — loads/saves `AppSettings.json` next to the executable. Auto-detects `GamePath` by walking up directories looking for `WowClassic.exe`.

### Templates and History

- Templates are the only transfer mechanism. The UI has two top-level modes: `Templates` and `History`; do not reintroduce saved accounts, profile switching, or a profile filter.
- Templates live under `<ProfilesPath>/.templates/<id>/`. Account templates hold account-scoped files. Character templates hold tokenized character files and an optional `Shared/` account-scoped payload.
- `TemplateApplyScope.Full` transfers all applicable files. `TemplateApplyScope.CacheOnly` transfers the cache-backed subset only.
- Every write that overwrites live `WTF` content must have a successfully completed `IChangeHistoryService` snapshot of every affected target first. This is a non-optional invariant.
- Character restore with `IncludeAccountScoped` requires separate snapshots of the character subtree and account subtree because shared account data affects every character on that account.
- History lives under `<ProfilesPath>/.history/<target-key>/` as tar.gz archives and `index.json`. `MaxHistoryEntriesPerTarget` defaults to 20 and is configurable through `AppSettings`.
- History restore is an offline operation: it resolves the current live target, snapshots it again, then uses `IDirectoryReplacer` to restore the archive. The UI must require WoW to be closed for this operation.
- When WoW is running, templates must never use `IDirectoryReplacer` or folder swap. Apply files in place, temporarily release cache protection, then lock and force-restore cache files before prompting for `/reload`.
- Clear read-only attributes before overwriting live files or deleting staging folders. Preserve the existing rollback behavior for closed-game directory replacement.

### Cache Protection

1. **Read-only lock**: set `FileAttributes.ReadOnly` on protected cache files.
2. **In-memory backup**: retain the original cache content while protection is active.
3. **FileSystemWatcher recovery**: restore protected content if external writes occur.
4. **Timestamp touch**: update `LastWriteTime` so WoW prefers the local file after restore.

`CacheFilePatterns` is the single source of truth. Protected patterns include
`bindings-cache.wtf`, `config-cache.wtf`, `macros-cache.txt`,
`edit-mode-cache-account.txt`, `edit-mode-cache-character.txt`,
`tts-cache-account.txt`, `tts-cache-character.txt`, `chat-cache.txt`,
`chat-frontend-cache.txt`, `flagged-cache-account.txt`, `layout-local.txt`, and
`cache.md5`.

## Code Style

### Formatting

- File-scoped namespaces: `namespace X.Y;` (one-liner, no braces).
- `ImplicitUsings` and `Nullable` are enabled globally. Do not add `using System;` or `using System.Collections.Generic;`.
- Explicit `using` only for non-global namespaces (`System.IO`, `System.Linq`, `System.Diagnostics`, etc.). Remove unused `using` directives.
- Never use `#region` / `#endregion`. Prefer well-named methods and small classes for organization.
- Prefer collection expressions (`[]`) over `Array.Empty<T>()`, `new List<T>()`, etc.
- Prefer method groups over lambda wrappers when the signatures match: `_cacheProtector.Log += AppendLog;` not `_cacheProtector.Log += msg => AppendLog(msg);`.
- Do not use the `async` keyword on a method that never `await`s anything. Return `Task.CompletedTask` or the inner task directly.
- Prefer async overloads of BCL/framework methods when available (e.g., `ReadAllTextAsync`, `WriteAllTextAsync`).
- Use `string.Empty` instead of `""` for empty string literals.

### Naming

- Classes: `PascalCase`. Models use `sealed class` with properties.
- Private fields: `_camelCase` with underscore prefix.
- Constants: `PascalCase` as `private const` or `private static readonly` inside the owning class.
- XAML resource keys: `PascalCase` (`CardBg`, `TextPrimary`, `ProfileBtn`).
- Event handlers: `On*` prefix in code-behind (`OnViewModelPropertyChanged`).

### Access Modifiers

- Services: `public sealed class`.
- Models: `public sealed class` with `required` keyword on mandatory properties. Use `{ get; init; }` by default; use `{ get; set; }` only when the property must be mutated after construction (e.g., `AppSettings` properties bound to UI or deserialized with `System.Text.Json`).
- ViewModel: `public partial class` (required for source generators).
- View code-behind helpers: `private` or `private static`.

### Patterns

- Constructor injection with explicit field assignment (no primary constructors).
- `sealed` on all leaf classes (services, models).
- `event Action<string>? Log` for cross-service logging that flows to the ViewModel's `AppendLog()`.
- `CancellationToken` for async operations. `CancellationTokenSource` managed by the ViewModel for unlock countdown and process monitoring.
- **Fire-and-forget** via discard: `_ = RunUnlockCountdownAsync(delay, ct);` — intentional pattern for background tasks that manage their own cancellation. Do not `await` these in command methods.
- **Dispatcher** for cross-thread UI updates: `Application.Current?.Dispatcher.Invoke(() => { ... });`. Use `Dispatcher.CheckAccess()` to detect if already on UI thread.
- Error handling: `try/catch` with user-visible `MessageBox.Show()` for critical failures; `AppendLog()` for non-critical warnings.
- **Rollback pattern**: `DirectoryReplacer.ReplaceDirectory()` is rollback-aware for closed-game directory replacement. New multi-step filesystem mutations must preserve rollback behavior and must take required History snapshots before touching live `WTF` content.
- `IDisposable` on classes managing unmanaged resources (`CacheProtector` owns `FileSystemWatcher` instances).
- **Threading**: `FileSystemWatcher` callbacks (`OnCacheFileChanged`) execute on a threadpool thread, not the UI thread. Keep handler logic IO-only — no UI calls inside watchers.

### Comments Policy

- Comments explain **"why"**, never **"how"**. If a comment describes what the next lines do, extract those lines into a well-named private method instead.
- XML `<summary>` on public API is allowed for non-obvious contracts.
- No step-numbering comments (`// Step 1`, `// Step 2`). Extract each step into a named method.
- "Why" comments that explain domain-specific WoW client behaviour are valuable — keep them.
- Remove dead/obvious comments like `// Restore the file from backup` above a `File.WriteAllBytes` call.

### Logging

- In-app log via `AppendLog()` in `MainViewModel`. Format: `[HH:mm:ss] message\n`.
- Services use `event Action<string>? Log` — ViewModel subscribes in constructor via method group: `_cacheProtector.Log += AppendLog;`.
- Use plain message strings (no structured logging). Prefix errors with `"ERROR: "`, warnings with `"Warning: "`.

### JSON Serialization

- `System.Text.Json` only (no Newtonsoft).
- `JsonSerializerOptions` with `WriteIndented = true` and `PropertyNameCaseInsensitive = true` for settings file.

### WPF / XAML

- Dark theme: background `#1a1a2e`, panel `#16213e`, card `#0f3460`.
- Named `SolidColorBrush` resources in `Window.Resources`.
- Custom button styles (`ProfileBtn`, `ActionBtn`, `LinkBtn`, `SegmentBtn`) use `ControlTemplate` and triggers. Reuse them rather than introducing parallel styles.
- The top-level mode switch is `Templates | History`; preserve its existing segmented-button bindings and refresh the selected collection when a mode changes.
- `BooleanToVisibilityConverter` declared in `App.xaml` as `BoolToVis`.
- `InverseBooleanToVisibilityConverter` is declared in `App.xaml` as `InverseBoolToVis`.
- Icon via `pack://application:,,,/app.ico` with `<Resource Include="app.ico" />` in csproj (required for single-file publish).
- **Settings overlay**: full-grid-span `Border` with semi-transparent background (`#ee1a1a2e`) and `Visibility` bound to `IsSettingsVisible`. Toggled via `LinkBtn`.
- Template and History overlays use the established tree search, `RelativeSource` command bindings, confirmation dialogs, and toast patterns. Keep business decisions in the ViewModel.

## Testing

- **NUnit** as test framework. `[Test]` for single-case tests, `[TestCase]` for parameterized.
- **AutoFixture** + **AutoNSubstitute** for automatic mocking and test data generation.
- **NSubstitute** for mocking (`Substitute.For<T>()`, `Arg.Any<T>()`, `.Returns()`, `.Throws()`).
- **Shouldly** for assertions (`result.ShouldBe(expected)`, `action.ShouldThrow<T>()`).
- **Arrange / Act / Assert** pattern with explicit `// Arrange`, `// Act`, `// Assert` comments.
- `GlobalFixture` (NUnit `[SetUpFixture]`) provides shared setup for the test assembly.
- Test project structure mirrors the source project folders (`Services/`, `ViewModels/`, `Models/`).
- Test classes: `{ClassUnderTest}Tests` (for example, `TemplateRestoreOrchestratorTests`, `ChangeHistoryServiceTests`, and `CacheProtectorTests`).
- Mocks are created with `_fixture.Freeze<T>()` — frozen in `[SetUp]`, arranged in test methods.
- SUT (System Under Test) is constructed in `[SetUp]` with all dependencies injected.
- `IFileSystem`, `IProcessManager`, `IArchiveService`, `IWtfInspector`, and the service dependencies of the SUT are substituted via NSubstitute in tests — no real filesystem or archive I/O in unit tests.
- Template restore tests must verify that required History snapshots complete before apply, that running-WoW paths avoid directory replacement, and that cache protection is re-established after a live apply.
- `MessageBox.Show()` is never called from ViewModel directly. UI dialogs are abstracted behind an `Action` delegate or `IMessageDialog` interface so the ViewModel is fully testable.

## Build & Publish

- Solution file: `HearthSwing.slnx`
- Build: `dotnet build HearthSwing.slnx -c Release`
- Test: `dotnet test HearthSwing.slnx -c Release`
- Publish: `dotnet publish HearthSwing/HearthSwing.csproj -c Release` (produces single-file self-contained exe, ~140 MB).
- Target: `net10.0-windows`, `win-x64`, `PublishSingleFile=true`, `SelfContained=true`, `IncludeNativeLibrariesForSelfExtract=true`.

## Adding New Functionality

When adding a new feature:

1. **Model**: Create a logic-free `sealed class`, record, or enum in `Models/` when the feature needs a domain type. Use `required` properties where appropriate.
2. **Service**: Add a focused `public sealed class` behind an interface in `Services/`, register it in `App.ConfigureServices()`, and expose logging through the existing `ILogger<T>`/UI log infrastructure when needed.
3. **Live WTF mutations**: route template application through `ITemplateRestoreOrchestrator` and take all required History snapshots before the mutation. Do not add a direct ViewModel-to-filesystem path.
4. **ViewModel and View**: add `[ObservableProperty]` fields and `[RelayCommand]` methods in `MainViewModel`, then bind them in `MainWindow.xaml` using the existing dark-theme styles, overlays, confirmation dialogs, and toast patterns.
5. **Tests**: add matching test coverage with NUnit, AutoFixture, NSubstitute, Shouldly, and the Arrange / Act / Assert pattern.
