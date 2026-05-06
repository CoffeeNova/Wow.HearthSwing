using System.Reflection;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Models;
using HearthSwing.Models.Accounts;
using HearthSwing.Models.WoW;
using HearthSwing.Services;
using HearthSwing.ViewModels;
using NSubstitute;
using Shouldly;

namespace HearthSwing.Tests.ViewModels;

[TestFixture]
public class MainViewModelTests
{
    private IFixture _fixture = null!;
    private ISettingsService _settingsService = null!;
    private ISavedAccountCatalog _savedAccountCatalog = null!;
    private IAccountSnapshotDiffService _accountSnapshotDiffService = null!;
    private ISwitchingOrchestrator _orchestrator = null!;
    private IProcessMonitor _processMonitor = null!;
    private IUpdateService _updateService = null!;
    private IProfileVersionService _versionService = null!;
    private IDialogService _dialogService = null!;
    private IUiDispatcher _uiDispatcher = null!;
    private IUiLogSink _logSink = null!;
    private IWtfInspector _wtfInspector = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _settingsService = _fixture.Freeze<ISettingsService>();
        _savedAccountCatalog = _fixture.Freeze<ISavedAccountCatalog>();
        _accountSnapshotDiffService = _fixture.Freeze<IAccountSnapshotDiffService>();
        _orchestrator = _fixture.Freeze<ISwitchingOrchestrator>();
        _processMonitor = _fixture.Freeze<IProcessMonitor>();
        _updateService = _fixture.Freeze<IUpdateService>();
        _versionService = _fixture.Freeze<IProfileVersionService>();
        _dialogService = _fixture.Freeze<IDialogService>();
        _uiDispatcher = _fixture.Freeze<IUiDispatcher>();
        _logSink = _fixture.Freeze<IUiLogSink>();
        _wtfInspector = _fixture.Freeze<IWtfInspector>();

        _settingsService.Current.Returns(
            new AppSettings { GamePath = @"C:\Game", ProfilesPath = @"C:\Profiles" }
        );
        _savedAccountCatalog.DiscoverAccounts().Returns([]);
        _savedAccountCatalog.GetActiveAccount().Returns((ActiveAccountState?)null);
        _wtfInspector.Inspect(@"C:\Game").Returns(
            new WowInstallation { GamePath = @"C:\Game", WtfPath = @"C:\Game\WTF", Accounts = [] }
        );
        _uiDispatcher.When(x => x.Invoke(Arg.Any<Action>())).Do(ci => ci.Arg<Action>().Invoke());
    }

    private MainViewModel CreateSut() =>
        new(
            _settingsService,
            _savedAccountCatalog,
            _accountSnapshotDiffService,
            _orchestrator,
            _processMonitor,
            _updateService,
            _versionService,
            _dialogService,
            _uiDispatcher,
            _logSink,
            _wtfInspector
        );

    private static void InvokePrivate(MainViewModel sut, string methodName, params object[] args)
    {
        var method =
            typeof(MainViewModel).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Could not find method '{methodName}'.");
        method.Invoke(sut, args);
    }

    private static async Task InvokePrivateAsync(
        MainViewModel sut,
        string methodName,
        params object[] args
    )
    {
        var method =
            typeof(MainViewModel).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Could not find method '{methodName}'.");

        var task =
            method.Invoke(sut, args) as Task
            ?? throw new InvalidOperationException($"Method '{methodName}' did not return a Task.");
        await task;
    }

    [Test]
    public void Constructor_InitializesFromSettingsAndRefreshesState()
    {
        // Arrange
        var settings = new AppSettings
        {
            GamePath = @"C:\Game",
            ProfilesPath = @"C:\Profiles",
            UnlockDelaySeconds = 90,
            VersioningEnabled = false,
            MaxVersionsPerProfile = 3,
            SaveOnExitEnabled = false,
            AutoSaveOnExit = true,
        };
        _settingsService.Current.Returns(settings);

        var savedAccount = new SavedAccountSummary
        {
            Id = "donky-id",
            AccountName = "donky",
            RootPath = @"C:\Profiles\donky-id",
            SnapshotPath = @"C:\Profiles\donky-id\Account\donky",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            IsActive = true,
        };
        _savedAccountCatalog.GetActiveAccount()
            .Returns(new ActiveAccountState { SavedAccountId = "donky-id", AccountName = "donky" });
        _savedAccountCatalog.GetById("donky-id").Returns(savedAccount);
        _savedAccountCatalog.DiscoverAccounts().Returns([savedAccount]);
        _processMonitor.IsWowRunning().Returns(false);
        _orchestrator.IsCacheLocked.Returns(false);

        // Act
        var sut = CreateSut();

        // Assert
        sut.GamePath.ShouldBe(@"C:\Game");
        sut.ProfilesPath.ShouldBe(@"C:\Profiles");
        sut.UnlockDelay.ShouldBe(90);
        sut.VersioningEnabled.ShouldBeFalse();
        sut.MaxVersionsPerProfile.ShouldBe(3);
        sut.SaveOnExitEnabled.ShouldBeFalse();
        sut.AutoSaveOnExit.ShouldBeTrue();
        sut.CurrentSavedAccountId.ShouldBe("donky-id");
        sut.CurrentAccountName.ShouldBe("donky");
        sut.SavedAccounts.ShouldHaveSingleItem();
    }

    [Test]
    public void SwitchSavedAccount_WhenCalled_DelegatesToOrchestrator()
    {
        // Arrange
        var target = new SavedAccountSummary
        {
            Id = "alpha",
            AccountName = "Alpha",
            RootPath = @"C:\Profiles\alpha",
            SnapshotPath = @"C:\Profiles\alpha\Account\Alpha",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        _processMonitor.IsWowRunning().Returns(false);
        _savedAccountCatalog.DiscoverAccounts().Returns([target]);

        var sut = CreateSut();

        // Act
        InvokePrivate(sut, "SwitchSavedAccount", "alpha");

        // Assert
        _orchestrator.Received().SwitchTo(Arg.Is<SavedAccountSummary>(p => p.Id == "alpha"));
    }

    [Test]
    public async Task LaunchWowAsync_LocksViaOrchestratorAndLaunchesWow()
    {
        // Arrange
        _settingsService.Current.Returns(
            new AppSettings
            {
                GamePath = @"C:\Game",
                ProfilesPath = @"C:\Profiles",
                UnlockDelaySeconds = 0,
            }
        );
        _orchestrator.LockForLaunch().Returns(2);
        _orchestrator
            .WaitForWowExitAndCleanupAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled(new CancellationToken(canceled: true)));

        var sut = CreateSut();

        // Act
        await InvokePrivateAsync(sut, "LaunchWowAsync");

        // Assert
        _orchestrator.Received().LockForLaunch();
        _processMonitor.Received().LaunchWow(@"C:\Game");
    }

    [Test]
    public async Task LaunchWowAsync_WhenLaunchFails_UnlocksCacheViaOrchestrator()
    {
        // Arrange
        _orchestrator.LockForLaunch().Returns(2);
        _processMonitor.When(monitor => monitor.LaunchWow(@"C:\Game"))
            .Do(_ => throw new InvalidOperationException("launch failed"));

        var sut = CreateSut();

        // Act
        await InvokePrivateAsync(sut, "LaunchWowAsync");

        // Assert
        _orchestrator.Received().UnlockCache();
        sut.StatusText.ShouldBe("Launch failed!");
    }

    [Test]
    public async Task MonitorWowAsync_WhenProcessExitsWithAutoSaveEnabled_SavesActiveAccount()
    {
        // Arrange
        var activeSavedAccount = new SavedAccountSummary
        {
            Id = "alpha",
            AccountName = "Alpha",
            RootPath = @"C:\Profiles\alpha",
            SnapshotPath = @"C:\Profiles\alpha\Account\Alpha",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            IsActive = true,
        };
        var liveAccount = new WowAccount
        {
            AccountName = "Alpha",
            FolderPath = @"C:\Game\WTF\Account\Alpha",
            Realms = [],
        };
        var diff = new AccountSnapshotDiff
        {
            AccountName = "Alpha",
            AccountSettingsStatus = AccountSnapshotDiffStatus.Modified,
            Realms = [],
        };

        _settingsService.Current.Returns(
            new AppSettings
            {
                GamePath = @"C:\Game",
                ProfilesPath = @"C:\Profiles",
                SaveOnExitEnabled = true,
                AutoSaveOnExit = true,
                VersioningEnabled = false,
            }
        );
        _savedAccountCatalog.GetActiveAccount()
            .Returns(new ActiveAccountState { SavedAccountId = "alpha", AccountName = "Alpha" });
        _savedAccountCatalog.GetById("alpha").Returns(activeSavedAccount);
        _savedAccountCatalog.DiscoverAccounts().Returns([activeSavedAccount]);
        _wtfInspector.Inspect(@"C:\Game").Returns(
            new WowInstallation
            {
                GamePath = @"C:\Game",
                WtfPath = @"C:\Game\WTF",
                Accounts = [liveAccount],
            }
        );
        _accountSnapshotDiffService.BuildDiff(liveAccount, activeSavedAccount).Returns(diff);
        _orchestrator
            .WaitForWowExitAndCleanupAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sut = CreateSut();

        // Act
        await InvokePrivateAsync(sut, "MonitorWowAsync", CancellationToken.None);

        // Assert
        await _orchestrator.Received().SaveAccountAsync(
            Arg.Is<WowAccount>(account => account.AccountName == "Alpha"),
            Arg.Is<AccountSavePlan>(plan => plan.AccountName == "Alpha" && plan.SaveAccountSettings),
            false,
            Arg.Any<CancellationToken>()
        );
        sut.IsWowRunning.ShouldBeFalse();
    }

    [Test]
    public async Task SaveAccountAsync_WhenMultipleLiveAccountsPresent_OpensSelectionOverlayForAccountChoice()
    {
        // Arrange
        _wtfInspector.Inspect(@"C:\Game").Returns(
            new WowInstallation
            {
                GamePath = @"C:\Game",
                WtfPath = @"C:\Game\WTF",
                Accounts =
                [
                    BuildLiveAccount("Alpha"),
                    BuildLiveAccount("Bravo"),
                ],
            }
        );

        var sut = CreateSut();

        // Act
        await InvokePrivateAsync(sut, "SaveAccountAsync");

        // Assert
        sut.IsSaveSelectionVisible.ShouldBeTrue();
        sut.LiveAccounts.Select(account => account).ShouldBe(["Alpha", "Bravo"]);
        sut.SelectedLiveAccountName.ShouldBeNull();
        sut.SaveSelectionMessage.ShouldBe("Choose a live account to save.");
    }

    [Test]
    public async Task ConfirmSaveSelectionAsync_WhenChangedCharacterSelected_SavesSelectivePlan()
    {
        // Arrange
        var savedAccount = BuildSavedAccount("alpha", "Alpha");
        var liveAccount = BuildLiveAccount("Alpha", "Firemaw", "Hero");
        var diff = BuildDiff(
            "Alpha",
            AccountSnapshotDiffStatus.Unchanged,
            new CharacterSnapshotDiff
            {
                RealmName = "Firemaw",
                CharacterName = "Hero",
                FolderPath = @"C:\Game\WTF\Account\Alpha\Firemaw\Hero",
                Status = AccountSnapshotDiffStatus.Modified,
            }
        );

        _savedAccountCatalog.FindByAccountName("Alpha").Returns(savedAccount);
        _wtfInspector.Inspect(@"C:\Game").Returns(
            new WowInstallation
            {
                GamePath = @"C:\Game",
                WtfPath = @"C:\Game\WTF",
                Accounts = [liveAccount],
            }
        );
        _accountSnapshotDiffService.BuildDiff(liveAccount, savedAccount).Returns(diff);
        _orchestrator
            .SaveAccountAsync(
                Arg.Any<WowAccount>(),
                Arg.Any<AccountSavePlan>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult<SavedAccountSummary?>(savedAccount));

        var sut = CreateSut();
        await InvokePrivateAsync(sut, "SaveAccountAsync");

        // Act
        await InvokePrivateAsync(sut, "ConfirmSaveSelectionAsync");

        // Assert
        await _orchestrator.Received().SaveAccountAsync(
            Arg.Is<WowAccount>(account => account.AccountName == "Alpha"),
            Arg.Is<AccountSavePlan>(plan =>
                plan.AccountName == "Alpha"
                && !plan.SaveAccountSettings
                && plan.SelectedCharacters.Count == 1
                && plan.SelectedCharacters[0].RealmName == "Firemaw"
                && plan.SelectedCharacters[0].CharacterName == "Hero"
            ),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>()
        );
        sut.IsSaveSelectionVisible.ShouldBeFalse();
        sut.StatusText.ShouldBe("Account 'Alpha' saved.");
    }

    [Test]
    public async Task MonitorWowAsync_WhenAutoSaveDisabled_OpensManualSaveSelectionInsteadOfSaving()
    {
        // Arrange
        var activeSavedAccount = BuildSavedAccount("alpha", "Alpha") with { IsActive = true };
        var liveAccount = BuildLiveAccount("Alpha", "Firemaw", "Hero");
        var diff = BuildDiff(
            "Alpha",
            AccountSnapshotDiffStatus.Modified,
            new CharacterSnapshotDiff
            {
                RealmName = "Firemaw",
                CharacterName = "Hero",
                FolderPath = @"C:\Game\WTF\Account\Alpha\Firemaw\Hero",
                Status = AccountSnapshotDiffStatus.Modified,
            }
        );

        _settingsService.Current.Returns(
            new AppSettings
            {
                GamePath = @"C:\Game",
                ProfilesPath = @"C:\Profiles",
                SaveOnExitEnabled = true,
                AutoSaveOnExit = false,
                VersioningEnabled = true,
            }
        );
        _savedAccountCatalog.GetActiveAccount()
            .Returns(new ActiveAccountState { SavedAccountId = "alpha", AccountName = "Alpha" });
        _savedAccountCatalog.GetById("alpha").Returns(activeSavedAccount);
        _savedAccountCatalog.DiscoverAccounts().Returns([activeSavedAccount]);
        _savedAccountCatalog.FindByAccountName("Alpha").Returns(activeSavedAccount);
        _wtfInspector.Inspect(@"C:\Game").Returns(
            new WowInstallation
            {
                GamePath = @"C:\Game",
                WtfPath = @"C:\Game\WTF",
                Accounts = [liveAccount],
            }
        );
        _accountSnapshotDiffService.BuildDiff(liveAccount, activeSavedAccount).Returns(diff);
        _orchestrator
            .WaitForWowExitAndCleanupAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sut = CreateSut();

        // Act
        await InvokePrivateAsync(sut, "MonitorWowAsync", CancellationToken.None);

        // Assert
        await _orchestrator.DidNotReceive().SaveAccountAsync(
            Arg.Any<WowAccount>(),
            Arg.Any<AccountSavePlan>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>()
        );
        sut.IsSaveSelectionVisible.ShouldBeTrue();
        sut.SelectedLiveAccountName.ShouldBe("Alpha");
        sut.SaveSelectionTitle.ShouldBe("Save Account — Alpha");
        sut.StatusText.ShouldBe("Review changes for account 'Alpha'.");
        sut.CanConfirmSaveSelection.ShouldBeTrue();
    }

    private static SavedAccountSummary BuildSavedAccount(string id, string accountName)
    {
        return new SavedAccountSummary
        {
            Id = id,
            AccountName = accountName,
            RootPath = $@"C:\Profiles\{id}",
            SnapshotPath = $@"C:\Profiles\{id}\Account\{accountName}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private static WowAccount BuildLiveAccount(
        string accountName,
        string? realmName = null,
        string? characterName = null
    )
    {
        var realms = new List<WowRealm>();
        if (!string.IsNullOrWhiteSpace(realmName) && !string.IsNullOrWhiteSpace(characterName))
        {
            realms.Add(
                new WowRealm
                {
                    AccountName = accountName,
                    RealmName = realmName,
                    FolderPath = $@"C:\Game\WTF\Account\{accountName}\{realmName}",
                    Characters =
                    [
                        new WowCharacter
                        {
                            AccountName = accountName,
                            RealmName = realmName,
                            CharacterName = characterName,
                            FolderPath = $@"C:\Game\WTF\Account\{accountName}\{realmName}\{characterName}",
                        },
                    ],
                }
            );
        }

        return new WowAccount
        {
            AccountName = accountName,
            FolderPath = $@"C:\Game\WTF\Account\{accountName}",
            Realms = realms,
        };
    }

    private static AccountSnapshotDiff BuildDiff(
        string accountName,
        AccountSnapshotDiffStatus accountSettingsStatus,
        params CharacterSnapshotDiff[] characters
    )
    {
        var realms = characters
            .GroupBy(character => character.RealmName)
            .Select(group =>
                new RealmSnapshotDiff
                {
                    RealmName = group.Key,
                    Status = group.Any(character => character.Status != AccountSnapshotDiffStatus.Unchanged)
                        ? AccountSnapshotDiffStatus.Modified
                        : AccountSnapshotDiffStatus.Unchanged,
                    Characters = group.ToList(),
                }
            )
            .ToList();

        return new AccountSnapshotDiff
        {
            AccountName = accountName,
            AccountSettingsStatus = accountSettingsStatus,
            Realms = realms,
        };
    }
}
