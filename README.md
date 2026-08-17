# HearthSwing

HearthSwing is a WPF desktop application for capturing, transferring, and
recovering World of Warcraft Classic Anniversary settings stored in the `WTF`
folder. It uses portable templates for transfer and keeps a bounded history of
live `WTF` changes for recovery.

## Features

- **Account and character templates**: capture settings from a live account or character and apply them to another target.
- **Character personalization**: character templates replace donor character and realm values with target values where supported.
- **Live template apply**: apply templates while WoW is running without swapping folders, then use `/reload` to pick up protected cache settings.
- **WTF history**: automatically archive affected targets before a template overwrites them, then restore or delete history points from the app.
- **Cache protection**: lock and monitor cache files so server synchronization cannot overwrite local settings after launch.
- **Direct launch**: start WoW from HearthSwing with cache protection enabled.
- **Flexible storage**: configure the game folder and a Profiles folder independently; HearthSwing can be installed anywhere.

## What Templates Transfer

Account templates capture account-level files, including top-level configuration
and `SavedVariables`. Character templates capture the selected character folder
and shared account settings. When applying a character template, you can choose
whether to also apply its shared account settings.

This includes settings such as:
- **Macros** (`macros-cache.txt`)
- **Keybindings** (`bindings-cache.wtf`)
- **Edit Mode layout** (`edit-mode-cache-*.txt`, `layout-local.txt`)
- **Addon settings** (`SavedVariables/`)
- **Client config** (`config-cache.wtf`, `Config.wtf`)

**Action bars are not stored in `WTF` and are not transferred by HearthSwing.**
Use [ActionBarSaver: Reloaded](https://www.curseforge.com/wow/addons/actionbarsaver-reloaded)
or another addon compatible with your game version to manage action-bar layouts.

## How to Use

### 1. Install

Download `HearthSwing.exe` from the [Releases](../../releases) page and place it anywhere on your PC. No installation required — it's a single-file self-contained executable.

### 2. Configure Paths

Start `HearthSwing.exe`, open **Settings**, and set:

- **Game Path**: the folder containing `WowClassic.exe`.
- **Profiles Path**: the folder where HearthSwing stores templates and history. It defaults to `Profiles` next to the executable.

If the executable is inside the WoW game folder, HearthSwing auto-detects the
game path.

### 3. Create a Template

Open the **Templates** mode and select **New Template**. Choose an account or
character donor, select the template type, and give it a name. The resulting
template is stored under `Profiles/.templates/`.

### 4. Apply a Template

Select **Apply** on a template card, choose the target account or character, and
select the restore scope:

- **Full**: transfer all applicable template files.
- **Cache only**: transfer only cache-backed settings such as macros, keybindings, and layout.

Before HearthSwing changes live `WTF` content, it automatically creates one or
more history snapshots of the affected target. Account-level settings can affect
every character on the target account, so review the confirmation carefully.

### 5. Apply While WoW Is Running

HearthSwing does not replace directories while WoW is running. It writes files
in place, re-establishes cache protection, and asks you to enter **`/reload`**
in the game. Cache-backed settings can take effect after `/reload`; changes to
`SavedVariables` take effect when the relevant character next logs in.

### 6. Recover with History

Open the **History** mode to view automatic change points grouped by their live
target. Close WoW, then select **Restore** to overwrite that target with an
archive. HearthSwing snapshots the current state before rollback, so the
rollback itself remains recoverable.

The default limit is 20 history entries per target. Change it in **Settings**,
or delete individual history entries when they are no longer needed.

### 7. Launch WoW and Protect Cache Files

Select **Launch WoW** to start the game with cache protection enabled. HearthSwing
keeps cache files read-only and monitors them during the configured protection
period (120 seconds by default). While WoW is running, you can:

- Select **Restore Cache**, then enter `/reload`, to rewrite protected files from the in-memory backup.
- Select **Unlock** to release cache protection early.

## Storage

`Profiles Path` contains HearthSwing-managed application data:

```text
Profiles/
	.templates/<template-id>/   Template metadata and captured files
	.history/<target-key>/      index.json and tar.gz history archives
```

The optional cleanup action in Settings removes obsolete folders and markers
created by pre-history releases.

## For Developers

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (Windows)
- Visual Studio 2022+ or VS Code with C# Dev Kit

### Project Structure

```text
HearthSwing/                 Main WPF application
	Models/                    App settings, history, template, and WoW models
	Services/                  Template, history, cache, process, and I/O services
	ViewModels/                CommunityToolkit.Mvvm view models
	MainWindow.xaml            Main view
	MainWindow.xaml.cs         UI-only code-behind helpers

HearthSwing.Tests/           NUnit unit tests mirroring the source structure
	Services/                  Service tests
	ViewModels/                View-model tests
```

### Architecture

The application follows MVVM: `Models -> Services -> ViewModels -> Views`.
`App.ConfigureServices()` registers services as singletons through
`Microsoft.Extensions.DependencyInjection`.

| Interface | Implementation | Role |
|-----------|---------------|------|
| `IFileSystem` | `FileSystem` | Abstracts filesystem access for tests. |
| `IProcessManager` | `SystemProcessManager` | Abstracts process access. |
| `IWtfInspector` | `WtfInspector` | Discovers the live account, realm, and character hierarchy. |
| `ITemplateCatalog` | `TemplateCatalog` | Stores and manages templates. |
| `ITemplateCaptureService` | `TemplateCaptureService` | Captures account and character templates. |
| `ITemplateRestoreOrchestrator` | `TemplateRestoreOrchestrator` | Takes history snapshots and applies templates safely for the WoW process state. |
| `IChangeHistoryService` | `ChangeHistoryService` | Creates, lists, restores, trims, and deletes history archives. |
| `ICacheProtector` | `CacheProtector` | Provides read-only locking, watcher recovery, and timestamp refresh. |
| `ISwitchingOrchestrator` | `SwitchingOrchestrator` | Coordinates launch-time cache lock, unlock, and cache restore. |
| `IProcessMonitor` | `ProcessMonitor` | Detects, launches, and monitors `WowClassic.exe`. |

### Build

```bash
dotnet build HearthSwing.slnx -c Release
```

### Test

```bash
dotnet test HearthSwing.slnx -c Release
```

### Publish

```bash
dotnet publish HearthSwing/HearthSwing.csproj -c Release
```

Produces a single-file self-contained executable (`~140 MB`) in `HearthSwing/bin/Release/net10.0-windows/win-x64/publish/`.

### CI/CD

GitHub Actions workflow (`.github/workflows/build.yml`) runs on push to `main` or manual dispatch:
1. Versioning — reads `<Version>` from csproj, appends run number (`1.0.0.N`).
2. Build & Test — `dotnet build` + `dotnet test`.
3. Publish — produces the single-file artifact.
4. Release — on `main`, creates a GitHub Release with the zipped artifact.

## License

See [LICENSE](LICENSE).
