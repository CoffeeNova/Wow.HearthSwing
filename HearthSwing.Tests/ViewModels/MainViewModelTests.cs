using System.Reflection;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Models;
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
    private ISwitchingOrchestrator _orchestrator = null!;
    private IProcessMonitor _processMonitor = null!;
    private IUpdateService _updateService = null!;
    private IDialogService _dialogService = null!;
    private IUiDispatcher _uiDispatcher = null!;
    private IUiLogSink _logSink = null!;
    private IWtfInspector _wtfInspector = null!;
    private IChangeHistoryService _changeHistoryService = null!;
    private ILegacyDataCleanupService _legacyDataCleanupService = null!;
    private ITemplateCatalog _templateCatalog = null!;
    private ITemplateCaptureService _templateCaptureService = null!;
    private ITemplateApplyService _templateApplyService = null!;
    private ITemplateRestoreOrchestrator _templateRestoreOrchestrator = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _settingsService = _fixture.Freeze<ISettingsService>();
        _orchestrator = _fixture.Freeze<ISwitchingOrchestrator>();
        _processMonitor = _fixture.Freeze<IProcessMonitor>();
        _updateService = _fixture.Freeze<IUpdateService>();
        _dialogService = _fixture.Freeze<IDialogService>();
        _uiDispatcher = _fixture.Freeze<IUiDispatcher>();
        _logSink = _fixture.Freeze<IUiLogSink>();
        _wtfInspector = _fixture.Freeze<IWtfInspector>();
        _changeHistoryService = _fixture.Freeze<IChangeHistoryService>();
        _legacyDataCleanupService = _fixture.Freeze<ILegacyDataCleanupService>();
        _templateCatalog = _fixture.Freeze<ITemplateCatalog>();
        _templateCaptureService = _fixture.Freeze<ITemplateCaptureService>();
        _templateApplyService = _fixture.Freeze<ITemplateApplyService>();
        _templateRestoreOrchestrator = _fixture.Freeze<ITemplateRestoreOrchestrator>();

        _settingsService.Current.Returns(
            new AppSettings { GamePath = @"C:\Game", ProfilesPath = @"C:\Profiles" }
        );
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

        _uiDispatcher
            .When(dispatcher => dispatcher.Invoke(Arg.Any<Action>()))
            .Do(call => call.Arg<Action>().Invoke());
    }

    [Test]
    public void Constructor_InitializesFromSettings()
    {
        // Arrange
        _settingsService.Current.Returns(
            new AppSettings
            {
                GamePath = @"D:\Games\WoW",
                ProfilesPath = @"D:\Profiles",
                UnlockDelaySeconds = 90,
            }
        );

        // Act
        var sut = CreateSut();

        // Assert
        sut.GamePath.ShouldBe(@"D:\Games\WoW");
        sut.ProfilesPath.ShouldBe(@"D:\Profiles");
        sut.UnlockDelay.ShouldBe(90);
    }

    [Test]
    public async Task LaunchWowAsync_LocksAndLaunches()
    {
        // Arrange
        _orchestrator.LockForLaunch().Returns(3);
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
    public async Task LaunchWowAsync_WhenLaunchFails_UnlocksCache()
    {
        // Arrange
        _orchestrator.LockForLaunch().Returns(1);
        _processMonitor
            .When(monitor => monitor.LaunchWow(@"C:\Game"))
            .Do(_ => throw new InvalidOperationException("fail"));

        var sut = CreateSut();

        // Act
        await InvokePrivateAsync(sut, "LaunchWowAsync");

        // Assert
        _orchestrator.Received().UnlockCache();
        sut.StatusText.ShouldBe("Launch failed!");
    }

    [Test]
    public void ForceRestore_WhenWowNotRunning_ShowsHistoryHint()
    {
        // Arrange
        _processMonitor.IsWowRunning().Returns(false);
        var sut = CreateSut();

        // Act
        InvokePrivate(sut, "ForceRestore");

        // Assert
        sut.StatusText.ShouldBe("Use History to restore.");
        sut.LogText.ShouldContain("Use History mode");
    }

    [Test]
    public void ToggleTemplateHistory_LoadsTemplateHistory()
    {
        // Arrange
        var entry = new HistoryEntry
        {
            TargetKey = "template/warlock",
            Kind = HistoryTargetKind.Template,
            CreatedUtc = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero),
            Description = "Update template",
            ArchivePath = @"C:\Profiles\.history\template\warlock\20260101_100000.tar.gz",
        };

        _templateCatalog
            .DiscoverTemplates()
            .Returns([
                new TemplateSummary
                {
                    Id = "warlock",
                    Name = "Warlock",
                    Kind = TemplateKind.Character,
                    RootPath = @"C:\Profiles\.templates\warlock",
                    SourceAccountName = "Main",
                },
            ]);
        _changeHistoryService.List("template/warlock").Returns([entry]);

        var sut = CreateSut();

        // Act
        sut.ToggleTemplateHistoryCommand.Execute("warlock");

        // Assert
        sut.IsTemplateHistoryVisible.ShouldBeTrue();
        sut.TemplateHistoryTitle.ShouldBe("Template History - Warlock");
        sut.TemplateHistorySubtitle.ShouldBe("Main");
        sut.TemplateHistoryEntries.ShouldHaveSingleItem();
        sut.TemplateHistoryEntries[0].TargetKey.ShouldBe("template/warlock");
    }

    [Test]
    public void ShowHistoryMode_LoadsNonTemplateHistoryGroups()
    {
        // Arrange
        var characterEntry = new HistoryEntry
        {
            TargetKey = "wtf/char/Main/Firemaw/Valeera",
            Kind = HistoryTargetKind.WtfCharacter,
            CreatedUtc = new DateTimeOffset(2026, 1, 2, 11, 0, 0, TimeSpan.Zero),
            Description = "Applied template Rogue UI",
            ArchivePath =
                @"C:\Profiles\.history\wtf\char\Main\Firemaw\Valeera\20260102_110000.tar.gz",
            AccountName = "Main",
            RealmName = "Firemaw",
            CharacterName = "Valeera",
        };
        var templateEntry = new HistoryEntry
        {
            TargetKey = "template/warlock",
            Kind = HistoryTargetKind.Template,
            CreatedUtc = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero),
            Description = "Updated template",
            ArchivePath = @"C:\Profiles\.history\template\warlock\20260101_100000.tar.gz",
        };

        _changeHistoryService.ListAll().Returns([characterEntry, templateEntry]);

        var sut = CreateSut();

        // Act
        sut.ShowHistoryModeCommand.Execute(null);

        // Assert
        sut.IsHistoryMode.ShouldBeTrue();
        sut.HistoryGroups.ShouldHaveSingleItem();
        sut.HistoryGroups[0].Title.ShouldBe("Valeera (Firemaw)");
        sut.HistoryGroups[0].Entries.ShouldHaveSingleItem();
        sut.HistoryGroups[0].Entries[0].TargetKey.ShouldBe("wtf/char/Main/Firemaw/Valeera");
    }

    [Test]
    public async Task RestoreHistoryEntryAsync_WhenConfirmed_RestoresEntryAndRefreshesHistory()
    {
        // Arrange
        var entry = new HistoryEntry
        {
            TargetKey = "wtf/account/Main",
            Kind = HistoryTargetKind.WtfAccount,
            CreatedUtc = new DateTimeOffset(2026, 1, 2, 11, 0, 0, TimeSpan.Zero),
            Description = "Applied template Shared UI",
            ArchivePath = @"C:\Profiles\.history\wtf\account\Main\20260102_110000.tar.gz",
            AccountName = "Main",
        };

        _dialogService.Confirm(Arg.Any<string>(), "Restore History").Returns(true);
        _changeHistoryService.RestoreAsync(entry).Returns(Task.CompletedTask);
        _changeHistoryService.ListAll().Returns([entry], [entry]);

        var sut = CreateSut();

        // Act
        await InvokePrivateAsync(sut, "RestoreHistoryEntryAsync", entry);

        // Assert
        await _changeHistoryService.Received(1).RestoreAsync(entry);
        sut.StatusText.ShouldBe("Restored Main.");
        sut.ToastMessage.ShouldBe("History restored.");
        sut.IsToastVisible.ShouldBeTrue();
    }

    [Test]
    public async Task DeleteHistoryEntry_WhenConfirmed_DeletesEntryAndRefreshesHistory()
    {
        // Arrange
        var entry = new HistoryEntry
        {
            TargetKey = "wtf/account/Main",
            Kind = HistoryTargetKind.WtfAccount,
            CreatedUtc = new DateTimeOffset(2026, 1, 2, 11, 0, 0, TimeSpan.Zero),
            Description = "Applied template Shared UI",
            ArchivePath = @"C:\Profiles\.history\wtf\account\Main\20260102_110000.tar.gz",
            AccountName = "Main",
        };

        _dialogService.Confirm(Arg.Any<string>(), "Delete History Entry").Returns(true);
        _changeHistoryService.DeleteAsync(entry).Returns(Task.CompletedTask);
        _changeHistoryService.ListAll().Returns([entry], []);

        var sut = CreateSut();

        // Act
        await InvokePrivateAsync(sut, "DeleteHistoryEntryAsync", entry);

        // Assert
        _ = _changeHistoryService.Received(1).DeleteAsync(entry);
        sut.StatusText.ShouldBe("History entry deleted.");
        sut.ToastMessage.ShouldBe("History entry deleted.");
        sut.IsToastVisible.ShouldBeTrue();
    }

    [Test]
    public void CleanupLegacyData_WhenConfirmed_CleansUpAndShowsToast()
    {
        // Arrange
        var summary = new LegacyDataCleanupSummary
        {
            Directories = [@"C:\Profiles\.versions", @"C:\Profiles\acc1"],
            Files = [@"C:\Profiles\.active-account.json"],
        };

        _legacyDataCleanupService.Discover().Returns(summary);
        _legacyDataCleanupService.Cleanup().Returns(summary);
        _dialogService.Confirm(Arg.Any<string>(), "Clean Up Legacy Data").Returns(true);

        var sut = CreateSut();

        // Act
        sut.CleanupLegacyDataCommand.Execute(null);

        // Assert
        _legacyDataCleanupService.Received(1).Cleanup();
        sut.StatusText.ShouldBe("Removed 3 legacy item(s).");
        sut.ToastMessage.ShouldBe("Legacy data cleaned up.");
        sut.IsToastVisible.ShouldBeTrue();
    }

    [Test]
    public void OpenApplyTemplate_ForAccount_SelectsOnlyMatchAfterFiltering()
    {
        // Arrange
        const string templateId = "acc-template";
        _templateCatalog
            .DiscoverTemplates()
            .Returns([
                new TemplateSummary
                {
                    Id = templateId,
                    Name = "Account Template",
                    Kind = TemplateKind.Account,
                    RootPath = @"C:\Profiles\.templates\acc-template",
                    SourceAccountName = "acc1",
                },
            ]);

        _wtfInspector
            .Inspect(@"C:\Game")
            .Returns(
                new WowInstallation
                {
                    GamePath = @"C:\Game",
                    WtfPath = @"C:\Game\WTF",
                    Accounts =
                    [
                        new WowAccount
                        {
                            AccountName = "acc1",
                            FolderPath = @"C:\Game\WTF\Account\acc1",
                        },
                        new WowAccount
                        {
                            AccountName = "acc2",
                            FolderPath = @"C:\Game\WTF\Account\acc2",
                        },
                    ],
                }
            );

        var sut = CreateSut();

        // Act
        sut.OpenApplyTemplateCommand.Execute(templateId);
        sut.TargetSearchText = "acc1";

        // Assert
        sut.SelectedTargetAccount.ShouldNotBeNull();
        sut.SelectedTargetAccount!.AccountName.ShouldBe("acc1");
        sut.CanConfirmApplyTemplate.ShouldBeTrue();
    }

    [Test]
    public void OpenApplyTemplate_ForCharacter_SelectsOnlyMatchAfterFiltering()
    {
        // Arrange
        const string templateId = "char-template";
        _templateCatalog
            .DiscoverTemplates()
            .Returns([
                new TemplateSummary
                {
                    Id = templateId,
                    Name = "Character Template",
                    Kind = TemplateKind.Character,
                    RootPath = @"C:\Profiles\.templates\char-template",
                    SourceAccountName = "acc1",
                },
            ]);

        _wtfInspector
            .Inspect(@"C:\Game")
            .Returns(
                new WowInstallation
                {
                    GamePath = @"C:\Game",
                    WtfPath = @"C:\Game\WTF",
                    Accounts =
                    [
                        new WowAccount
                        {
                            AccountName = "acc1",
                            FolderPath = @"C:\Game\WTF\Account\acc1",
                            Realms =
                            [
                                new WowRealm
                                {
                                    AccountName = "acc1",
                                    RealmName = "RealmOne",
                                    FolderPath = @"C:\Game\WTF\Account\acc1\RealmOne",
                                    Characters =
                                    [
                                        new WowCharacter
                                        {
                                            AccountName = "acc1",
                                            RealmName = "RealmOne",
                                            CharacterName = "RogueOne",
                                            FolderPath =
                                                @"C:\Game\WTF\Account\acc1\RealmOne\RogueOne",
                                        },
                                        new WowCharacter
                                        {
                                            AccountName = "acc1",
                                            RealmName = "RealmOne",
                                            CharacterName = "MageTwo",
                                            FolderPath =
                                                @"C:\Game\WTF\Account\acc1\RealmOne\MageTwo",
                                        },
                                    ],
                                },
                            ],
                        },
                    ],
                }
            );

        var sut = CreateSut();

        // Act
        sut.OpenApplyTemplateCommand.Execute(templateId);
        sut.TargetSearchText = "RogueOne";

        // Assert
        sut.SelectedTargetCharacter.ShouldNotBeNull();
        sut.SelectedTargetCharacter!.CharacterName.ShouldBe("RogueOne");
        sut.CanConfirmApplyTemplate.ShouldBeTrue();
    }

    private MainViewModel CreateSut() =>
        new(
            _settingsService,
            _orchestrator,
            _processMonitor,
            _updateService,
            _dialogService,
            _uiDispatcher,
            _logSink,
            _wtfInspector,
            _changeHistoryService,
            _legacyDataCleanupService,
            _templateCatalog,
            _templateCaptureService,
            _templateApplyService,
            _templateRestoreOrchestrator
        );

    private static void InvokePrivate(MainViewModel sut, string methodName, params object[] args)
    {
        var method =
            typeof(MainViewModel).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException($"Could not find method '{methodName}'.");

        _ = method.Invoke(sut, args);
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
}
