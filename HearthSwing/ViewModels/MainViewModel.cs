using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HearthSwing.Models;
using HearthSwing.Services;

namespace HearthSwing.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IProfileManager _profileManager;
    private readonly ICacheProtector _cacheProtector;
    private readonly IProcessMonitor _processMonitor;
    private readonly IFileSystem _fs;
    private readonly IUpdateService _updateService;
    private readonly IProfileVersionService _versionService;
    private readonly Action<string, string> _showError;
    private readonly Func<string, string, bool> _showConfirm;
    private CancellationTokenSource? _unlockCts;
    private CancellationTokenSource? _monitorCts;
    private TaskCompletionSource<bool>? _savePromptTcs;
    private readonly object _archiveLock = new();
    private int _activeArchiveCount;
    private TaskCompletionSource? _archiveDoneTcs;

    [ObservableProperty]
    private string _currentProfileName = "None";

    [ObservableProperty]
    private string _currentProfileId = string.Empty;

    [ObservableProperty]
    private string _logText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isWowRunning;

    [ObservableProperty]
    private bool _isCacheLocked;

    [ObservableProperty]
    private string _gamePath = string.Empty;

    [ObservableProperty]
    private string _profilesPath = string.Empty;

    [ObservableProperty]
    private int _unlockDelay = 120;

    [ObservableProperty]
    private bool _isSettingsVisible;

    [ObservableProperty]
    private bool _isAboutVisible;

    [ObservableProperty]
    private bool _isHowToUseVisible;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private int _unlockCountdown;

    [ObservableProperty]
    private string _newProfileName = string.Empty;

    [ObservableProperty]
    private bool _isCheckingForUpdate;

    [ObservableProperty]
    private bool _versioningEnabled = true;

    [ObservableProperty]
    private int _maxVersionsPerProfile = 5;

    [ObservableProperty]
    private bool _saveOnExitEnabled = true;

    [ObservableProperty]
    private bool _autoSaveOnExit;

    [ObservableProperty]
    private bool _isSavePromptVisible;

    [ObservableProperty]
    private string _savePromptProfileName = string.Empty;

    [ObservableProperty]
    private bool _isVersionHistoryVisible;

    [ObservableProperty]
    private bool _isArchiving;

    [ObservableProperty]
    private bool _isCloseBlockedByArchiving;

    public ObservableCollection<ProfileInfo> Profiles { get; } = [];

    public ObservableCollection<ProfileVersion> Versions { get; } = [];

    public string AppVersion { get; } = GetVersion();

    private static string GetVersion()
    {
        var version =
            Assembly
                .GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion
            ?? "0.0.0";

        // MSBuild appends "+commitHash" to InformationalVersion
        var plusIndex = version.IndexOf('+');
        return plusIndex >= 0 ? version[..plusIndex] : version;
    }

    public MainViewModel(
        ISettingsService settingsService,
        IProfileManager profileManager,
        ICacheProtector cacheProtector,
        IProcessMonitor processMonitor,
        IFileSystem fileSystem,
        IUpdateService updateService,
        IProfileVersionService versionService,
        AppLogger logger,
        Action<string, string> showError,
        Func<string, string, bool> showConfirm
    )
    {
        _settingsService = settingsService;
        _profileManager = profileManager;
        _cacheProtector = cacheProtector;
        _processMonitor = processMonitor;
        _fs = fileSystem;
        _updateService = updateService;
        _versionService = versionService;
        _showError = showError;
        _showConfirm = showConfirm;

        logger.SetSink(AppendLog);

        GamePath = settingsService.Current.GamePath;
        ProfilesPath = settingsService.Current.ProfilesPath;
        UnlockDelay = settingsService.Current.UnlockDelaySeconds;
        VersioningEnabled = settingsService.Current.VersioningEnabled;
        MaxVersionsPerProfile = settingsService.Current.MaxVersionsPerProfile;
        SaveOnExitEnabled = settingsService.Current.SaveOnExitEnabled;
        AutoSaveOnExit = settingsService.Current.AutoSaveOnExit;

        RefreshState();
    }

    private void RefreshState()
    {
        var profile = _profileManager.DetectCurrentProfile();
        CurrentProfileName = profile?.DisplayName ?? "None";
        CurrentProfileId = profile?.Id ?? string.Empty;
        NewProfileName = profile?.Id ?? string.Empty;
        IsWowRunning = _processMonitor.IsWowRunning();
        IsCacheLocked = _cacheProtector.IsLocked;

        Profiles.Clear();
        foreach (var p in _profileManager.DiscoverProfiles())
            Profiles.Add(p);
    }

    [RelayCommand]
    private void SwitchProfile(string profileId)
    {
        if (IsBusy)
            return;

        var target = FindProfile(profileId);
        if (target is null)
            return;

        if (IsAlreadyActive(target))
            return;

        if (GuardWowRunning("Close the game before switching profiles."))
            return;

        IsBusy = true;
        StatusText = "Switching...";
        try
        {
            UnlockCacheIfNeeded();
            _profileManager.SwitchTo(target, AppendLog);
            RefreshState();
            StatusText = $"Active: {CurrentProfileName}";
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            StatusText = "Switch failed!";
            _showError(ex.Message, "Switch Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveCurrentProfileAsync()
    {
        var name = SanitizeProfileName(NewProfileName);
        if (string.IsNullOrEmpty(name))
        {
            AppendLog("Enter a profile name first.");
            return;
        }

        if (GuardWowRunning("Close the game before saving a profile."))
            return;

        IsBusy = true;
        try
        {
            UnlockCacheIfNeeded();
            await SaveActiveProfileWithVersioningAsync(name);
            RefreshState();
            StatusText = $"Profile '{name}' saved.";
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task LaunchWowAsync()
    {
        if (IsBusy)
            return Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(GamePath))
        {
            AppendLog("ERROR: Game path is not set.");
            return Task.CompletedTask;
        }

        IsBusy = true;
        try
        {
            LockCacheFiles();
            _processMonitor.LaunchWow(GamePath);
            IsWowRunning = true;
            AppendLog("WoW launched. Cache files are protected from server sync.");

            StartUnlockCountdown();
            StartProcessMonitor();
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            StatusText = "Launch failed!";
        }
        finally
        {
            IsBusy = false;
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private void ForceUnlock()
    {
        _unlockCts?.Cancel();
        _cacheProtector.Unlock();
        IsCacheLocked = false;
        UnlockCountdown = 0;
        StatusText = IsWowRunning ? "WoW running (cache unlocked)" : "Ready";
        AppendLog("Cache protection manually released.");
    }

    [RelayCommand]
    private void ForceRestore()
    {
        var wtfPath = Path.Combine(GamePath, "WTF");
        if (!_fs.DirectoryExists(wtfPath))
        {
            AppendLog("ERROR: WTF folder not found.");
            return;
        }

        if (IsWowRunning)
        {
            SeedMissingCacheFilesFromProfile(wtfPath);
            _cacheProtector.ForceRestore(wtfPath);
            IsCacheLocked = _cacheProtector.IsLocked;
            StatusText = "Files restored — type /reload in WoW!";
        }
        else
        {
            RestoreFromSavedProfile();
        }
    }

    private void SeedMissingCacheFilesFromProfile(string wtfPath)
    {
        if (string.IsNullOrEmpty(CurrentProfileId))
            return;

        var profilePath = Path.Combine(ProfilesPath, CurrentProfileId);
        if (!_fs.DirectoryExists(profilePath))
            return;

        var profileCacheFiles = _cacheProtector.CollectCacheFiles(profilePath);
        var seeded = 0;

        foreach (var profileFile in profileCacheFiles)
        {
            var relativePath = Path.GetRelativePath(profilePath, profileFile);
            var wtfFile = Path.Combine(wtfPath, relativePath);

            if (_fs.FileExists(wtfFile))
                continue;

            var dir = Path.GetDirectoryName(wtfFile);
            if (dir is not null && !_fs.DirectoryExists(dir))
                _fs.CreateDirectory(dir);

            _fs.CopyFile(profileFile, wtfFile);
            seeded++;
        }

        if (seeded > 0)
            AppendLog($"Restored {seeded} missing cache file(s) from saved profile.");
    }

    private void RestoreFromSavedProfile()
    {
        if (string.IsNullOrEmpty(CurrentProfileId))
        {
            AppendLog("No active profile to restore.");
            return;
        }

        IsBusy = true;
        StatusText = "Restoring...";
        try
        {
            UnlockCacheIfNeeded();
            _profileManager.RestoreActiveProfile(AppendLog);
            RefreshState();
            StatusText = $"Profile restored: {CurrentProfileName}";
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            StatusText = "Restore failed!";
            _showError(ex.Message, "Restore Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        IsSettingsVisible = !IsSettingsVisible;
    }

    [RelayCommand]
    private void ToggleAbout()
    {
        IsAboutVisible = !IsAboutVisible;
    }

    [RelayCommand]
    private void ToggleHowToUse()
    {
        IsHowToUseVisible = !IsHowToUseVisible;
    }

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        if (IsCheckingForUpdate)
            return;

        IsCheckingForUpdate = true;
        try
        {
            AppendLog("Checking for updates...");
            var result = await _updateService.CheckForUpdateAsync(
                AppVersion,
                CancellationToken.None
            );

            if (result is null)
            {
                AppendLog($"You're running the latest version ({AppVersion}).");
                return;
            }

            AppendLog($"New version available: {result.Version} (current: {AppVersion}).");

            if (
                !_showConfirm(
                    $"Version {result.Version} is available.\nUpdate now?",
                    "Update Available"
                )
            )
            {
                AppendLog("Update cancelled by user.");
                return;
            }

            await _updateService.ApplyUpdateAsync(result, CancellationToken.None);
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: Update check failed — {ex.Message}");
        }
        finally
        {
            IsCheckingForUpdate = false;
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _settingsService.Current.GamePath = GamePath;
        _settingsService.Current.ProfilesPath = ProfilesPath;
        _settingsService.Current.UnlockDelaySeconds = UnlockDelay;
        _settingsService.Current.VersioningEnabled = VersioningEnabled;
        _settingsService.Current.MaxVersionsPerProfile = MaxVersionsPerProfile;
        _settingsService.Current.SaveOnExitEnabled = SaveOnExitEnabled;
        _settingsService.Current.AutoSaveOnExit = AutoSaveOnExit;
        _settingsService.Save();
        IsSettingsVisible = false;
        AppendLog("Settings saved.");
        RefreshState();
    }

    private ProfileInfo? FindProfile(string profileId)
    {
        var target = Profiles.FirstOrDefault(p =>
            p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase)
        );
        if (target is null)
            AppendLog($"Profile '{profileId}' not found.");
        return target;
    }

    private bool IsAlreadyActive(ProfileInfo target)
    {
        if (!target.Id.Equals(CurrentProfileId, StringComparison.OrdinalIgnoreCase))
            return false;

        AppendLog($"'{target.DisplayName}' is already active.");
        return true;
    }

    /// <returns>True if WoW is running and the operation should be aborted.</returns>
    private bool GuardWowRunning(string reason)
    {
        if (!_processMonitor.IsWowRunning())
            return false;

        AppendLog($"ERROR: WoW is running. {reason}");
        _showError($"WoW is currently running!\n{reason}", "HearthSwing");
        return true;
    }

    private void UnlockCacheIfNeeded()
    {
        if (!_cacheProtector.IsLocked)
            return;

        _cacheProtector.Unlock();
        IsCacheLocked = false;
    }

    private static string? SanitizeProfileName(string? raw)
    {
        var name = raw?.Trim();
        if (string.IsNullOrEmpty(name))
            return null;

        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        return name;
    }

    private void LockCacheFiles()
    {
        var wtfPath = Path.Combine(GamePath, "WTF");
        if (!_fs.DirectoryExists(wtfPath))
            return;

        _cacheProtector.Lock(wtfPath);
        IsCacheLocked = true;
        StatusText = $"Protected ({_cacheProtector.ProtectedFileCount} files) — Launching WoW...";
    }

    private void StartUnlockCountdown()
    {
        _unlockCts?.Cancel();
        _unlockCts = new CancellationTokenSource();
        _ = RunUnlockCountdownAsync(UnlockDelay, _unlockCts.Token);
    }

    private void StartProcessMonitor()
    {
        _monitorCts?.Cancel();
        _monitorCts = new CancellationTokenSource();
        _ = MonitorWowAsync(_monitorCts.Token);
    }

    private async Task RunUnlockCountdownAsync(int totalSeconds, CancellationToken ct)
    {
        try
        {
            for (var i = totalSeconds; i > 0; i--)
            {
                if (ct.IsCancellationRequested)
                    break;
                UnlockCountdown = i;
                StatusText = $"Cache locked — unlock in {i}s";
                await Task.Delay(1000, ct);
            }

            if (!ct.IsCancellationRequested)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    _cacheProtector.Unlock();
                    IsCacheLocked = false;
                    UnlockCountdown = 0;
                    StatusText = IsWowRunning ? "WoW running (cache unlocked)" : "Ready";
                    AppendLog($"Cache protection released after {totalSeconds}s.");
                });
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task MonitorWowAsync(CancellationToken ct)
    {
        try
        {
            await _processMonitor.WaitForExitAsync(ct);

            // WoW may still be flushing writes after the process exits
            await Task.Delay(2000, ct);

            Application.Current?.Dispatcher.Invoke(() =>
            {
                IsWowRunning = false;
                UnlockCacheIfNeeded();
                UnlockCountdown = 0;
                StatusText = "WoW closed. Ready.";
                AppendLog("WoW process exited.");
            });

            if (SaveOnExitEnabled)
                await HandleSaveOnExitAsync();
        }
        catch (OperationCanceledException) { }
    }

    private async Task HandleSaveOnExitAsync()
    {
        var profileId = CurrentProfileId;
        if (string.IsNullOrEmpty(profileId))
            return;

        var wtfPath = Path.Combine(GamePath, "WTF");
        if (!_fs.DirectoryExists(wtfPath))
            return;

        if (AutoSaveOnExit)
        {
            await SaveActiveProfileWithVersioningAsync(profileId);
            Application.Current?.Dispatcher.Invoke(() =>
            {
                RefreshState();
                StatusText = $"Profile '{profileId}' auto-saved.";
            });
            return;
        }

        var tcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _savePromptTcs = tcs;
        Application.Current?.Dispatcher.Invoke(() =>
        {
            SavePromptProfileName = profileId;
            IsSavePromptVisible = true;
        });

        var accepted = await tcs.Task;
        if (accepted)
        {
            await SaveActiveProfileWithVersioningAsync(profileId);
            Application.Current?.Dispatcher.Invoke(() =>
            {
                RefreshState();
                StatusText = $"Profile '{profileId}' saved.";
            });
        }
        else
        {
            AppendLog("Save skipped by user.");
        }
    }

    [RelayCommand]
    private void ToggleVersionHistory()
    {
        if (IsVersionHistoryVisible)
        {
            IsVersionHistoryVisible = false;
            return;
        }

        var profileId = CurrentProfileId;
        if (string.IsNullOrEmpty(profileId))
        {
            AppendLog("No active profile — nothing to show.");
            return;
        }

        Versions.Clear();
        foreach (var v in _versionService.GetVersions(profileId))
            Versions.Add(v);

        IsVersionHistoryVisible = true;
    }

    [RelayCommand]
    private async Task RestoreVersionAsync(string versionId)
    {
        var version = Versions.FirstOrDefault(v => v.VersionId == versionId);
        if (version is null)
            return;

        if (GuardWowRunning("Close the game before restoring a version."))
            return;

        try
        {
            await RunTrackedArchiveAsync(_versionService.RestoreVersionAsync(version));
            IsVersionHistoryVisible = false;
            RefreshState();
            StatusText = $"Restored version {version.DisplayName}.";
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
        }
    }

    [RelayCommand]
    private void DeleteVersion(string versionId)
    {
        var version = Versions.FirstOrDefault(v => v.VersionId == versionId);
        if (version is null)
            return;

        try
        {
            _versionService.DeleteVersion(version);
            Versions.Remove(version);
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
        }
    }

    [RelayCommand]
    private void AcceptSavePrompt()
    {
        IsSavePromptVisible = false;
        _savePromptTcs?.TrySetResult(true);
    }

    [RelayCommand]
    private void SkipSavePrompt()
    {
        IsSavePromptVisible = false;
        _savePromptTcs?.TrySetResult(false);
    }

    private async Task SaveActiveProfileWithVersioningAsync(string profileId)
    {
        var profilePath = Path.Combine(_profileManager.ProfilesPath, profileId);
        if (VersioningEnabled && _fs.DirectoryExists(profilePath))
            await RunTrackedArchiveAsync(_versionService.CreateVersionAsync(profileId));

        _profileManager.SaveCurrentAsProfile(profileId, AppendLog);
    }

    private async Task RunTrackedArchiveAsync(Task archiveTask)
    {
        lock (_archiveLock)
        {
            _activeArchiveCount++;
            _archiveDoneTcs ??= new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
        }

        IsArchiving = true;
        try
        {
            await archiveTask;
        }
        finally
        {
            TaskCompletionSource? tcs = null;
            lock (_archiveLock)
            {
                _activeArchiveCount--;
                if (_activeArchiveCount == 0)
                {
                    tcs = _archiveDoneTcs;
                    _archiveDoneTcs = null;
                }
            }

            if (tcs is not null)
            {
                IsArchiving = false;
                tcs.TrySetResult();
            }
        }
    }

    public Task WaitForArchivingAsync()
    {
        lock (_archiveLock)
        {
            if (_activeArchiveCount == 0)
                return Task.CompletedTask;

            _archiveDoneTcs ??= new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            return _archiveDoneTcs.Task;
        }
    }

    private void AppendLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var line = $"[{timestamp}] {message}\n";

        if (Application.Current?.Dispatcher.CheckAccess() == false)
        {
            Application.Current.Dispatcher.Invoke(() => LogText += line);
        }
        else
        {
            LogText += line;
        }
    }
}
