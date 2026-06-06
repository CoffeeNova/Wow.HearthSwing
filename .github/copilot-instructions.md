# HearthSwing — Copilot Instructions

Use these instructions for all work in this repository. For full project context, refer to `CLAUDE.md`.

## Project Overview

- WPF desktop application targeting .NET 10 and `win-x64`.
- Purpose: switch World of Warcraft Classic Anniversary `WTF` settings profiles between multiple users on one PC.
- Architecture is MVVM: `Models -> Services -> ViewModels -> Views`.

## Architecture

- Keep responsibilities separated:
  - `Models/` contains data-only types such as `AppSettings` and `ProfileInfo`.
  - `Services/` contains business logic and infrastructure abstractions.
  - `ViewModels/` contains MVVM state and commands.
  - Root XAML files define views.
- Register services in `MainWindow.ConfigureServices()` and use constructor injection throughout.
- Services must depend on interfaces, not concrete implementations.
- Keep filesystem access behind `IFileSystem` and process access behind `IProcessManager`.
- Do not move business logic into XAML code-behind. `MainWindow.xaml.cs` is only for UI-specific behavior.
- Preserve rollback behavior for multi-step filesystem operations such as profile switching.

## MVVM Conventions

- ViewModels inherit `ObservableObject`.
- Prefer `[ObservableProperty]` and `[RelayCommand]` from CommunityToolkit.Mvvm.
- Use `_camelCase` private backing fields for generated properties.
- Use `ObservableCollection<T>` for list bindings.
- `DataContext` is set via DI; ViewModels must not instantiate their own services.
- Use the WPF dispatcher for cross-thread UI updates.

## Service Conventions

- Services are `public sealed class` types implementing interfaces such as `ISettingsService`, `IProfileManager`, `ICacheProtector`, and `IProcessMonitor`.
- Reuse existing abstractions instead of calling `File.*`, `Directory.*`, or process APIs directly in service code.
- `CacheProtector` owns watcher resources and should continue to follow the existing `IDisposable` pattern.
- `ProcessMonitor` is responsible for detecting and launching `WowClassic.exe`.
- `SettingsService` stores `AppSettings.json` beside the executable and auto-detects `GamePath`.

## Domain Rules

- Profile folders under `ProfilesPath` are snapshots of the WoW `WTF` folder.
- The `.active` marker in `ProfilesPath` identifies the active profile whose folder is currently absent.
- Profile switching flow is:
  1. Park the current `WTF` folder into the current profile folder.
  2. Move the target profile folder into `WTF`.
  3. Update the `.active` marker.
- Support same-volume move and cross-volume copy/delete behavior.
- Clear read-only attributes before directory moves.
- Cache protection depends on four layers working together: folder swap, read-only lock, watcher restore, and timestamp touch.

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
3. Wire new services through DI and subscribe logs in `MainViewModel` when needed.
4. Follow existing XAML styling and binding conventions instead of introducing parallel patterns.
