using System.Reflection;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Models;
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
    private IProfileManager _profileManager = null!;
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
        _profileManager = _fixture.Freeze<IProfileManager>();
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
        _profileManager.DiscoverProfiles().Returns([]);
        _profileManager.DetectCurrentProfile().Returns((ProfileInfo?)null);
        _profileManager.WtfPath.Returns(@"C:\Game\WTF");
        _profileManager.ProfilesPath.Returns(@"C:\Profiles");
        _uiDispatcher.When(x => x.Invoke(Arg.Any<Action>())).Do(ci => ci.Arg<Action>().Invoke());
    }

    private MainViewModel CreateSut() =>
        new(
            _settingsService,
            _profileManager,
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

        var profile = new ProfileInfo
        {
            Id = "donky",
            FolderPath = @"C:\Profiles\donky",
            IsActive = true,
        };
        _profileManager.DetectCurrentProfile().Returns(profile);
        _profileManager.DiscoverProfiles().Returns([profile]);
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
        sut.CurrentProfileId.ShouldBe("donky");
        sut.CurrentProfileName.ShouldBe("donky");
        sut.Profiles.ShouldHaveSingleItem();
    }

    [Test]
    public void SwitchProfile_WhenCalled_DelegatesToOrchestrator()
    {
        // Arrange
        var target = new ProfileInfo
        {
            Id = "alpha",
            FolderPath = @"C:\Profiles\alpha",
        };

        _processMonitor.IsWowRunning().Returns(false);
        _profileManager.DiscoverProfiles().Returns([target]);

        var sut = CreateSut();

        // Act
        InvokePrivate(sut, "SwitchProfile", "alpha");

        // Assert
        _orchestrator.Received().SwitchTo(Arg.Is<ProfileInfo>(p => p.Id == "alpha"));
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
    public async Task MonitorWowAsync_WhenProcessExitsWithAutoSaveEnabled_SavesActiveProfile()
    {
        // Arrange
        var activeProfile = new ProfileInfo
        {
            Id = "alpha",
            FolderPath = @"C:\Profiles\alpha",
            IsActive = true,
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
        _profileManager.DetectCurrentProfile().Returns(activeProfile);
        _profileManager.DiscoverProfiles().Returns([activeProfile]);
        _orchestrator
            .WaitForWowExitAndCleanupAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sut = CreateSut();

        // Act
        await InvokePrivateAsync(sut, "MonitorWowAsync", CancellationToken.None);

        // Assert
        await _orchestrator.Received().SaveWithVersioningAsync(
            "alpha",
            false,
            Arg.Any<CancellationToken>()
        );
        sut.IsWowRunning.ShouldBeFalse();
    }
}
