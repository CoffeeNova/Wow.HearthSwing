# HearthSwing

WPF desktop application for switching World of Warcraft (Classic Anniversary) settings profiles between multiple users on the same PC.

Each player's keybindings, macros, UI layout, and addon settings live inside the WoW `WTF` folder. HearthSwing swaps the entire `WTF` folder between named profile snapshots so you can switch players in seconds without manual copying.

## Features

- **Profile switching** — swap the active `WTF` folder between saved snapshots with one click.
- **Cache protection** — locks cache files read-only and restores them from backup to prevent the WoW server from overwriting your local settings on login.
- **Flexible placement** — HearthSwing can live anywhere on your system; just point it to the game folder and profiles folder in Settings.
- **Launch WoW** — start the game directly from the app with cache protection enabled.
- **Save profiles** — snapshot the current `WTF` folder as a named profile at any time.

## What is transferred

HearthSwing swaps the entire `WTF` folder, which includes:
- **Macros** (`macros-cache.txt`)
- **Keybindings** (`bindings-cache.wtf`)
- **Edit Mode layout** (`edit-mode-cache-*.txt`, `layout-local.txt`)
- **Addon settings** (`SavedVariables/`)
- **Client config** (`config-cache.wtf`, `Config.wtf`)

**Action bars are NOT included** in the WTF folder and are NOT transferred by HearthSwing. To save and restore action bar layouts between profiles, use the [ActionBarSaver: Reloaded](https://www.curseforge.com/wow/addons/actionbarsaver-reloaded) addon (or a similar addon for your game version).

## How to Use

### 1. Install

Download `HearthSwing.exe` from the [Releases](../../releases) page and place it anywhere on your PC. No installation required — it's a single-file self-contained executable.

### 2. First launch

Run `HearthSwing.exe`. Open **Settings** and set:
- **Game Path** — the folder that contains `WowClassic.exe`.
- **Profiles Path** — the folder where profile snapshots will be stored (default: `Profiles` next to the exe).

If the exe happens to be inside the WoW game folder, the game path is auto-detected.

### 3. Save a profile

Type a name in the **Save WTF as** field and click **Save**. This copies your current `WTF` folder as a named profile snapshot.

### 4. Switch profiles

Click a profile button to swap the active `WTF` folder. The previous profile is parked back into `Profiles/` automatically.

After switching, launch WoW and click **Restore** in HearthSwing, then type **`/reload`** in the game chat. This forces WoW to re-read the restored cache files so your macros, keybindings, and UI layout apply immediately.

### 5. Launch WoW

Click **Launch WoW** to start the game with cache protection enabled. Cache files (`bindings-cache.wtf`, `config-cache.wtf`, `macros-cache.txt`, etc.) are locked read-only and monitored. If the server tries to overwrite them, HearthSwing restores the originals from an in-memory backup.

### 6. Cache protection

After launch, cache files stay locked for the configured delay (default 120 seconds). You can:
- Click **Unlock** to release the lock early.
- Click **Restore** while WoW is running to force-restore cache files from backup, then type **`/reload`** in-game to apply them.

## For Developers

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (Windows)
- Visual Studio 2022+ or VS Code with C# Dev Kit

### Project structure

```
HearthSwing/             Main WPF application
├── Models/              Data models (AppSettings, ProfileInfo)
├── Services/            Business logic (profile swapping, cache protection, process management)
├── ViewModels/          MVVM view models (CommunityToolkit.Mvvm)
├── MainWindow.xaml      UI
└── MainWindow.xaml.cs   Code-behind (visual-tree helpers only)

HearthSwing.Tests/       Unit tests (NUnit + NSubstitute + Shouldly + AutoFixture)
└── Services/            Tests for each service
```

### Architecture

MVVM with `Microsoft.Extensions.DependencyInjection`. All services are registered as singletons in `MainWindow.ConfigureServices()`.

| Interface | Implementation | Role |
|-----------|---------------|------|
| `IFileSystem` | `FileSystem` | Abstracts `System.IO` for testability |
| `IProcessManager` | `SystemProcessManager` | Abstracts `System.Diagnostics.Process` |
| `ISettingsService` | `SettingsService` | Loads/saves `AppSettings.json` |
| `IProfileManager` | `ProfileManager` | Discovers profiles, swaps WTF folders |
| `ICacheProtector` | `CacheProtector` | 4-layer cache protection strategy |
| `IProcessMonitor` | `ProcessMonitor` | Detects/launches WoW, monitors exit |

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

Produces a single-file self-contained executable (`~140 MB`) in `HearthSwing/bin/Release/net8.0-windows/win-x64/publish/`.

### CI/CD

GitHub Actions workflow (`.github/workflows/build.yml`) runs on push to `main` or manual dispatch:
1. Versioning — reads `<Version>` from csproj, appends run number (`1.0.0.N`).
2. Build & Test — `dotnet build` + `dotnet test`.
3. Publish — produces the single-file artifact.
4. Release — on `main`, creates a GitHub Release with the zipped artifact.

## License

See [LICENSE](LICENSE).
