using System.Reflection;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Models;
using HearthSwing.Models.Accounts;
using HearthSwing.Models.Templates;
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
    private ITemplateCatalog _templateCatalog = null!;
    private ITemplateCaptureService _templateCaptureService = null!;
    private ITemplateApplyService _templateApplyService = null!;
    private ITemplateVersionService _templateVersionService = null!;

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
        _templateCatalog = _fixture.Freeze<ITemplateCatalog>();
        _templateCaptureService = _fixture.Freeze<ITemplateCaptureService>();
        _templateApplyService = _fixture.Freeze<ITemplateApplyService>();
        _templateVersionService = _fixture.Freeze<ITemplateVersionService>();

        _settingsService.Current.Returns(
            new AppSettings { GamePath = @"C:\Game", ProfilesPath = @"C:\Profiles" }
        );
        _savedAccountCatalog.DiscoverAccounts().Returns([]);
        _savedAccountCatalog.GetActiveAccount().Returns((ActiveAccountState?)null);
        _templateCatalog.DiscoverTemplates().Returns([]);
        _wtfInspector
            .Inspect(@"C:\Game")
            .Returns(
                new WowInstallation
                {
                    GamePath = @"C:\Game",
                    WtfPath = @"C:\Game\WTF",
                    Accounts = [],
                }
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
            _wtfInspector,
            _templateCatalog,
            _templateCaptureService,
            _templateApplyService,
            _templateVersionService
        );

    private static void InvokePrivate(MainViewModel sut, string methodName, params object[] args)
    {
        var method =
            typeof(MainViewModel).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException($"Could not find method '{methodName}'.");
        method.Invoke(sut, args);
    }

    private static async Task InvokePrivateAsync(
        MainViewModel sut,
        string methodName,
        params object[] args
    )
    {
        var method =
            typeof(MainViewModel).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException($"Could not find method '{methodName}'.");

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
        _savedAccountCatalog
            .GetActiveAccount()
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
    public void Constructor_WhenSavedAccountStorageIsUnsupported_ShowsWarningAndKeepsAppUsable()
    {
        // Arrange
        _savedAccountCatalog.GetActiveAccount().Returns((ActiveAccountState?)null);
        _savedAccountCatalog
            .When(catalog => catalog.DiscoverAccounts())
            .Do(_ =>
                throw new InvalidOperationException(
                    "Unsupported saved-account storage at 'C:\\Profiles\\donky'. Expected metadata file 'account.json'."
                )
            );

        // Act
        var sut = Should.NotThrow(CreateSut);

        // Assert
        sut.CurrentAccountName.ShouldBe("None");
        sut.CurrentSavedAccountId.ShouldBeEmpty();
        sut.SavedAccounts.ShouldBeEmpty();
        _dialogService
            .Received()
            .ShowWarning(
                Arg.Is<string>(message =>
                    message.Contains("Unsupported saved-account storage")
                    && message.Contains("Choose an empty Saved Accounts Path")
                ),
                "Saved Accounts Path Error"
            );
        sut.LogText.ShouldContain("Unsupported saved-account storage");
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
        _processMonitor
            .When(monitor => monitor.LaunchWow(@"C:\Game"))
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
        _savedAccountCatalog
            .GetActiveAccount()
            .Returns(new ActiveAccountState { SavedAccountId = "alpha", AccountName = "Alpha" });
        _savedAccountCatalog.GetById("alpha").Returns(activeSavedAccount);
        _savedAccountCatalog.DiscoverAccounts().Returns([activeSavedAccount]);
        _wtfInspector
            .Inspect(@"C:\Game")
            .Returns(
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
        await _orchestrator
            .Received()
            .SaveAccountAsync(
                Arg.Is<WowAccount>(account => account.AccountName == "Alpha"),
                Arg.Is<AccountSavePlan>(plan =>
                    plan.AccountName == "Alpha" && plan.SaveAccountSettings
                ),
                false,
                Arg.Any<CancellationToken>()
            );
        sut.IsWowRunning.ShouldBeFalse();
    }

    [Test]
    public async Task SaveAccountAsync_WhenMultipleLiveAccountsPresent_OpensSelectionOverlayForAccountChoice()
    {
        // Arrange
        _wtfInspector
            .Inspect(@"C:\Game")
            .Returns(
                new WowInstallation
                {
                    GamePath = @"C:\Game",
                    WtfPath = @"C:\Game\WTF",
                    Accounts = [BuildLiveAccount("Alpha"), BuildLiveAccount("Bravo")],
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
        _wtfInspector
            .Inspect(@"C:\Game")
            .Returns(
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
        await InvokePrivateAsync(sut, "LoadSaveSelectionForSelectedAccountAsync", "Alpha");

        // Act
        await InvokePrivateAsync(sut, "ConfirmSaveSelectionAsync");

        // Assert
        await _orchestrator
            .Received()
            .SaveAccountAsync(
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
    public async Task LoadSaveSelectionForSelectedAccountAsync_SortsChangedRealmsAndCharactersFirst()
    {
        // Arrange
        var savedAccount = BuildSavedAccount("alpha", "Alpha");
        var liveAccount = new WowAccount
        {
            AccountName = "Alpha",
            FolderPath = @"C:\Game\WTF\Account\Alpha",
            Realms =
            [
                new WowRealm
                {
                    AccountName = "Alpha",
                    RealmName = "Firemaw",
                    FolderPath = @"C:\Game\WTF\Account\Alpha\Firemaw",
                    Characters =
                    [
                        new WowCharacter
                        {
                            AccountName = "Alpha",
                            RealmName = "Firemaw",
                            CharacterName = "UnchangedCharacter",
                            FolderPath = @"C:\Game\WTF\Account\Alpha\Firemaw\UnchangedCharacter",
                        },
                        new WowCharacter
                        {
                            AccountName = "Alpha",
                            RealmName = "Firemaw",
                            CharacterName = "ChangedCharacter",
                            FolderPath = @"C:\Game\WTF\Account\Alpha\Firemaw\ChangedCharacter",
                        },
                    ],
                },
                new WowRealm
                {
                    AccountName = "Alpha",
                    RealmName = "Pyrewood",
                    FolderPath = @"C:\Game\WTF\Account\Alpha\Pyrewood",
                    Characters =
                    [
                        new WowCharacter
                        {
                            AccountName = "Alpha",
                            RealmName = "Pyrewood",
                            CharacterName = "QuietCharacter",
                            FolderPath = @"C:\Game\WTF\Account\Alpha\Pyrewood\QuietCharacter",
                        },
                    ],
                },
            ],
        };
        var diff = new AccountSnapshotDiff
        {
            AccountName = "Alpha",
            AccountSettingsStatus = AccountSnapshotDiffStatus.Unchanged,
            Realms =
            [
                new RealmSnapshotDiff
                {
                    RealmName = "Pyrewood",
                    Status = AccountSnapshotDiffStatus.Unchanged,
                    Characters =
                    [
                        new CharacterSnapshotDiff
                        {
                            RealmName = "Pyrewood",
                            CharacterName = "QuietCharacter",
                            FolderPath = @"C:\Game\WTF\Account\Alpha\Pyrewood\QuietCharacter",
                            Status = AccountSnapshotDiffStatus.Unchanged,
                        },
                    ],
                },
                new RealmSnapshotDiff
                {
                    RealmName = "Firemaw",
                    Status = AccountSnapshotDiffStatus.Modified,
                    Characters =
                    [
                        new CharacterSnapshotDiff
                        {
                            RealmName = "Firemaw",
                            CharacterName = "UnchangedCharacter",
                            FolderPath = @"C:\Game\WTF\Account\Alpha\Firemaw\UnchangedCharacter",
                            Status = AccountSnapshotDiffStatus.Unchanged,
                        },
                        new CharacterSnapshotDiff
                        {
                            RealmName = "Firemaw",
                            CharacterName = "ChangedCharacter",
                            FolderPath = @"C:\Game\WTF\Account\Alpha\Firemaw\ChangedCharacter",
                            Status = AccountSnapshotDiffStatus.Modified,
                        },
                    ],
                },
            ],
        };

        _savedAccountCatalog.FindByAccountName("Alpha").Returns(savedAccount);
        _wtfInspector
            .Inspect(@"C:\Game")
            .Returns(
                new WowInstallation
                {
                    GamePath = @"C:\Game",
                    WtfPath = @"C:\Game\WTF",
                    Accounts = [liveAccount],
                }
            );
        _accountSnapshotDiffService.BuildDiff(liveAccount, savedAccount).Returns(diff);

        var sut = CreateSut();
        await InvokePrivateAsync(sut, "SaveAccountAsync");

        // Act
        await InvokePrivateAsync(sut, "LoadSaveSelectionForSelectedAccountAsync", "Alpha");

        // Assert
        sut.SaveRealms.Count.ShouldBe(2);
        sut.SaveRealms[0].RealmName.ShouldBe("Firemaw");
        sut.SaveRealms[1].RealmName.ShouldBe("Pyrewood");
        sut.SaveRealms[0].Characters[0].CharacterName.ShouldBe("ChangedCharacter");
        sut.SaveRealms[0].Characters[1].CharacterName.ShouldBe("UnchangedCharacter");
    }

    [Test]
    public async Task MonitorWowAsync_WhenAutoSaveDisabled_OpensManualSaveSelectionInsteadOfSaving()
    {
        // Arrange
        var activeSavedAccount = BuildSavedAccount("alpha", "Alpha") with
        {
            IsActive = true,
        };
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
        _savedAccountCatalog
            .GetActiveAccount()
            .Returns(new ActiveAccountState { SavedAccountId = "alpha", AccountName = "Alpha" });
        _savedAccountCatalog.GetById("alpha").Returns(activeSavedAccount);
        _savedAccountCatalog.DiscoverAccounts().Returns([activeSavedAccount]);
        _savedAccountCatalog.FindByAccountName("Alpha").Returns(activeSavedAccount);
        _wtfInspector
            .Inspect(@"C:\Game")
            .Returns(
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
        await InvokePrivateAsync(sut, "LoadSaveSelectionForSelectedAccountAsync", "Alpha");

        // Assert
        await _orchestrator
            .DidNotReceive()
            .SaveAccountAsync(
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
                            FolderPath =
                                $@"C:\Game\WTF\Account\{accountName}\{realmName}\{characterName}",
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
            .Select(group => new RealmSnapshotDiff
            {
                RealmName = group.Key,
                Status = group.Any(character =>
                    character.Status != AccountSnapshotDiffStatus.Unchanged
                )
                    ? AccountSnapshotDiffStatus.Modified
                    : AccountSnapshotDiffStatus.Unchanged,
                Characters = group.ToList(),
            })
            .ToList();

        return new AccountSnapshotDiff
        {
            AccountName = accountName,
            AccountSettingsStatus = accountSettingsStatus,
            Realms = realms,
        };
    }

    private static WowCharacter SampleCharacter =>
        new()
        {
            AccountName = "MainAccount",
            RealmName = "Firemaw",
            CharacterName = "Thrall",
            FolderPath = @"C:\WTF\Account\MainAccount\Firemaw\Thrall",
        };

    private static TemplateSummary SampleTemplate =>
        new()
        {
            Id = "warlock",
            Name = "Warlock - TBC",
            RootPath = @"C:\Profiles\.templates\warlock",
            SourceAccountName = "MainAccount",
            SourceRealmName = "Firemaw",
            SourceCharacterName = "Thrall",
        };

    [Test]
    public void ShowTemplatesMode_SetsTemplatesModeFlags()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        sut.ShowTemplatesModeCommand.Execute(null);

        // Assert
        sut.IsTemplatesMode.ShouldBeTrue();
        sut.IsAccountsMode.ShouldBeFalse();
    }

    [Test]
    public void ShowAccountsMode_SetsAccountsModeFlags()
    {
        // Arrange
        var sut = CreateSut();
        sut.ShowTemplatesModeCommand.Execute(null);

        // Act
        sut.ShowAccountsModeCommand.Execute(null);

        // Assert
        sut.IsAccountsMode.ShouldBeTrue();
        sut.IsTemplatesMode.ShouldBeFalse();
    }

    [Test]
    public void ConfirmCreateTemplate_WhenValid_CallsCaptureServiceAndRefreshes()
    {
        // Arrange
        _templateCaptureService
            .CreateTemplate(Arg.Any<WowCharacter>(), "Warlock - TBC")
            .Returns(SampleTemplate);
        var sut = CreateSut();
        sut.SelectedDonorCharacter = SampleCharacter;
        sut.NewTemplateName = "Warlock - TBC";

        // Act
        sut.ConfirmCreateTemplateCommand.Execute(null);

        // Assert
        _templateCaptureService.Received().CreateTemplate(SampleCharacter, "Warlock - TBC");
        sut.IsTemplateDonorSelectionVisible.ShouldBeFalse();
    }

    [Test]
    public void ConfirmApplyTemplate_WhenValid_CallsApplyService()
    {
        // Arrange
        var template = SampleTemplate;
        var sut = CreateSut();
        sut.TemplateToApply = template;
        sut.SelectedTargetCharacter = SampleCharacter;

        // Act
        sut.ConfirmApplyTemplateCommand.Execute(null);

        // Assert
        _templateApplyService
            .Received()
            .ApplyTemplate(template, SampleCharacter, Arg.Any<TemplateApplyOptions>());
        sut.IsTemplateApplyVisible.ShouldBeFalse();
    }

    [Test]
    public void ConfirmApplyTemplate_WhenWowRunning_DoesNotApply()
    {
        // Arrange
        var sut = CreateSut();
        sut.TemplateToApply = SampleTemplate;
        sut.SelectedTargetCharacter = SampleCharacter;
        sut.IsWowRunning = true;

        // Act
        sut.ConfirmApplyTemplateCommand.Execute(null);

        // Assert
        _templateApplyService
            .DidNotReceive()
            .ApplyTemplate(
                Arg.Any<TemplateSummary>(),
                Arg.Any<WowCharacter>(),
                Arg.Any<TemplateApplyOptions>()
            );
    }

    [Test]
    public void ConfirmApplyTemplate_WhenIncludeAccountSettingsDeclined_DoesNotApply()
    {
        // Arrange
        _dialogService.Confirm(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        var sut = CreateSut();
        sut.TemplateToApply = SampleTemplate;
        sut.SelectedTargetCharacter = SampleCharacter;
        sut.IncludeAccountSettings = true;

        // Act
        sut.ConfirmApplyTemplateCommand.Execute(null);

        // Assert
        _templateApplyService
            .DidNotReceive()
            .ApplyTemplate(
                Arg.Any<TemplateSummary>(),
                Arg.Any<WowCharacter>(),
                Arg.Any<TemplateApplyOptions>()
            );
    }

    [Test]
    public void DeleteTemplate_WhenConfirmed_CallsCatalogDelete()
    {
        // Arrange
        _templateCatalog.DiscoverTemplates().Returns([SampleTemplate]);
        _dialogService.Confirm(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        var sut = CreateSut();

        // Act
        sut.DeleteTemplateCommand.Execute("warlock");

        // Assert
        _templateCatalog.Received().Delete("warlock");
    }

    [Test]
    public void ToggleTemplateVersionHistory_LoadsVersionsAndShowsPanel()
    {
        // Arrange
        var version = new ProfileVersion
        {
            VersionId = "20260101_100000",
            ProfileId = "warlock",
            CreatedAt = new DateTime(2026, 1, 1, 10, 0, 0),
            ArchivePath = @"C:\Profiles\.template-versions\warlock\20260101_100000.tar.gz",
        };
        _templateVersionService.GetVersions("warlock").Returns([version]);
        var sut = CreateSut();

        // Act
        sut.ToggleTemplateVersionHistoryCommand.Execute("warlock");

        // Assert
        sut.IsTemplateVersionHistoryVisible.ShouldBeTrue();
        sut.TemplateVersions.ShouldHaveSingleItem();
    }
}
