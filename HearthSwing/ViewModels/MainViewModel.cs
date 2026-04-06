using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HearthSwing.Models;
using HearthSwing.Services;

namespace HearthSwing.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly ProfileManager _profileManager;
    private readonly CacheProtector _cacheProtector;
    private readonly ProcessMonitor _processMonitor;
    private CancellationTokenSource? _unlockCts;

    [ObservableProperty]
    private string _currentProfileName = "None";

    [ObservableProperty]
    private string _currentProfileId = "";

    [ObservableProperty]
    private string _logText = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isWowRunning;

    [ObservableProperty]
    private bool _isCacheLocked;

    [ObservableProperty]
    private string _gamePath = "";

    [ObservableProperty]
    private string _profilesPath = "";

    [ObservableProperty]
    private int _unlockDelay = 120;

    [ObservableProperty]
    private bool _isSettingsVisible;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private int _unlockCountdown;

    [ObservableProperty]
    private string _newProfileName = "";

    public ObservableCollection<ProfileInfo> Profiles { get; } = [];

    public MainViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _profileManager = new ProfileManager(settingsService);
        _cacheProtector = new CacheProtector();
        _processMonitor = new ProcessMonitor();

        _cacheProtector.Log += msg => AppendLog(msg);
        _processMonitor.Log += msg => AppendLog(msg);

        GamePath = settingsService.Current.GamePath;
        ProfilesPath = settingsService.Current.ProfilesPath;
        UnlockDelay = settingsService.Current.UnlockDelaySeconds;

        RefreshState();
    }

    public void RefreshState()
    {
        var profile = _profileManager.DetectCurrentProfile();
        CurrentProfileName = profile?.DisplayName ?? "None";
        CurrentProfileId = profile?.Id ?? "";
        IsWowRunning = _processMonitor.IsWowRunning();
        IsCacheLocked = _cacheProtector.IsLocked;

        // Refresh profiles list
        Profiles.Clear();
        foreach (var p in _profileManager.DiscoverProfiles())
            Profiles.Add(p);
    }

    [RelayCommand]
    private void SwitchProfile(string profileId)
    {
        if (IsBusy)
            return;

        var target = Profiles.FirstOrDefault(p =>
            p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase)
        );
        if (target is null)
        {
            AppendLog($"Profile '{profileId}' not found.");
            return;
        }

        if (target.Id.Equals(CurrentProfileId, StringComparison.OrdinalIgnoreCase))
        {
            AppendLog($"'{target.DisplayName}' is already active.");
            return;
        }

        if (_processMonitor.IsWowRunning())
        {
            AppendLog("ERROR: WoW is running. Close the game before switching profiles.");
            MessageBox.Show(
                "WoW is currently running!\nClose the game before switching profiles.",
                "WoW Profile Switcher",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        IsBusy = true;
        StatusText = "Switching...";
        try
        {
            if (_cacheProtector.IsLocked)
            {
                _cacheProtector.Unlock();
                IsCacheLocked = false;
            }

            _profileManager.SwitchTo(target, msg => AppendLog(msg));
            RefreshState();
            StatusText = $"Active: {CurrentProfileName}";
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            StatusText = "Switch failed!";
            MessageBox.Show(ex.Message, "Switch Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SaveCurrentProfile()
    {
        var name = NewProfileName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            AppendLog("Enter a profile name first.");
            return;
        }

        // Sanitize: only allow safe chars
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        if (_processMonitor.IsWowRunning())
        {
            AppendLog("ERROR: WoW is running. Close the game before saving a profile.");
            return;
        }

        IsBusy = true;
        try
        {
            _profileManager.SaveCurrentAsProfile(name, msg => AppendLog(msg));
            NewProfileName = "";
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
    private async Task LaunchWowAsync()
    {
        if (IsBusy)
            return;
        if (string.IsNullOrWhiteSpace(GamePath))
        {
            AppendLog("ERROR: Game path is not set.");
            return;
        }

        IsBusy = true;
        try
        {
            // Step 1: Lock cache files
            var wtfPath = Path.Combine(GamePath, "WTF");
            if (Directory.Exists(wtfPath))
            {
                _cacheProtector.Lock(wtfPath);
                IsCacheLocked = true;
                StatusText =
                    $"Protected ({_cacheProtector.ProtectedFileCount} files) — Launching WoW...";
            }

            // Step 2: Launch WoW
            _processMonitor.LaunchWow(GamePath);
            IsWowRunning = true;
            AppendLog("WoW launched. Cache files are protected from server sync.");

            // Step 3: Start unlock countdown
            _unlockCts?.Cancel();
            _unlockCts = new CancellationTokenSource();
            var ct = _unlockCts.Token;
            var delay = UnlockDelay;

            _ = RunUnlockCountdownAsync(delay, ct);

            // Step 4: Monitor WoW process in background
            _ = MonitorWowAsync(ct);
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
                if (_cacheProtector.IsLocked)
                {
                    _cacheProtector.Unlock();
                    IsCacheLocked = false;
                }
                UnlockCountdown = 0;
                StatusText = "WoW closed. Ready.";
                AppendLog("WoW process exited.");
            });
        }
        catch (OperationCanceledException) { }
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
        if (!Directory.Exists(wtfPath))
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
