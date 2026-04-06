# HearthSwing — Copilot Instructions

## Project Overview

WPF desktop application (.NET 10, `win-x64`) for switching World of Warcraft (Classic Anniversary) settings profiles between multiple users on the same PC.
Single-project solution with MVVM architecture: **Models → Services → ViewModels → Views**.

| Folder | Namespace | Role |
|--------|-----------|------|
| `Models/` | `HearthSwing.Models` | Data models: `AppSettings`, `ProfileInfo` |
| `Services/` | `HearthSwing.Services` | Business logic: profile swapping, cache protection, process management, settings I/O |
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
- All services are registered in `MainWindow.ConfigureServices()` as singletons: `IFileSystem`, `IProcessManager`, `ISettingsService`, `IProfileManager`, `ICacheProtector`, `IProcessMonitor`, `MainViewModel`.
- Services depend on interfaces, not concrete types (e.g., `ProfileManager` takes `ISettingsService`, not `SettingsService`).
- `MainViewModel` depends on service interfaces (`ISettingsService`, `IProfileManager`, `ICacheProtector`, `IProcessMonitor`, `IFileSystem`) plus `Action<string, string>` for error dialogs.

### Service Layer

- Services are `public sealed class` implementing their respective interfaces (`ISettingsService`, `IProfileManager`, `ICacheProtector`, `IProcessMonitor`).
- Filesystem I/O is abstracted behind `IFileSystem` (interface in `Services/`) to enable unit testing. The production implementation `FileSystem` delegates to `System.IO`. All services accept `IFileSystem` via constructor — never call `File.*` / `Directory.*` statics directly in service code.
- Process management is abstracted behind `IProcessManager` for the same reason.
- `ProfileManager` — discovers profiles from folder structure, swaps WTF folders, manages `.active` marker file. Implements rollback on switch failure (restores parked profile if activation fails).
- `CacheProtector` — protects WoW cache files from server sync via read-only attributes, `FileSystemWatcher` backup/restore, and timestamp touching. Implements `IDisposable`.
- `ProcessMonitor` — detects/launches `WowClassic.exe`, monitors process exit.
- `SettingsService` — loads/saves `AppSettings.json` next to the executable. Auto-detects `GamePath` by walking up directories looking for `WowClassic.exe`.

### Profile System

- `ProfilesPath` directory contains profile subfolders. Each subfolder is a snapshot of the WoW `WTF` folder.
- `.active` marker file (plain text) in `ProfilesPath` tracks which profile is currently active (its folder is absent because it was moved to `WTF`).
- Switch flow: park current `WTF → Profiles/{current}`, then `Profiles/{target} → WTF`, update `.active` marker.
- Cross-volume support: same-volume uses `Directory.Move()`, different-volume does copy + delete.
- `ClearReadOnlyAttributes()` is called before any directory move.

### Cache Protection (4-Layer Strategy)

1. **Folder swap** — move entire WTF directory per profile.
2. **Read-only lock** — set `FileAttributes.ReadOnly` on all cache files matching known patterns.
3. **FileSystemWatcher** — monitor for changes and restore from in-memory backups.
4. **Timestamp touch** — set `LastWriteTime = DateTime.Now` so WoW prefers local files over server data.

Protected file patterns: `bindings-cache.wtf`, `config-cache.wtf`, `macros-cache.txt`, `edit-mode-cache-*.txt`, `tts-cache-*.txt`, `chat-cache.txt`, `chat-frontend-cache.txt`, `flagged-cache-account.txt`, `layout-local.txt`, `cache.md5`.

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
- **Rollback pattern**: `ProfileManager.SwitchTo()` attempts to restore the previous state if activation fails. New operations that modify filesystem state should follow the same try/rollback approach.
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
- Custom button styles (`ProfileBtn`, `ActionBtn`, `LinkBtn`) with `ControlTemplate` and triggers.
- `ItemsControl` with `WrapPanel` for dynamic profile buttons.
- `BooleanToVisibilityConverter` declared in `App.xaml` as `BoolToVis`.
- Icon via `pack://application:,,,/app.ico` with `<Resource Include="app.ico" />` in csproj (required for single-file publish).
- **Settings overlay**: full-grid-span `Border` with semi-transparent background (`#ee1a1a2e`) and `Visibility` bound to `IsSettingsVisible`. Toggled via `LinkBtn`.
- Bindings: `{Binding PropertyName}`, commands: `{Binding CommandName}`, relative source for nested templates: `{Binding DataContext.Command, RelativeSource={RelativeSource AncestorType=Window}}`.

## Testing

- **NUnit** as test framework. `[Test]` for single-case tests, `[TestCase]` for parameterized.
- **AutoFixture** + **AutoNSubstitute** for automatic mocking and test data generation.
- **NSubstitute** for mocking (`Substitute.For<T>()`, `Arg.Any<T>()`, `.Returns()`, `.Throws()`).
- **Shouldly** for assertions (`result.ShouldBe(expected)`, `action.ShouldThrow<T>()`).
- **Arrange / Act / Assert** pattern with explicit `// Arrange`, `// Act`, `// Assert` comments.
- `GlobalFixture` (NUnit `[SetUpFixture]`) provides shared setup for the test assembly.
- Test project structure mirrors the source project folders (`Services/`, `ViewModels/`, `Models/`).
- Test classes: `{ClassUnderTest}Tests` (e.g., `ProfileManagerTests`, `CacheProtectorTests`).
- Mocks are created with `_fixture.Freeze<T>()` — frozen in `[SetUp]`, arranged in test methods.
- SUT (System Under Test) is constructed in `[SetUp]` with all dependencies injected.
- `IFileSystem` and `IProcessManager` are substituted via NSubstitute in tests — no real filesystem I/O in unit tests.
- `MessageBox.Show()` is never called from ViewModel directly. UI dialogs are abstracted behind an `Action` delegate or `IMessageDialog` interface so the ViewModel is fully testable.

## Build & Publish

- Solution file: `HearthSwing.slnx`
- Build: `dotnet build HearthSwing.slnx -c Release`
- Test: `dotnet test HearthSwing.slnx -c Release`
- Publish: `dotnet publish HearthSwing/HearthSwing.csproj -c Release` (produces single-file self-contained exe, ~140 MB).
- Target: `net10.0-windows`, `win-x64`, `PublishSingleFile=true`, `SelfContained=true`, `IncludeNativeLibrariesForSelfExtract=true`.

## Adding New Functionality

When adding a new feature:

1. **Model**: Create `sealed class` in `Models/` with `required` properties. Keep models logic-free.
2. **Service**: Create `sealed class` in `Services/`. Expose `event Action<string>? Log` if it needs to report activity. Wire it up in `MainViewModel` constructor.
3. **ViewModel**: Add `[ObservableProperty]` fields and `[RelayCommand]` methods in `MainViewModel`. For complex features, consider a separate ViewModel.
4. **View**: Bind new properties/commands in `MainWindow.xaml`. Follow existing dark-theme style keys.
5. **Tests**: Create matching test file in the test project. Use `NUnit`, `AutoFixture`, `NSubstitute`, `Shouldly`, and AAA pattern.
