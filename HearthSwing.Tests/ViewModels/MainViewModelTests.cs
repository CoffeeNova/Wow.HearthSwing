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
    private ICacheProtector _cacheProtector = null!;
    private IProcessMonitor _processMonitor = null!;
    private IFileSystem _fileSystem = null!;
    private IUpdateService _updateService = null!;
    private IProfileVersionService _versionService = null!;
    private IDialogService _dialogService = null!;
    private IUiDispatcher _uiDispatcher = null!;
    private IUiLogSink _logSink = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _settingsService = _fixture.Freeze<ISettingsService>();
        _profileManager = _fixture.Freeze<IProfileManager>();
        _cacheProtector = _fixture.Freeze<ICacheProtector>();
        _processMonitor = _fixture.Freeze<IProcessMonitor>();
        _fileSystem = _fixture.Freeze<IFileSystem>();
        _updateService = _fixture.Freeze<IUpdateService>();
        _versionService = _fixture.Freeze<IProfileVersionService>();
        _dialogService = _fixture.Freeze<IDialogService>();
        _uiDispatcher = _fixture.Freeze<IUiDispatcher>();
        _logSink = _fixture.Freeze<IUiLogSink>();

        _settingsService.Current.Returns(new AppSettings());
        _profileManager.DiscoverProfiles().Returns([]);
        _profileManager.DetectCurrentProfile().Returns((ProfileInfo?)null);
        _uiDispatcher.When(x => x.Invoke(Arg.Any<Action>())).Do(ci => ci.Arg<Action>().Invoke());
    }

    [TearDown]
    public void TearDown()
    {
        _cacheProtector.Dispose();
    }

    private MainViewModel CreateSut() =>
        new(
            _settingsService,
            _profileManager,
            _cacheProtector,
            _processMonitor,
            _fileSystem,
            _updateService,
            _versionService,
            _dialogService,
            _uiDispatcher,
            _logSink
        );

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
        _cacheProtector.IsLocked.Returns(false);

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
}
