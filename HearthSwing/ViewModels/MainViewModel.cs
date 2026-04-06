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
    private readonly Action<string, string> _showError;
    private CancellationTokenSource? _unlockCts;

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

    public ObservableCollection<ProfileInfo> Profiles { get; } = [];

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
        Action<string, string> showError
    )
    {
        _settingsService = settingsService;
        _profileManager = profileManager;
        _cacheProtector = cacheProtector;
        _processMonitor = processMonitor;
        _fs = fileSystem;
        _showError = showError;

        _cacheProtector.Log += AppendLog;
        _processMonitor.Log += AppendLog;

        GamePath = settingsService.Current.GamePath;
        ProfilesPath = settingsService.Current.ProfilesPath;
        UnlockDelay = settingsService.Current.UnlockDelaySeconds;

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
    private void SaveCurrentProfile()
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
            _profileManager.SaveCurrentAsProfile(name, AppendLog);
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

        _cacheProtector.ForceRestore(wtfPath);
        IsCacheLocked = _cacheProtector.IsLocked;
        StatusText = "Files restored — type /reload in WoW!";
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
    private void SaveSettings()
    {
        _settingsService.Current.GamePath = GamePath;
        _settingsService.Current.ProfilesPath = ProfilesPath;
        _settingsService.Current.UnlockDelaySeconds = UnlockDelay;
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
        var ct = _unlockCts?.Token ?? CancellationToken.None;
        _ = MonitorWowAsync(ct);
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
            Application.Current?.Dispatcher.Invoke(() =>
            {
                IsWowRunning = false;
                UnlockCacheIfNeeded();
                UnlockCountdown = 0;
                StatusText = "WoW closed. Ready.";
                AppendLog("WoW process exited.");
            });
        }
        catch (OperationCanceledException) { }
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
