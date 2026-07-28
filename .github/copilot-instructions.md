# HearthSwing — Copilot Instructions

Use these instructions for all work in this repository. For full project context, refer to `CLAUDE.md`.

## Project Overview

- WPF desktop application targeting .NET 10 and `win-x64`.
- Purpose: capture, apply, and recover World of Warcraft Classic Anniversary `WTF` settings through portable templates and bounded change history.
- Architecture is MVVM: `Models -> Services -> ViewModels -> Views`.

## Architecture

- Keep responsibilities separated:
  - `Models/` contains data-only types such as `AppSettings`, `HistoryEntry`, template models, and WoW target models.
  - `Services/` contains business logic and infrastructure abstractions.
  - `ViewModels/` contains MVVM state and commands.
  - Root XAML files define views.
- Register services in `App.ConfigureServices()` and use constructor injection throughout.
- Services must depend on interfaces, not concrete implementations.
- Keep filesystem access behind `IFileSystem` and process access behind `IProcessManager`.
- Do not move business logic into XAML code-behind. `MainWindow.xaml.cs` is only for UI-specific behavior.
- Preserve rollback behavior for closed-game directory replacement and take required History snapshots before live `WTF` mutations.

## MVVM Conventions

- ViewModels inherit `ObservableObject`.
- Prefer `[ObservableProperty]` and `[RelayCommand]` from CommunityToolkit.Mvvm.
- Use `_camelCase` private backing fields for generated properties.
- Use `ObservableCollection<T>` for list bindings.
- `DataContext` is set via DI; ViewModels must not instantiate their own services.
- Use the WPF dispatcher for cross-thread UI updates.

## Service Conventions

- Services are `public sealed class` types implementing interfaces such as `ITemplateCatalog`, `ITemplateCaptureService`, `ITemplateApplyService`, `ITemplateRestoreOrchestrator`, `IChangeHistoryService`, `ICacheProtector`, and `IProcessMonitor`.
- Reuse existing abstractions instead of calling `File.*`, `Directory.*`, or process APIs directly in service code.
- `CacheProtector` owns watcher resources and should continue to follow the existing `IDisposable` pattern.
- `ProcessMonitor` is responsible for detecting and launching `WowClassic.exe`.
- `SettingsService` stores `AppSettings.json` beside the executable and auto-detects `GamePath`.
- `TemplateCaptureService` creates account templates and tokenized character templates with optional shared account-scoped settings.
- `TemplateApplyService` chooses targeted writes for live WoW and uses rollback-aware replacement only when WoW is closed.
- `TemplateRestoreOrchestrator` snapshots targets through `IChangeHistoryService` before apply. For live WoW it follows `Unlock -> apply -> Lock -> ForceRestore`, then prompts for `/reload`.
- `ChangeHistoryService` owns bounded tar.gz snapshots, list/restore/delete operations, and a pre-rollback snapshot.
- `SwitchingOrchestrator` is cache and launch only. Do not reintroduce account switching or saved-account behavior there.

## Domain Rules

- Templates are the only transfer mechanism. The top-level UI modes are `Templates` and `History`; do not reintroduce saved accounts, profile switching, profile filters, or a saved-account store.
- Templates are stored under `<ProfilesPath>/.templates/<id>/`. Account templates capture account-scoped files. Character templates capture tokenized character files and can include `Shared/` account-scoped settings.
- `TemplateApplyScope.Full` transfers all applicable files; `TemplateApplyScope.CacheOnly` transfers the cache-backed subset only.
- Every operation that overwrites live `WTF` content must complete an `IChangeHistoryService` snapshot for every affected target first. This is mandatory and may not be bypassed by an option.
- Character restore with account-scoped settings must snapshot both the character target and the account target before applying the template.
- History is stored under `<ProfilesPath>/.history/<target-key>/` as tar.gz archives and `index.json`. `AppSettings.MaxHistoryEntriesPerTarget` defaults to 20 and limits retained entries per target.
- History restore is offline only: resolve the live target, snapshot it again, then restore using `IDirectoryReplacer`. The UI must require WoW to be closed.
- While WoW is running, never use `IDirectoryReplacer` or folder swap. Apply files in place, then re-establish cache protection and prompt the user to run `/reload`.
- Clear read-only attributes before overwriting live files or deleting a staging folder. Preserve rollback behavior for closed-game directory replacement.
- `CacheFilePatterns` is the single source of truth for protected cache files. Cache protection combines a read-only lock, in-memory backup, `FileSystemWatcher` restore, and timestamp touch.

## C# Style

- Use file-scoped namespaces.
- `ImplicitUsings` and nullable reference types are enabled; add only necessary non-global `using` directives.
- Prefer collection expressions, method groups, async BCL APIs, and `string.Empty`.
- Do not add `async` to methods that never `await`.
- Avoid `#region` / `#endregion`.
- Use `PascalCase` for types, constants, and XAML resource keys; use `_camelCase` for private fields.
- Models should usually use `required` properties with `init`; use `set` only when mutation or deserialization requires it.
- Add comments only when they explain non-obvious intent or WoW-specific behavior.

## WPF and XAML

- Keep the existing dark theme and reuse established resource keys and button styles.
- Continue using `BooleanToVisibilityConverter` from `App.xaml`.
- Follow existing binding patterns, including `RelativeSource` bindings inside templates.
- The settings overlay remains a full-grid-span `Border` controlled by `IsSettingsVisible`.

## Logging and Errors

- Services should surface log messages through `event Action<string>? Log`.
- The ViewModel log format is `[HH:mm:ss] message\n`.
- Prefix warnings with `Warning:` and errors with `ERROR:`.
- Surface critical failures using the existing user-visible dialog pattern. Do not silently swallow errors.

## Testing

- Test project structure should mirror the source structure.
- Use NUnit, AutoFixture with AutoNSubstitute, NSubstitute, and Shouldly.
- Follow Arrange / Act / Assert with explicit section comments.
- Freeze mocks during setup and construct the SUT with injected dependencies.
- Keep unit tests isolated from the real filesystem and process APIs.

## Build and Publish

- Build: `dotnet build HearthSwing.slnx -c Release`
- Test: `dotnet test HearthSwing.slnx -c Release`
- Publish: `dotnet publish HearthSwing\HearthSwing.csproj -c Release`

## When Adding Features

1. Add or update the relevant model, service, ViewModel, view, and tests.
2. Keep models logic-free.
3. Wire new services through `App.ConfigureServices()` and subscribe logs in `MainViewModel` when needed.
4. Route live `WTF` mutations through `ITemplateRestoreOrchestrator`; complete all required History snapshots before applying the mutation.
5. Follow existing XAML styling, segmented `Templates | History` mode bindings, overlays, confirmations, and toast conventions instead of introducing parallel patterns.
