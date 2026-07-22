using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HearthSwing.Models;
using HearthSwing.Models.Accounts;
using HearthSwing.Models.Templates;
using HearthSwing.Models.WoW;
using HearthSwing.Services;

namespace HearthSwing.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ISavedAccountCatalog _savedAccountCatalog;
    private readonly IAccountSnapshotDiffService _accountSnapshotDiffService;
    private readonly ISwitchingOrchestrator _orchestrator;
    private readonly IProcessMonitor _processMonitor;
    private readonly IUpdateService _updateService;
    private readonly IProfileVersionService _versionService;
    private readonly IDialogService _dialogService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IUiLogSink _logSink;
    private readonly IWtfInspector _wtfInspector;
    private readonly ITemplateCatalog _templateCatalog;
    private readonly ITemplateCaptureService _templateCaptureService;
    private readonly ITemplateApplyService _templateApplyService;
    private readonly ITemplateVersionService _templateVersionService;
    private WowInstallation? _installation;
    private WowAccount? _pendingLiveAccount;
    private string _templateToRenameId = string.Empty;
    private string _versionHistoryTemplateId = string.Empty;
    private CancellationTokenSource? _unlockCts;
    private CancellationTokenSource? _monitorCts;
    private CancellationTokenSource? _saveSelectionLoadCts;
    private readonly object _archiveLock = new();
    private int _activeArchiveCount;
    private TaskCompletionSource? _archiveDoneTcs;

    [ObservableProperty]
    private string _currentAccountName = "None";

    [ObservableProperty]
    private string _currentSavedAccountId = string.Empty;

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
    [NotifyPropertyChangedFor(nameof(CanConfirmSaveSelection))]
    private string? _selectedLiveAccountName;

    [ObservableProperty]
    private bool _isSaveSelectionVisible;

    [ObservableProperty]
    private string _saveSelectionTitle = "Save Account";

    [ObservableProperty]
    private string _saveSelectionMessage = "Choose a live account to save.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmSaveSelection))]
    private bool _isLoadingSaveSelection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmSaveSelection))]
    private bool _isNewSaveAccount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmSaveSelection))]
    private bool _saveAccountSettingsSelected;

    [ObservableProperty]
    private bool _hasPendingCharacterNodes;

    [ObservableProperty]
    private string _detectedLiveAccountsSummary = "No live accounts detected.";

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
    private bool _isVersionHistoryVisible;

    [ObservableProperty]
    private bool _isArchiving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArchivingDetailText))]
    private bool _isCloseBlockedByArchiving;

    [ObservableProperty]
    private string _archivingTitle = "Working...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAccountsMode))]
    [NotifyPropertyChangedFor(nameof(IsTemplatesMode))]
    private AppMode _activeMode = AppMode.Accounts;

    [ObservableProperty]
    private bool _isTemplateDonorSelectionVisible;

    [ObservableProperty]
    private bool _isTemplateApplyVisible;

    [ObservableProperty]
    private bool _isTemplateVersionHistoryVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmCreateTemplate))]
    private WowCharacter? _selectedDonorCharacter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmCreateTemplate))]
    private string _newTemplateName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmApplyTemplate))]
    private WowCharacter? _selectedTargetCharacter;

    [ObservableProperty]
    private bool _includeAccountSettings;

    [ObservableProperty]
    private TemplateSummary? _templateToApply;

    [ObservableProperty]
    private string _templateApplyTitle = "Apply Template";

    [ObservableProperty]
    private bool _isTemplateRenameVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmRenameTemplate))]
    private string _renameTemplateName = string.Empty;

    public ObservableCollection<SavedAccountSummary> SavedAccounts { get; } = [];

    public ObservableCollection<string> LiveAccounts { get; } = [];

    public ObservableCollection<RealmSaveSelectionViewModel> SaveRealms { get; } = [];

    public ObservableCollection<ProfileVersion> Versions { get; } = [];

    public ObservableCollection<TemplateSummary> Templates { get; } = [];

    public ObservableCollection<ProfileVersion> TemplateVersions { get; } = [];

    public ObservableCollection<WowCharacter> LiveCharacters { get; } = [];

    public bool IsAccountsMode => ActiveMode == AppMode.Accounts;

    public bool IsTemplatesMode => ActiveMode == AppMode.Templates;

    public bool CanConfirmCreateTemplate =>
        SelectedDonorCharacter is not null && !string.IsNullOrWhiteSpace(NewTemplateName);

    public bool CanConfirmApplyTemplate =>
        TemplateToApply is not null && SelectedTargetCharacter is not null;

    public bool CanConfirmRenameTemplate => !string.IsNullOrWhiteSpace(RenameTemplateName);

    public bool CanConfirmSaveSelection =>
        !IsLoadingSaveSelection
        && _pendingLiveAccount is not null
        && (
            IsNewSaveAccount
            || SaveAccountSettingsSelected
            || SaveRealms.Any(realm => realm.Characters.Any(character => character.IsSelected))
        );

    public string ArchivingDetailText =>
        IsCloseBlockedByArchiving
            ? "Please wait. The application will close once the save is complete."
            : "Please wait while HearthSwing completes the current operation.";

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
        ISavedAccountCatalog savedAccountCatalog,
        IAccountSnapshotDiffService accountSnapshotDiffService,
        ISwitchingOrchestrator orchestrator,
        IProcessMonitor processMonitor,
        IUpdateService updateService,
        IProfileVersionService versionService,
        IDialogService dialogService,
        IUiDispatcher uiDispatcher,
        IUiLogSink logSink,
        IWtfInspector wtfInspector,
        ITemplateCatalog templateCatalog,
        ITemplateCaptureService templateCaptureService,
        ITemplateApplyService templateApplyService,
        ITemplateVersionService templateVersionService
    )
    {
        _settingsService = settingsService;
        _savedAccountCatalog = savedAccountCatalog;
        _accountSnapshotDiffService = accountSnapshotDiffService;
        _orchestrator = orchestrator;
        _processMonitor = processMonitor;
        _updateService = updateService;
        _versionService = versionService;
        _dialogService = dialogService;
        _uiDispatcher = uiDispatcher;
        _logSink = logSink;
        _wtfInspector = wtfInspector;
        _templateCatalog = templateCatalog;
        _templateCaptureService = templateCaptureService;
        _templateApplyService = templateApplyService;
        _templateVersionService = templateVersionService;

        _logSink.MessageLogged += OnLogMessage;
        _orchestrator.Log += OnLogMessage;

        GamePath = settingsService.Current.GamePath;
        ProfilesPath = settingsService.Current.ProfilesPath;
        UnlockDelay = settingsService.Current.UnlockDelaySeconds;
        VersioningEnabled = settingsService.Current.VersioningEnabled;
        MaxVersionsPerProfile = settingsService.Current.MaxVersionsPerProfile;
        SaveOnExitEnabled = settingsService.Current.SaveOnExitEnabled;
        AutoSaveOnExit = settingsService.Current.AutoSaveOnExit;

        RefreshState();
        RefreshTemplates();
    }

    private void RefreshState()
    {
        RefreshSavedAccountState();
        IsWowRunning = _processMonitor.IsWowRunning();
        IsCacheLocked = _orchestrator.IsCacheLocked;

        if (!string.IsNullOrWhiteSpace(GamePath))
        {
            try
            {
                var installation = _wtfInspector.Inspect(GamePath);
                _installation = installation;
                UpdateLiveAccounts(installation);
            }
            catch (Exception ex)
            {
                _installation = null;
                LiveAccounts.Clear();
                DetectedLiveAccountsSummary = "No live accounts detected.";
                AppendLog($"Warning: WTF inspection failed — {ex.Message}");
            }
        }
        else
        {
            _installation = null;
            LiveAccounts.Clear();
            DetectedLiveAccountsSummary = "No live accounts detected.";
        }
    }

    private void RefreshSavedAccountState()
    {
        SavedAccounts.Clear();

        try
        {
            var activeSavedAccountState = _savedAccountCatalog.GetActiveAccount();
            var discoveredAccounts = _savedAccountCatalog.DiscoverAccounts();
            var activeSavedAccount = activeSavedAccountState is null
                ? null
                : discoveredAccounts.FirstOrDefault(account =>
                    account.Id.Equals(
                        activeSavedAccountState.SavedAccountId,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            CurrentAccountName =
                activeSavedAccount?.AccountName ?? activeSavedAccountState?.AccountName ?? "None";
            CurrentSavedAccountId =
                activeSavedAccount?.Id ?? activeSavedAccountState?.SavedAccountId ?? string.Empty;
            NewProfileName = activeSavedAccount?.AccountName ?? string.Empty;

            foreach (var account in discoveredAccounts)
                SavedAccounts.Add(account);
        }
        catch (InvalidOperationException ex)
        {
            CurrentAccountName = "None";
            CurrentSavedAccountId = string.Empty;
            NewProfileName = string.Empty;
            AppendLog($"Warning: {ex.Message}");
            _dialogService.ShowWarning(
                $"{ex.Message}\n\nChoose an empty Saved Accounts Path or migrate/remove the legacy folders before using this storage root.",
                "Saved Accounts Path Error"
            );
        }
    }

    [RelayCommand]
    private void SwitchSavedAccount(string savedAccountId)
    {
        if (IsBusy)
            return;

        var target = FindSavedAccount(savedAccountId);
        if (target is null)
            return;

        if (IsAlreadyActive(target))
            return;

        if (GuardWowRunning("Close the game before switching accounts."))
            return;

        IsBusy = true;
        StatusText = "Switching...";
        try
        {
            _orchestrator.SwitchTo(target);
            RefreshState();
            StatusText = $"Active account: {CurrentAccountName}";
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            StatusText = "Switch failed!";
            _dialogService.ShowWarning(ex.Message, "Switch Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task SaveAccountAsync()
    {
        if (GuardWowRunning("Close the game before saving an account."))
            return Task.CompletedTask;

        OpenSaveSelection(title: "Save Account");
        return Task.CompletedTask;
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
            var protectedCount = _orchestrator.LockForLaunch();
            _processMonitor.LaunchWow(GamePath);
            IsWowRunning = true;
            IsCacheLocked = _orchestrator.IsCacheLocked;
            StatusText =
                protectedCount > 0
                    ? $"Protected ({protectedCount} files) — Launching WoW..."
                    : "Launching WoW...";
            AppendLog("WoW launched. Cache files are protected from server sync.");

            StartUnlockCountdown();
            StartProcessMonitor();
        }
        catch (Exception ex)
        {
            _orchestrator.UnlockCache();
            IsCacheLocked = false;
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
        _orchestrator.UnlockCache();
        IsCacheLocked = false;
        UnlockCountdown = 0;
        StatusText = IsWowRunning ? "WoW running (cache unlocked)" : "Ready";
        AppendLog("Cache protection manually released.");
    }

    [RelayCommand]
    private void ForceRestore()
    {
        if (IsWowRunning)
        {
            _orchestrator.ForceRestoreCache();
            IsCacheLocked = _orchestrator.IsCacheLocked;
            StatusText = "Files restored — type /reload in WoW!";
        }
        else
        {
            if (string.IsNullOrEmpty(CurrentSavedAccountId))
            {
                AppendLog("No active saved account to restore.");
                return;
            }

            IsBusy = true;
            StatusText = "Restoring...";
            try
            {
                _orchestrator.RestoreFromSaved();
                RefreshState();
                StatusText = $"Account restored: {CurrentAccountName}";
            }
            catch (Exception ex)
            {
                AppendLog($"ERROR: {ex.Message}");
                StatusText = "Restore failed!";
                _dialogService.ShowWarning(ex.Message, "Restore Error");
            }
            finally
            {
                IsBusy = false;
            }
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
                !_dialogService.Confirm(
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

    private SavedAccountSummary? FindSavedAccount(string savedAccountId)
    {
        var target = SavedAccounts.FirstOrDefault(account =>
            account.Id.Equals(savedAccountId, StringComparison.OrdinalIgnoreCase)
        );

        if (target is null)
            AppendLog($"Saved account '{savedAccountId}' not found.");

        return target;
    }

    private bool IsAlreadyActive(SavedAccountSummary target)
    {
        if (!target.Id.Equals(CurrentSavedAccountId, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private bool GuardWowRunning(string message)
    {
        if (!IsWowRunning)
            return false;

        AppendLog(message);
        return true;
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
                _uiDispatcher.Invoke(() =>
                {
                    _orchestrator.UnlockCache();
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
            await _orchestrator.WaitForWowExitAndCleanupAsync(2000, ct);

            _uiDispatcher.Invoke(() =>
            {
                IsWowRunning = false;
                IsCacheLocked = false;
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
        var activeSavedAccount = _savedAccountCatalog.GetActiveAccount();
        if (activeSavedAccount is null)
            return;

        var liveAccount = TryGetLiveAccount(activeSavedAccount.AccountName);
        if (liveAccount is null)
        {
            AppendLog(
                $"Warning: Active live account '{activeSavedAccount.AccountName}' was not found — skipping save."
            );
            return;
        }

        var savedAccount = _savedAccountCatalog.GetById(activeSavedAccount.SavedAccountId);
        var diff = _accountSnapshotDiffService.BuildDiff(liveAccount, savedAccount);
        if (!diff.IsNewAccount && !diff.HasChanges)
        {
            AppendLog(
                $"No changes detected for account '{liveAccount.AccountName}' — skipping save."
            );
            return;
        }

        if (AutoSaveOnExit)
        {
            ArchivingTitle = $"Saving account '{liveAccount.AccountName}'...";
            var saveTask = _orchestrator.SaveAccountAsync(
                liveAccount,
                BuildSavePlanFromDiff(diff),
                VersioningEnabled
            );
            await RunTrackedArchiveAsync(saveTask);

            _uiDispatcher.Invoke(() =>
            {
                RefreshState();
                StatusText = $"Account '{liveAccount.AccountName}' auto-saved.";
            });
            return;
        }

        _uiDispatcher.Invoke(() =>
        {
            OpenSaveSelection(
                activeSavedAccount.AccountName,
                $"Save Account — {activeSavedAccount.AccountName}"
            );
            StatusText = $"Review changes for account '{activeSavedAccount.AccountName}'.";
        });

        AppendLog($"Review changes for account '{activeSavedAccount.AccountName}'.");
    }

    [RelayCommand]
    private void ToggleVersionHistory()
    {
        if (IsVersionHistoryVisible)
        {
            IsVersionHistoryVisible = false;
            return;
        }

        var savedAccountId = CurrentSavedAccountId;
        if (string.IsNullOrEmpty(savedAccountId))
        {
            AppendLog("No active saved account — nothing to show.");
            return;
        }

        Versions.Clear();
        foreach (var v in _versionService.GetVersions(savedAccountId))
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
            ArchivingTitle = $"Restoring version {version.DisplayName}...";
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
    private async Task ConfirmSaveSelectionAsync()
    {
        if (_pendingLiveAccount is null)
            return;

        if (!CanConfirmSaveSelection)
        {
            AppendLog($"No account changes selected for '{_pendingLiveAccount.AccountName}'.");
            return;
        }

        IsBusy = true;
        try
        {
            ArchivingTitle = $"Saving account '{_pendingLiveAccount.AccountName}'...";
            var saveTask = _orchestrator.SaveAccountAsync(
                _pendingLiveAccount,
                BuildCurrentSavePlan(),
                VersioningEnabled
            );
            await RunTrackedArchiveAsync(saveTask);
            var savedAccount = await saveTask;

            IsSaveSelectionVisible = false;
            RefreshState();
            StatusText =
                $"Account '{savedAccount?.AccountName ?? _pendingLiveAccount.AccountName}' saved.";
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
    private void CancelSaveSelection()
    {
        _saveSelectionLoadCts?.Cancel();
        IsSaveSelectionVisible = false;
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

    private void OnLogMessage(string message) => AppendLog(message);

    [RelayCommand]
    private void ShowAccountsMode() => ActiveMode = AppMode.Accounts;

    [RelayCommand]
    private void ShowTemplatesMode()
    {
        ActiveMode = AppMode.Templates;
        RefreshTemplates();
    }

    [RelayCommand]
    private void OpenCreateTemplate()
    {
        if (GuardWowRunning("Close the game before creating a template."))
            return;
        if (!RefreshLiveCharacters())
            return;

        NewTemplateName = string.Empty;
        SelectedDonorCharacter = null;
        IsTemplateDonorSelectionVisible = true;
    }

    [RelayCommand]
    private void CancelCreateTemplate() => IsTemplateDonorSelectionVisible = false;

    [RelayCommand]
    private void ConfirmCreateTemplate()
    {
        if (!CanConfirmCreateTemplate || SelectedDonorCharacter is null)
            return;
        if (GuardWowRunning("Close the game before creating a template."))
            return;

        IsBusy = true;
        try
        {
            var template = _templateCaptureService.CreateTemplate(
                SelectedDonorCharacter,
                NewTemplateName
            );
            IsTemplateDonorSelectionVisible = false;
            RefreshTemplates();
            StatusText = $"Template '{template.Name}' created.";
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            _dialogService.ShowWarning(ex.Message, "Create Template Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenApplyTemplate(string templateId)
    {
        var template = Templates.FirstOrDefault(candidate => candidate.Id == templateId);
        if (template is null)
            return;
        if (GuardWowRunning("Close the game before applying a template."))
            return;
        if (!RefreshLiveCharacters())
            return;

        TemplateToApply = template;
        SelectedTargetCharacter = null;
        IncludeAccountSettings = false;
        TemplateApplyTitle = $"Apply '{template.Name}'";
        IsTemplateApplyVisible = true;
    }

    [RelayCommand]
    private void CancelApplyTemplate()
    {
        IsTemplateApplyVisible = false;
        TemplateToApply = null;
    }

    [RelayCommand]
    private void ConfirmApplyTemplate()
    {
        if (!CanConfirmApplyTemplate || TemplateToApply is null || SelectedTargetCharacter is null)
            return;
        if (GuardWowRunning("Close the game before applying a template."))
            return;

        if (
            IncludeAccountSettings
            && !_dialogService.Confirm(
                "Applying account-level settings overwrites the SavedVariables shared by every character on the target account. Continue?",
                "Overwrite Account Settings?"
            )
        )
        {
            AppendLog("Template apply cancelled by user.");
            return;
        }

        var template = TemplateToApply;
        var target = SelectedTargetCharacter;
        var options = new TemplateApplyOptions
        {
            IncludeAccountSettings = IncludeAccountSettings,
            CreateVersionBeforeApply = VersioningEnabled,
        };

        IsBusy = true;
        try
        {
            _templateApplyService.ApplyTemplate(template, target, options);
            IsTemplateApplyVisible = false;
            TemplateToApply = null;
            StatusText =
                $"Template '{template.Name}' applied to {target.CharacterName}. Type /reload in WoW.";
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            _dialogService.ShowWarning(ex.Message, "Apply Template Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenRenameTemplate(string templateId)
    {
        var template = Templates.FirstOrDefault(candidate => candidate.Id == templateId);
        if (template is null)
            return;

        _templateToRenameId = template.Id;
        RenameTemplateName = template.Name;
        IsTemplateRenameVisible = true;
    }

    [RelayCommand]
    private void CancelRenameTemplate()
    {
        IsTemplateRenameVisible = false;
        _templateToRenameId = string.Empty;
    }

    [RelayCommand]
    private void ConfirmRenameTemplate()
    {
        if (!CanConfirmRenameTemplate || string.IsNullOrEmpty(_templateToRenameId))
            return;

        try
        {
            _templateCatalog.Rename(_templateToRenameId, RenameTemplateName);
            IsTemplateRenameVisible = false;
            _templateToRenameId = string.Empty;
            RefreshTemplates();
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            _dialogService.ShowWarning(ex.Message, "Rename Template Error");
        }
    }

    [RelayCommand]
    private void DeleteTemplate(string templateId)
    {
        var template = Templates.FirstOrDefault(candidate => candidate.Id == templateId);
        if (template is null)
            return;

        if (
            !_dialogService.Confirm(
                $"Delete template '{template.Name}' and all its versions? This cannot be undone.",
                "Delete Template"
            )
        )
            return;

        try
        {
            _templateCatalog.Delete(template.Id);
            RefreshTemplates();
            if (IsTemplateVersionHistoryVisible && _versionHistoryTemplateId == template.Id)
                IsTemplateVersionHistoryVisible = false;
            StatusText = $"Template '{template.Name}' deleted.";
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            _dialogService.ShowWarning(ex.Message, "Delete Template Error");
        }
    }

    [RelayCommand]
    private void ToggleTemplateVersionHistory(string templateId)
    {
        if (IsTemplateVersionHistoryVisible && _versionHistoryTemplateId == templateId)
        {
            IsTemplateVersionHistoryVisible = false;
            return;
        }

        _versionHistoryTemplateId = templateId;
        TemplateVersions.Clear();
        foreach (var version in _templateVersionService.GetVersions(templateId))
            TemplateVersions.Add(version);

        IsTemplateVersionHistoryVisible = true;
    }

    [RelayCommand]
    private void CloseTemplateVersionHistory()
    {
        IsTemplateVersionHistoryVisible = false;
        _versionHistoryTemplateId = string.Empty;
    }

    [RelayCommand]
    private async Task RestoreTemplateVersionAsync(string versionId)
    {
        var version = TemplateVersions.FirstOrDefault(candidate =>
            candidate.VersionId == versionId
        );
        if (version is null)
            return;

        try
        {
            ArchivingTitle = $"Restoring template version {version.DisplayName}...";
            await RunTrackedArchiveAsync(_templateVersionService.RestoreVersionAsync(version));
            IsTemplateVersionHistoryVisible = false;
            RefreshTemplates();
            StatusText = $"Restored template version {version.DisplayName}.";
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
        }
    }

    [RelayCommand]
    private void DeleteTemplateVersion(string versionId)
    {
        var version = TemplateVersions.FirstOrDefault(candidate =>
            candidate.VersionId == versionId
        );
        if (version is null)
            return;

        try
        {
            _templateVersionService.DeleteVersion(version);
            TemplateVersions.Remove(version);
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
        }
    }

    private void RefreshTemplates()
    {
        Templates.Clear();
        try
        {
            foreach (var template in _templateCatalog.DiscoverTemplates())
                Templates.Add(template);
        }
        catch (Exception ex)
        {
            AppendLog($"Warning: Failed to load templates — {ex.Message}");
        }
    }

    private bool RefreshLiveCharacters()
    {
        if (!EnsureInstallation() || _installation is null)
        {
            AppendLog("No live WoW characters were found in WTF.");
            return false;
        }

        LiveCharacters.Clear();
        foreach (
            var character in _installation
                .Accounts.SelectMany(account => account.Realms)
                .SelectMany(realm => realm.Characters)
                .OrderBy(character => character.AccountName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(character => character.RealmName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(character => character.CharacterName, StringComparer.OrdinalIgnoreCase)
        )
        {
            LiveCharacters.Add(character);
        }

        if (LiveCharacters.Count == 0)
        {
            AppendLog("No live WoW characters were found in WTF.");
            return false;
        }

        return true;
    }

    partial void OnActiveModeChanged(AppMode value)
    {
        OnPropertyChanged(nameof(IsAccountsMode));
        OnPropertyChanged(nameof(IsTemplatesMode));
    }

    private void AppendLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var line = $"[{timestamp}] {message}\n";
        _uiDispatcher.Invoke(() => LogText += line);
    }

    partial void OnSelectedLiveAccountNameChanged(string? value)
    {
        _ = LoadSaveSelectionForSelectedAccountAsync(value);
    }

    partial void OnSaveAccountSettingsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanConfirmSaveSelection));
    }

    private void UpdateLiveAccounts(WowInstallation installation)
    {
        LiveAccounts.Clear();
        foreach (var account in installation.Accounts)
            LiveAccounts.Add(account.AccountName);

        DetectedLiveAccountsSummary = installation.Accounts.Count switch
        {
            0 => "No live accounts detected.",
            1 => $"Live account detected: {installation.Accounts[0].AccountName}",
            _ =>
                $"Live accounts detected: {string.Join(", ", installation.Accounts.Select(account => account.AccountName))}",
        };
    }

    private void OpenSaveSelection(string? preselectedAccountName = null, string? title = null)
    {
        if (!EnsureInstallation())
            return;

        if (_installation is null || _installation.Accounts.Count == 0)
        {
            AppendLog("No live WoW accounts were found in WTF.");
            return;
        }

        SaveSelectionTitle = title ?? "Save Account";
        IsSaveSelectionVisible = true;

        var targetAccountName =
            !string.IsNullOrWhiteSpace(preselectedAccountName) ? preselectedAccountName
            : _installation.Accounts.Count == 1 ? _installation.Accounts[0].AccountName
            : null;

        SelectedLiveAccountName = targetAccountName;
        if (targetAccountName is null)
            ResetPendingSaveSelection("Choose a live account to save.");
    }

    private bool EnsureInstallation()
    {
        if (string.IsNullOrWhiteSpace(GamePath))
        {
            AppendLog("ERROR: Game path is not set.");
            return false;
        }

        try
        {
            _installation = _wtfInspector.Inspect(GamePath);
            UpdateLiveAccounts(_installation);
            return true;
        }
        catch (Exception ex)
        {
            _installation = null;
            LiveAccounts.Clear();
            DetectedLiveAccountsSummary = "No live accounts detected.";
            AppendLog($"Warning: WTF inspection failed — {ex.Message}");
            return false;
        }
    }

    private WowAccount? TryGetLiveAccount(string accountName)
    {
        if (!EnsureInstallation() || _installation is null)
            return null;

        return _installation.Accounts.FirstOrDefault(account =>
            account.AccountName.Equals(accountName, StringComparison.OrdinalIgnoreCase)
        );
    }

    private async Task LoadSaveSelectionForSelectedAccountAsync(string? selectedLiveAccountName)
    {
        _saveSelectionLoadCts?.Cancel();
        _saveSelectionLoadCts?.Dispose();
        var loadCts = new CancellationTokenSource();
        _saveSelectionLoadCts = loadCts;

        var ct = loadCts.Token;

        SaveRealms.Clear();
        _pendingLiveAccount = null;
        IsNewSaveAccount = false;
        SaveAccountSettingsSelected = false;
        HasPendingCharacterNodes = false;

        if (_installation is null || string.IsNullOrWhiteSpace(selectedLiveAccountName))
        {
            ResetPendingSaveSelection("Choose a live account to save.");
            return;
        }

        var liveAccount = _installation.Accounts.FirstOrDefault(account =>
            account.AccountName.Equals(selectedLiveAccountName, StringComparison.OrdinalIgnoreCase)
        );
        if (liveAccount is null)
        {
            ResetPendingSaveSelection("Choose a live account to save.");
            return;
        }

        IsLoadingSaveSelection = true;
        SaveSelectionMessage = $"Loading changes for '{liveAccount.AccountName}'...";

        try
        {
            var diff = await BuildDiffAsync(liveAccount, ct);
            if (ct.IsCancellationRequested)
                return;

            _pendingLiveAccount = liveAccount;
            IsNewSaveAccount = diff.IsNewAccount;
            SaveAccountSettingsSelected =
                diff.IsNewAccount
                || diff.AccountSettingsStatus != AccountSnapshotDiffStatus.Unchanged;

            if (diff.IsNewAccount)
            {
                SaveSelectionMessage =
                    $"Account '{liveAccount.AccountName}' has not been saved yet. Confirm to save the entire account snapshot.";
                return;
            }

            foreach (var realm in OrderRealmsForSelection(diff.Realms))
            {
                var realmViewModel = new RealmSaveSelectionViewModel(realm.RealmName, realm.Status);

                foreach (var character in OrderCharactersForSelection(realm.Characters))
                {
                    realmViewModel.Characters.Add(
                        new CharacterSaveSelectionViewModel(
                            character.RealmName,
                            character.CharacterName,
                            character.Status,
                            isSelected: character.Status != AccountSnapshotDiffStatus.Unchanged,
                            selectionChanged: OnPendingSaveSelectionChanged
                        )
                    );
                }

                SaveRealms.Add(realmViewModel);
            }

            HasPendingCharacterNodes = SaveRealms.Count > 0;
            SaveSelectionMessage = diff.HasChanges
                ? $"Review changed account settings and characters for '{liveAccount.AccountName}'."
                : $"No changes detected for '{liveAccount.AccountName}' since the last save.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
            {
                ResetPendingSaveSelection("Failed to load account changes.");
                AppendLog($"Warning: Failed to load account changes — {ex.Message}");
            }
        }
        finally
        {
            if (ReferenceEquals(_saveSelectionLoadCts, loadCts))
            {
                IsLoadingSaveSelection = false;
                _saveSelectionLoadCts.Dispose();
                _saveSelectionLoadCts = null;
            }
        }
    }

    private void ResetPendingSaveSelection(string message)
    {
        SaveSelectionMessage = message;
        OnPropertyChanged(nameof(CanConfirmSaveSelection));
    }

    private void OnPendingSaveSelectionChanged()
    {
        OnPropertyChanged(nameof(CanConfirmSaveSelection));
    }

    private async Task<AccountSnapshotDiff> BuildDiffAsync(
        WowAccount liveAccount,
        CancellationToken ct
    )
    {
        return await Task.Run(
            () =>
            {
                ct.ThrowIfCancellationRequested();
                var savedAccount = _savedAccountCatalog.FindByAccountName(liveAccount.AccountName);
                return _accountSnapshotDiffService.BuildDiff(liveAccount, savedAccount);
            },
            ct
        );
    }

    private static IOrderedEnumerable<RealmSnapshotDiff> OrderRealmsForSelection(
        IEnumerable<RealmSnapshotDiff> realms
    )
    {
        return realms
            .OrderBy(realm => GetSelectionSortGroup(realm.Status))
            .ThenBy(realm => realm.RealmName, StringComparer.OrdinalIgnoreCase);
    }

    private static IOrderedEnumerable<CharacterSnapshotDiff> OrderCharactersForSelection(
        IEnumerable<CharacterSnapshotDiff> characters
    )
    {
        return characters
            .OrderBy(character => GetSelectionSortGroup(character.Status))
            .ThenBy(character => character.CharacterName, StringComparer.OrdinalIgnoreCase);
    }

    private static int GetSelectionSortGroup(AccountSnapshotDiffStatus status)
    {
        return status == AccountSnapshotDiffStatus.Unchanged ? 1 : 0;
    }

    private AccountSavePlan BuildCurrentSavePlan()
    {
        if (_pendingLiveAccount is null)
            throw new InvalidOperationException("No live account is selected for saving.");

        if (IsNewSaveAccount)
        {
            return new AccountSavePlan
            {
                AccountName = _pendingLiveAccount.AccountName,
                SaveAccountSettings = true,
            };
        }

        var selectedCharacters = SaveRealms
            .SelectMany(realm => realm.Characters)
            .Where(character => character.IsSelected)
            .Select(character => new CharacterSaveSelection
            {
                RealmName = character.RealmName,
                CharacterName = character.CharacterName,
            })
            .ToList();

        return new AccountSavePlan
        {
            AccountName = _pendingLiveAccount.AccountName,
            SaveAccountSettings = SaveAccountSettingsSelected,
            SelectedCharacters = selectedCharacters,
        };
    }

    private static AccountSavePlan BuildSavePlanFromDiff(AccountSnapshotDiff diff)
    {
        if (diff.IsNewAccount)
        {
            return new AccountSavePlan
            {
                AccountName = diff.AccountName,
                SaveAccountSettings = true,
            };
        }

        return new AccountSavePlan
        {
            AccountName = diff.AccountName,
            SaveAccountSettings = diff.AccountSettingsStatus != AccountSnapshotDiffStatus.Unchanged,
            SelectedCharacters = diff
                .Realms.SelectMany(realm => realm.Characters)
                .Where(character => character.Status != AccountSnapshotDiffStatus.Unchanged)
                .Select(character => new CharacterSaveSelection
                {
                    RealmName = character.RealmName,
                    CharacterName = character.CharacterName,
                })
                .ToList(),
        };
    }
}
