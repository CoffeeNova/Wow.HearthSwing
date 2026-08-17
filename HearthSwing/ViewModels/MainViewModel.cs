using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HearthSwing.Models;
using HearthSwing.Models.Templates;
using HearthSwing.Models.WoW;
using HearthSwing.Services;

namespace HearthSwing.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const string AccountOverwriteMessage =
        "Applying an account template overwrites the SavedVariables shared by every character on the target account. Continue?";
    private const string AccountOverwriteTitle = "Overwrite Account Settings?";
    private const string SharedOverwriteMessage =
        "Applying a character template also overwrites the shared account settings for every character on the target account. Continue?";
    private const string SharedOverwriteTitle = "Overwrite Shared Account Settings?";
    private const string DeleteTemplateTitle = "Delete Template";

    private readonly ISettingsService _settingsService;
    private readonly ISwitchingOrchestrator _orchestrator;
    private readonly IProcessMonitor _processMonitor;
    private readonly IUpdateService _updateService;
    private readonly IDialogService _dialogService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IUiLogSink _logSink;
    private readonly IWtfInspector _wtfInspector;
    private readonly IChangeHistoryService _changeHistoryService;
    private readonly ILegacyDataCleanupService _legacyDataCleanupService;
    private readonly ITemplateCatalog _templateCatalog;
    private readonly ITemplateCaptureService _templateCaptureService;
    private readonly ITemplateApplyService _templateApplyService;
    private readonly ITemplateRestoreOrchestrator _templateRestoreOrchestrator;
    private WowInstallation? _installation;
    private string _templateToRenameId = string.Empty;
    private string _templateHistoryTemplateId = string.Empty;
    private string _templateBeingUpdatedId = string.Empty;
    private CancellationTokenSource? _unlockCts;
    private CancellationTokenSource? _monitorCts;
    private CancellationTokenSource? _templateRestoreCts;
    private CancellationTokenSource? _toastCts;
    private readonly object _archiveLock = new();
    private int _activeArchiveCount;
    private TaskCompletionSource? _archiveDoneTcs;

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
    private string _toastMessage = string.Empty;

    [ObservableProperty]
    private bool _isToastVisible;

    [ObservableProperty]
    private int _unlockCountdown;

    [ObservableProperty]
    private bool _isCheckingForUpdate;

    [ObservableProperty]
    private int _maxHistoryEntriesPerTarget = 20;

    [ObservableProperty]
    private bool _isArchiving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ArchivingDetailText))]
    private bool _isCloseBlockedByArchiving;

    [ObservableProperty]
    private string _archivingTitle = "Working...";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTemplatesMode))]
    [NotifyPropertyChangedFor(nameof(IsHistoryMode))]
    private AppMode _activeMode = AppMode.Templates;

    [ObservableProperty]
    private bool _isTemplateCreateVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreatingNewTemplate))]
    private bool _isUpdatingTemplate;

    [ObservableProperty]
    private string _templateCreateTitle = "Create Template";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreatingAccountTemplate))]
    [NotifyPropertyChangedFor(nameof(IsCreatingCharacterTemplate))]
    [NotifyPropertyChangedFor(nameof(CanConfirmCreateTemplate))]
    private TemplateKind _createTemplateKind = TemplateKind.Character;

    [ObservableProperty]
    private bool _isTemplateApplyVisible;

    [ObservableProperty]
    private bool _isTemplateHistoryVisible;

    [ObservableProperty]
    private string _templateHistoryTitle = "Template History";

    [ObservableProperty]
    private string _templateHistorySubtitle = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmCreateTemplate))]
    private WowCharacter? _selectedDonorCharacter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmCreateTemplate))]
    private WowAccount? _selectedDonorAccount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmCreateTemplate))]
    private string _newTemplateName = string.Empty;

    [ObservableProperty]
    private string _donorSearchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmApplyTemplate))]
    private WowCharacter? _selectedTargetCharacter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmApplyTemplate))]
    private WowAccount? _selectedTargetAccount;

    [ObservableProperty]
    private string _targetSearchText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsApplyingAccountTemplate))]
    [NotifyPropertyChangedFor(nameof(IsApplyingCharacterTemplate))]
    [NotifyPropertyChangedFor(nameof(CanConfirmApplyTemplate))]
    private TemplateSummary? _templateToApply;

    [ObservableProperty]
    private string _templateApplyTitle = "Apply Template";

    [ObservableProperty]
    private bool _includeAccountScopedCharacterSettings = true;

    [ObservableProperty]
    private TemplateApplyScope _templateRestoreScope = TemplateApplyScope.Full;

    [ObservableProperty]
    private bool _isTemplateRenameVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmRenameTemplate))]
    private string _renameTemplateName = string.Empty;

    public ObservableCollection<TemplateSummary> Templates { get; } = [];

    public ObservableCollection<HistoryEntry> TemplateHistoryEntries { get; } = [];

    public ObservableCollection<HistoryTargetGroupViewModel> HistoryGroups { get; } = [];

    public ObservableCollection<WtfTreeNodeViewModel> DonorCharacterTree { get; } = [];

    public ObservableCollection<WowAccount> DonorAccounts { get; } = [];

    public ObservableCollection<WtfTreeNodeViewModel> TargetCharacterTree { get; } = [];

    public ObservableCollection<WowAccount> TargetAccounts { get; } = [];

    public bool IsTemplatesMode => ActiveMode == AppMode.Templates;

    public bool IsHistoryMode => ActiveMode == AppMode.History;

    public bool IsCreatingNewTemplate => !IsUpdatingTemplate;

    public bool IsCreatingAccountTemplate => CreateTemplateKind == TemplateKind.Account;

    public bool IsCreatingCharacterTemplate => CreateTemplateKind == TemplateKind.Character;

    public bool IsApplyingAccountTemplate => TemplateToApply?.Kind == TemplateKind.Account;

    public bool IsApplyingCharacterTemplate => TemplateToApply?.Kind == TemplateKind.Character;

    public bool CanConfirmCreateTemplate =>
        !string.IsNullOrWhiteSpace(NewTemplateName)
        && (
            IsCreatingAccountTemplate
                ? SelectedDonorAccount is not null
                : SelectedDonorCharacter is not null
        );

    public bool CanConfirmApplyTemplate =>
        TemplateToApply is not null
        && (
            IsApplyingAccountTemplate
                ? SelectedTargetAccount is not null
                : SelectedTargetCharacter is not null
        );

    public bool CanConfirmRenameTemplate => !string.IsNullOrWhiteSpace(RenameTemplateName);

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
        ISwitchingOrchestrator orchestrator,
        IProcessMonitor processMonitor,
        IUpdateService updateService,
        IDialogService dialogService,
        IUiDispatcher uiDispatcher,
        IUiLogSink logSink,
        IWtfInspector wtfInspector,
        IChangeHistoryService changeHistoryService,
        ILegacyDataCleanupService legacyDataCleanupService,
        ITemplateCatalog templateCatalog,
        ITemplateCaptureService templateCaptureService,
        ITemplateApplyService templateApplyService,
        ITemplateRestoreOrchestrator templateRestoreOrchestrator
    )
    {
        _settingsService = settingsService;
        _orchestrator = orchestrator;
        _processMonitor = processMonitor;
        _updateService = updateService;
        _dialogService = dialogService;
        _uiDispatcher = uiDispatcher;
        _logSink = logSink;
        _wtfInspector = wtfInspector;
        _changeHistoryService = changeHistoryService;
        _legacyDataCleanupService = legacyDataCleanupService;
        _templateCatalog = templateCatalog;
        _templateCaptureService = templateCaptureService;
        _templateApplyService = templateApplyService;
        _templateRestoreOrchestrator = templateRestoreOrchestrator;

        _logSink.MessageLogged += OnLogMessage;
        _changeHistoryService.Log += OnLogMessage;
        _templateRestoreOrchestrator.Log += OnLogMessage;

        GamePath = settingsService.Current.GamePath;
        ProfilesPath = settingsService.Current.ProfilesPath;
        UnlockDelay = settingsService.Current.UnlockDelaySeconds;
        MaxHistoryEntriesPerTarget = settingsService.Current.MaxHistoryEntriesPerTarget;

        RefreshState();
        RefreshTemplates();
        RefreshHistory();
    }

    private void RefreshState()
    {
        IsWowRunning = _processMonitor.IsWowRunning();
        IsCacheLocked = _orchestrator.IsCacheLocked;

        if (!string.IsNullOrWhiteSpace(GamePath))
        {
            try
            {
                var installation = _wtfInspector.Inspect(GamePath);
                _installation = installation;
            }
            catch (Exception ex)
            {
                _installation = null;
                AppendLog($"Warning: WTF inspection failed — {ex.Message}");
            }
        }
        else
        {
            _installation = null;
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
            AppendLog("Use History mode to restore previous WTF state.");
            StatusText = "Use History to restore.";
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
        _settingsService.Current.MaxHistoryEntriesPerTarget = MaxHistoryEntriesPerTarget;
        _settingsService.Save();
        IsSettingsVisible = false;
        AppendLog("Settings saved.");
        RefreshState();
        RefreshTemplates();
        RefreshHistory();
    }

    [RelayCommand]
    private void CleanupLegacyData()
    {
        var summary = _legacyDataCleanupService.Discover();
        if (!summary.HasItems)
        {
            StatusText = "No legacy data found.";
            ShowToast("No legacy data found.");
            return;
        }

        var message =
            $"Remove {summary.TotalCount} legacy storage item(s) from Profiles Path?\n\n"
            + $"Folders: {summary.Directories.Count}\nFiles: {summary.Files.Count}\n\n"
            + BuildLegacyCleanupPreview(summary)
            + "\n\n"
            + "This deletes old pre-history data and cannot be undone.";

        if (!_dialogService.Confirm(message, "Clean Up Legacy Data"))
            return;

        try
        {
            var removed = _legacyDataCleanupService.Cleanup();
            StatusText = $"Removed {removed.TotalCount} legacy item(s).";
            ShowToast("Legacy data cleaned up.");
            RefreshTemplates();
            RefreshHistory();
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            _dialogService.ShowWarning(ex.Message, "Legacy Cleanup Error");
        }
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
        }
        catch (OperationCanceledException) { }
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
    private void ShowHistoryMode()
    {
        ActiveMode = AppMode.History;
        RefreshHistory();
    }

    [RelayCommand]
    private void OpenRestoreFromTemplate()
    {
        IsTemplateApplyVisible = true;
        TemplateApplyTitle = "Apply Template";
        TemplateRestoreScope = TemplateApplyScope.Full;
        IncludeAccountScopedCharacterSettings = true;
        TemplateToApply = null;
        SelectedTargetCharacter = null;
        SelectedTargetAccount = null;
        TargetSearchText = string.Empty;
        BuildCharacterTree(TargetCharacterTree);
        BuildAccountList(TargetAccounts);
    }

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
        if (!EnsureLiveInstallation())
            return;

        _templateBeingUpdatedId = string.Empty;
        IsUpdatingTemplate = false;
        TemplateCreateTitle = "Create Template";
        NewTemplateName = string.Empty;
        SelectedDonorCharacter = null;
        SelectedDonorAccount = null;
        DonorSearchText = string.Empty;
        CreateTemplateKind = TemplateKind.Character;
        BuildCharacterTree(DonorCharacterTree);
        BuildAccountList(DonorAccounts);
        IsTemplateCreateVisible = true;
    }

    [RelayCommand]
    private void OpenUpdateTemplate(string templateId)
    {
        var template = Templates.FirstOrDefault(candidate => candidate.Id == templateId);
        if (template is null)
            return;
        if (GuardWowRunning("Close the game before updating a template."))
            return;
        if (!EnsureLiveInstallation())
            return;

        _templateBeingUpdatedId = template.Id;
        IsUpdatingTemplate = true;
        TemplateCreateTitle = $"Update '{template.Name}'";
        NewTemplateName = template.Name;
        DonorSearchText = string.Empty;
        CreateTemplateKind = template.Kind;

        if (template.Kind == TemplateKind.Account)
        {
            BuildAccountList(DonorAccounts);
            SelectedDonorCharacter = null;
            SelectedDonorAccount = FindLiveAccount(template.SourceAccountName);
        }
        else
        {
            BuildCharacterTree(DonorCharacterTree);
            SelectedDonorAccount = null;
            SelectedDonorCharacter = FindLiveCharacter(
                template.SourceAccountName,
                template.SourceRealmName,
                template.SourceCharacterName
            );
        }

        IsTemplateCreateVisible = true;
    }

    [RelayCommand]
    private void SetCreateKindCharacter() => CreateTemplateKind = TemplateKind.Character;

    [RelayCommand]
    private void SetCreateKindAccount() => CreateTemplateKind = TemplateKind.Account;

    [RelayCommand]
    private void CancelCreateTemplate()
    {
        IsTemplateCreateVisible = false;
        IsUpdatingTemplate = false;
        _templateBeingUpdatedId = string.Empty;
    }

    [RelayCommand]
    private void ConfirmCreateTemplate()
    {
        if (!CanConfirmCreateTemplate)
            return;
        if (GuardWowRunning("Close the game before creating a template."))
            return;

        IsBusy = true;
        try
        {
            var template =
                CreateTemplateKind == TemplateKind.Account
                    ? _templateCaptureService.CreateAccountTemplate(
                        SelectedDonorAccount!,
                        NewTemplateName
                    )
                    : _templateCaptureService.CreateCharacterTemplate(
                        SelectedDonorCharacter!,
                        NewTemplateName
                    );
            IsTemplateCreateVisible = false;
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
    private async Task ConfirmUpdateTemplateAsync()
    {
        if (!CanConfirmCreateTemplate || string.IsNullOrEmpty(_templateBeingUpdatedId))
            return;
        if (GuardWowRunning("Close the game before updating a template."))
            return;

        var template = Templates.FirstOrDefault(candidate =>
            candidate.Id == _templateBeingUpdatedId
        );
        if (template is null)
            return;

        IsBusy = true;
        try
        {
            ArchivingTitle = $"Saving template history for '{template.Name}'...";
            await RunTrackedArchiveAsync(
                _changeHistoryService.SnapshotAsync(
                    BuildTemplateHistoryTargetKey(template.Id),
                    HistoryTargetKind.Template,
                    template.RootPath,
                    $"Update template '{template.Name}'"
                )
            );

            if (template.Kind == TemplateKind.Account)
                _templateCaptureService.UpdateAccountTemplate(template, SelectedDonorAccount!);
            else
                _templateCaptureService.UpdateCharacterTemplate(template, SelectedDonorCharacter!);

            IsTemplateCreateVisible = false;
            IsUpdatingTemplate = false;
            _templateBeingUpdatedId = string.Empty;
            RefreshTemplates();
            StatusText = $"Template '{template.Name}' updated.";
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            _dialogService.ShowWarning(ex.Message, "Update Template Error");
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
        if (!EnsureLiveInstallation())
            return;

        TemplateToApply = template;
        TemplateRestoreScope = TemplateApplyScope.Full;
        IncludeAccountScopedCharacterSettings = true;
        SelectedTargetCharacter = null;
        SelectedTargetAccount = null;
        TargetSearchText = string.Empty;
        if (template.Kind == TemplateKind.Account)
        {
            BuildAccountList(TargetAccounts);
            UpdateTargetAccountSelection();
        }
        else
        {
            BuildCharacterTree(TargetCharacterTree);
            UpdateTargetCharacterSelection();
        }
        TemplateApplyTitle = $"Apply '{template.Name}'";
        IsTemplateApplyVisible = true;
    }

    [RelayCommand]
    private void CancelApplyTemplate()
    {
        _templateRestoreCts?.Cancel();
        IsTemplateApplyVisible = false;
        TemplateToApply = null;
    }

    [RelayCommand]
    private async Task ConfirmApplyTemplateAsync()
    {
        if (!CanConfirmApplyTemplate || TemplateToApply is null)
            return;

        var template = TemplateToApply;

        if (template.Kind == TemplateKind.Account)
        {
            if (!_dialogService.Confirm(AccountOverwriteMessage, AccountOverwriteTitle))
            {
                AppendLog("Template apply cancelled by user.");
                return;
            }
        }
        else if (
            IncludeAccountScopedCharacterSettings
            && !_dialogService.Confirm(SharedOverwriteMessage, SharedOverwriteTitle)
        )
        {
            AppendLog("Template apply cancelled by user.");
            return;
        }

        IsBusy = true;
        try
        {
            _templateRestoreCts?.Cancel();
            _templateRestoreCts?.Dispose();
            _templateRestoreCts = new CancellationTokenSource();
            var ct = _templateRestoreCts.Token;
            var wowRunning = _processMonitor.IsWowRunning();

            string appliedTo;
            if (template.Kind == TemplateKind.Account)
            {
                var target = SelectedTargetAccount!;
                await _templateRestoreOrchestrator.RestoreAccountTemplateAsync(
                    template,
                    target,
                    new TemplateRestoreOptions { Scope = TemplateRestoreScope },
                    ct
                );
                appliedTo = target.AccountName;
            }
            else
            {
                var target = SelectedTargetCharacter!;
                await _templateRestoreOrchestrator.RestoreCharacterTemplateAsync(
                    template,
                    target,
                    new TemplateRestoreOptions
                    {
                        Scope = TemplateRestoreScope,
                        IncludeAccountScoped = IncludeAccountScopedCharacterSettings,
                    },
                    ct
                );
                appliedTo = target.CharacterName;
            }

            IsTemplateApplyVisible = false;
            TemplateToApply = null;
            StatusText = wowRunning
                ? $"Template '{template.Name}' applied to {appliedTo}. Type /reload in WoW."
                : $"Template '{template.Name}' applied to {appliedTo}.";
        }
        catch (OperationCanceledException)
        {
            AppendLog("Template apply cancelled.");
            StatusText = "Template apply cancelled.";
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            _dialogService.ShowWarning(ex.Message, "Apply Template Error");
        }
        finally
        {
            _templateRestoreCts?.Dispose();
            _templateRestoreCts = null;
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
            StatusText = "Template renamed.";
            ShowToast("Template renamed.");
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
                $"Delete template '{template.Name}' and all its history? This cannot be undone.",
                DeleteTemplateTitle
            )
        )
            return;

        try
        {
            _templateCatalog.Delete(template.Id);
            RefreshTemplates();
            if (IsTemplateHistoryVisible && _templateHistoryTemplateId == template.Id)
                IsTemplateHistoryVisible = false;
            StatusText = $"Template '{template.Name}' deleted.";
            ShowToast("Template deleted.");
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            _dialogService.ShowWarning(ex.Message, "Delete Template Error");
        }
    }

    [RelayCommand]
    private void ToggleTemplateHistory(string templateId)
    {
        if (IsTemplateHistoryVisible && _templateHistoryTemplateId == templateId)
        {
            IsTemplateHistoryVisible = false;
            return;
        }

        var template = Templates.FirstOrDefault(candidate => candidate.Id == templateId);
        if (template is null)
            return;

        _templateHistoryTemplateId = templateId;
        TemplateHistoryTitle = $"Template History - {template.DisplayName}";
        TemplateHistorySubtitle = BuildTemplateHistorySubtitle(template);
        TemplateHistoryEntries.Clear();
        foreach (
            var version in _changeHistoryService.List(BuildTemplateHistoryTargetKey(templateId))
        )
            TemplateHistoryEntries.Add(version);

        IsTemplateHistoryVisible = true;
    }

    [RelayCommand]
    private void CloseTemplateHistory()
    {
        IsTemplateHistoryVisible = false;
        _templateHistoryTemplateId = string.Empty;
        TemplateHistoryTitle = "Template History";
        TemplateHistorySubtitle = string.Empty;
    }

    [RelayCommand]
    private async Task RestoreTemplateHistoryEntryAsync(HistoryEntry? version)
    {
        if (version is null)
            return;

        if (
            !_dialogService.Confirm(
                $"Restore template history entry '{version.DisplayName}'? The current template state will be snapshotted first.",
                "Restore Template History"
            )
        )
        {
            return;
        }

        try
        {
            ArchivingTitle = $"Restoring template history entry {version.DisplayName}...";
            await RunTrackedArchiveAsync(_changeHistoryService.RestoreAsync(version));
            IsTemplateHistoryVisible = false;
            RefreshTemplates();
            StatusText = $"Restored template history entry {version.DisplayName}.";
            ShowToast("Template history restored.");
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            _dialogService.ShowWarning(ex.Message, "Restore Template History Error");
        }
    }

    [RelayCommand]
    private async Task DeleteTemplateHistoryEntryAsync(HistoryEntry? version)
    {
        if (version is null)
            return;

        if (
            !_dialogService.Confirm(
                $"Delete template history entry '{version.DisplayName}'?",
                "Delete Template History Entry"
            )
        )
        {
            return;
        }

        try
        {
            await _changeHistoryService.DeleteAsync(version);
            TemplateHistoryEntries.Remove(version);
            ShowToast("Template history entry deleted.");
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            _dialogService.ShowWarning(ex.Message, "Delete Template History Error");
        }
    }

    private static string BuildTemplateHistoryTargetKey(string templateId)
    {
        return string.Join('/', "template", templateId);
    }

    private static string BuildTemplateHistorySubtitle(TemplateSummary template)
    {
        if (template.Kind == TemplateKind.Account)
            return template.SourceAccountName;

        if (
            !string.IsNullOrWhiteSpace(template.SourceCharacterName)
            && !string.IsNullOrWhiteSpace(template.SourceRealmName)
        )
        {
            return $"{template.SourceCharacterName} - {template.SourceRealmName}";
        }

        return template.SourceAccountName;
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

    private void RefreshHistory()
    {
        HistoryGroups.Clear();

        try
        {
            var groups = _changeHistoryService
                .ListAll()
                .Where(entry => entry.Kind != HistoryTargetKind.Template)
                .GroupBy(entry => entry.TargetKey)
                .OrderByDescending(group => group.Max(entry => entry.CreatedUtc));

            foreach (var group in groups)
            {
                var latest = group.OrderByDescending(entry => entry.CreatedUtc).First();
                var viewModel = new HistoryTargetGroupViewModel
                {
                    KindLabel =
                        latest.Kind == HistoryTargetKind.WtfCharacter ? "Character" : "Account",
                    Title = BuildHistoryGroupTitle(latest),
                    Subtitle = BuildHistoryGroupSubtitle(latest),
                };

                foreach (var entry in group.OrderByDescending(candidate => candidate.CreatedUtc))
                    viewModel.Entries.Add(entry);

                HistoryGroups.Add(viewModel);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Warning: Failed to load history — {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RestoreHistoryEntryAsync(HistoryEntry? entry)
    {
        if (entry is null)
            return;

        if (GuardWowRunning("Close the game before restoring history."))
            return;

        var targetLabel = BuildHistoryEntryTargetLabel(entry);
        if (
            !_dialogService.Confirm(
                $"Restore '{targetLabel}' from {entry.DisplayName}? The current state will be snapshotted first.",
                "Restore History"
            )
        )
        {
            return;
        }

        try
        {
            ArchivingTitle = $"Restoring {targetLabel}...";
            await RunTrackedArchiveAsync(_changeHistoryService.RestoreAsync(entry));
            RefreshHistory();
            StatusText = $"Restored {targetLabel}.";
            ShowToast("History restored.");
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            _dialogService.ShowWarning(ex.Message, "Restore History Error");
        }
    }

    [RelayCommand]
    private async Task DeleteHistoryEntryAsync(HistoryEntry? entry)
    {
        if (entry is null)
            return;

        var targetLabel = BuildHistoryEntryTargetLabel(entry);
        if (
            !_dialogService.Confirm(
                $"Delete history entry '{entry.DisplayName}' for '{targetLabel}'?",
                "Delete History Entry"
            )
        )
        {
            return;
        }

        try
        {
            await _changeHistoryService.DeleteAsync(entry);
            RefreshHistory();
            StatusText = "History entry deleted.";
            ShowToast("History entry deleted.");
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex.Message}");
            _dialogService.ShowWarning(ex.Message, "Delete History Error");
        }
    }

    private static string BuildHistoryGroupTitle(HistoryEntry entry)
    {
        return entry.Kind switch
        {
            HistoryTargetKind.WtfCharacter => string.IsNullOrWhiteSpace(entry.CharacterName)
                ? entry.TargetKey
            : string.IsNullOrWhiteSpace(entry.RealmName) ? entry.CharacterName
            : $"{entry.CharacterName} ({entry.RealmName})",
            HistoryTargetKind.WtfAccount => entry.AccountName ?? entry.TargetKey,
            _ => entry.TargetKey,
        };
    }

    private static string BuildHistoryGroupSubtitle(HistoryEntry entry)
    {
        return entry.Kind switch
        {
            HistoryTargetKind.WtfCharacter => string.IsNullOrWhiteSpace(entry.AccountName)
                ? "Character restore points"
                : $"Account: {entry.AccountName}",
            HistoryTargetKind.WtfAccount => "Shared account settings restore points",
            _ => entry.Description,
        };
    }

    private static string BuildHistoryEntryTargetLabel(HistoryEntry entry)
    {
        return entry.Kind switch
        {
            HistoryTargetKind.WtfCharacter => BuildHistoryGroupTitle(entry),
            HistoryTargetKind.WtfAccount => entry.AccountName ?? entry.TargetKey,
            _ => entry.TargetKey,
        };
    }

    private static string BuildLegacyCleanupPreview(LegacyDataCleanupSummary summary)
    {
        var lines = new List<string>();
        lines.AddRange(summary.Directories.Select(path => $"- {path}"));
        lines.AddRange(summary.Files.Select(path => $"- {path}"));

        if (lines.Count == 0)
            return "No paths selected.";

        return "Paths to delete:\n" + string.Join("\n", lines);
    }

    private bool EnsureLiveInstallation()
    {
        if (!EnsureInstallation() || _installation is null || _installation.Accounts.Count == 0)
        {
            AppendLog("No live WoW accounts were found in WTF.");
            return false;
        }

        return true;
    }

    private void BuildCharacterTree(ObservableCollection<WtfTreeNodeViewModel> destination)
    {
        destination.Clear();
        if (_installation is null)
            return;

        foreach (
            var account in _installation.Accounts.OrderBy(
                account => account.AccountName,
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            var accountNode = new WtfTreeNodeViewModel(account.AccountName, character: null);

            foreach (
                var realm in account.Realms.OrderBy(
                    realm => realm.RealmName,
                    StringComparer.OrdinalIgnoreCase
                )
            )
            {
                var realmNode = new WtfTreeNodeViewModel(realm.RealmName, character: null);

                foreach (
                    var character in realm.Characters.OrderBy(
                        character => character.CharacterName,
                        StringComparer.OrdinalIgnoreCase
                    )
                )
                {
                    realmNode.Children.Add(
                        new WtfTreeNodeViewModel(character.CharacterName, character)
                    );
                }

                if (realmNode.Children.Count > 0)
                    accountNode.Children.Add(realmNode);
            }

            if (accountNode.Children.Count > 0)
                destination.Add(accountNode);
        }
    }

    private void BuildAccountList(ObservableCollection<WowAccount> destination)
    {
        destination.Clear();
        if (_installation is null)
            return;

        foreach (
            var account in _installation.Accounts.OrderBy(
                account => account.AccountName,
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            destination.Add(account);
        }
    }

    private WowAccount? FindLiveAccount(string? accountName)
    {
        if (_installation is null || string.IsNullOrWhiteSpace(accountName))
            return null;

        return _installation.Accounts.FirstOrDefault(account =>
            account.AccountName.Equals(accountName, StringComparison.OrdinalIgnoreCase)
        );
    }

    private WowCharacter? FindLiveCharacter(
        string? accountName,
        string? realmName,
        string? characterName
    )
    {
        if (
            _installation is null
            || string.IsNullOrWhiteSpace(accountName)
            || string.IsNullOrWhiteSpace(realmName)
            || string.IsNullOrWhiteSpace(characterName)
        )
            return null;

        return _installation
            .Accounts.SelectMany(account => account.Realms)
            .SelectMany(realm => realm.Characters)
            .FirstOrDefault(character =>
                character.AccountName.Equals(accountName, StringComparison.OrdinalIgnoreCase)
                && character.RealmName.Equals(realmName, StringComparison.OrdinalIgnoreCase)
                && character.CharacterName.Equals(characterName, StringComparison.OrdinalIgnoreCase)
            );
    }

    partial void OnDonorSearchTextChanged(string value)
    {
        foreach (var node in DonorCharacterTree)
            node.ApplyFilter(value);

        FilterAccountList(DonorAccounts, value);
    }

    partial void OnTargetSearchTextChanged(string value)
    {
        foreach (var node in TargetCharacterTree)
            node.ApplyFilter(value);

        FilterAccountList(TargetAccounts, value);
        UpdateTargetAccountSelection();
        UpdateTargetCharacterSelection();
    }

    private void UpdateTargetAccountSelection()
    {
        if (!IsApplyingAccountTemplate)
            return;

        if (SelectedTargetAccount is not null && TargetAccounts.Contains(SelectedTargetAccount))
            return;

        SelectedTargetAccount = TargetAccounts.Count == 1 ? TargetAccounts[0] : null;
    }

    private void UpdateTargetCharacterSelection()
    {
        if (!IsApplyingCharacterTemplate)
            return;

        if (
            SelectedTargetCharacter is not null
            && IsVisibleTargetCharacter(SelectedTargetCharacter)
        )
            return;

        var visibleCharacters = TargetCharacterTree
            .SelectMany(GetVisibleCharacters)
            .Take(2)
            .ToList();

        SelectedTargetCharacter = visibleCharacters.Count == 1 ? visibleCharacters[0] : null;
    }

    private static IEnumerable<WowCharacter> GetVisibleCharacters(WtfTreeNodeViewModel node)
    {
        if (node.Character is not null)
        {
            if (node.IsVisible)
                yield return node.Character;
            yield break;
        }

        foreach (var child in node.Children)
        {
            foreach (var character in GetVisibleCharacters(child))
                yield return character;
        }
    }

    private bool IsVisibleTargetCharacter(WowCharacter character)
    {
        return TargetCharacterTree
            .SelectMany(GetVisibleCharacters)
            .Any(candidate => candidate.Equals(character));
    }

    private void FilterAccountList(ObservableCollection<WowAccount> destination, string? search)
    {
        if (_installation is null)
            return;

        destination.Clear();
        foreach (
            var account in _installation
                .Accounts.Where(account =>
                    string.IsNullOrWhiteSpace(search)
                    || account.AccountName.Contains(search, StringComparison.OrdinalIgnoreCase)
                )
                .OrderBy(account => account.AccountName, StringComparer.OrdinalIgnoreCase)
        )
        {
            destination.Add(account);
        }
    }

    private void AppendLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var line = $"[{timestamp}] {message}\n";
        _uiDispatcher.Invoke(() => LogText += line);
    }

    private void ShowToast(string message, int durationMs = 2200)
    {
        _toastCts?.Cancel();
        _toastCts?.Dispose();

        var cts = new CancellationTokenSource();
        _toastCts = cts;

        _uiDispatcher.Invoke(() =>
        {
            ToastMessage = message;
            IsToastVisible = true;
        });

        _ = HideToastLaterAsync(cts.Token, durationMs);
    }

    private async Task HideToastLaterAsync(CancellationToken ct, int durationMs)
    {
        try
        {
            await Task.Delay(durationMs, ct);
            _uiDispatcher.Invoke(() => IsToastVisible = false);
        }
        catch (OperationCanceledException)
        {
            // A newer toast replaced this one.
        }
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
            return true;
        }
        catch (Exception ex)
        {
            _installation = null;
            AppendLog($"Warning: WTF inspection failed — {ex.Message}");
            return false;
        }
    }
}
