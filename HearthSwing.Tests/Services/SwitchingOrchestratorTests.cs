using System.IO;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Models;
using HearthSwing.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class SwitchingOrchestratorTests
{
    private IFixture _fixture = null!;
    private IProfileManager _profileManager = null!;
    private ICacheProtector _cacheProtector = null!;
    private IProcessMonitor _processMonitor = null!;
    private IFileSystem _fileSystem = null!;
    private IProfileVersionService _versionService = null!;
    private SwitchingOrchestrator _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _profileManager = _fixture.Freeze<IProfileManager>();
        _cacheProtector = _fixture.Freeze<ICacheProtector>();
        _processMonitor = _fixture.Freeze<IProcessMonitor>();
        _fileSystem = _fixture.Freeze<IFileSystem>();
        _versionService = _fixture.Freeze<IProfileVersionService>();

        _profileManager.WtfPath.Returns(@"C:\Game\WTF");
        _profileManager.ProfilesPath.Returns(@"C:\Profiles");

        _sut = new SwitchingOrchestrator(
            _profileManager,
            _cacheProtector,
            _processMonitor,
            _fileSystem,
            _versionService
        );
    }

    [TearDown]
    public void TearDown()
    {
        _cacheProtector.Dispose();
    }

    [Test]
    public void SwitchTo_WhenCacheIsLocked_UnlocksBeforeSwitching()    {
        // Arrange
        _cacheProtector.IsLocked.Returns(true);
        var target = new ProfileInfo { Id = "alpha", FolderPath = @"C:\Profiles\alpha" };

        // Act
        _sut.SwitchTo(target);

        // Assert
        Received.InOrder(() =>
        {
            _cacheProtector.Unlock();
            _profileManager.SwitchTo(target);
        });
    }

    [Test]
    public void SwitchTo_WhenCacheIsNotLocked_SwitchesWithoutUnlocking()
    {
        // Arrange
        _cacheProtector.IsLocked.Returns(false);
        var target = new ProfileInfo { Id = "beta", FolderPath = @"C:\Profiles\beta" };

        // Act
        _sut.SwitchTo(target);

        // Assert
        _cacheProtector.DidNotReceive().Unlock();
        _profileManager.Received().SwitchTo(target);
    }

    [Test]
    public void UnlockCache_WhenLocked_UnlocksProtector()
    {
        // Arrange
        _cacheProtector.IsLocked.Returns(true);

        // Act
        _sut.UnlockCache();

        // Assert
        _cacheProtector.Received().Unlock();
    }

    [Test]
    public void UnlockCache_WhenNotLocked_DoesNothing()
    {
        // Arrange
        _cacheProtector.IsLocked.Returns(false);

        // Act
        _sut.UnlockCache();

        // Assert
        _cacheProtector.DidNotReceive().Unlock();
    }

    [Test]
    public void LockForLaunch_WhenWtfFolderExists_LocksAndReturnsProtectedCount()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _cacheProtector.IsLocked.Returns(false);
        _cacheProtector.ProtectedFileCount.Returns(5);

        // Act
        var result = _sut.LockForLaunch();

        // Assert
        _cacheProtector.Received().Lock(@"C:\Game\WTF");
        result.ShouldBe(5);
    }

    [Test]
    public void LockForLaunch_WhenCacheAlreadyLocked_UnlocksFirstThenRelocks()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _cacheProtector.IsLocked.Returns(true);
        _cacheProtector.ProtectedFileCount.Returns(3);

        // Act
        _sut.LockForLaunch();

        // Assert
        Received.InOrder(() =>
        {
            _cacheProtector.Unlock();
            _cacheProtector.Lock(@"C:\Game\WTF");
        });
    }

    [Test]
    public void LockForLaunch_WhenWtfFolderMissing_ReturnsZeroWithoutLocking()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\Game\WTF").Returns(false);

        // Act
        var result = _sut.LockForLaunch();

        // Assert
        _cacheProtector.DidNotReceive().Lock(Arg.Any<string>());
        result.ShouldBe(0);
    }

    [Test]
    public void ForceRestoreCache_CallsForceRestoreOnProtector()
    {
        // Arrange
        _profileManager.DetectCurrentProfile().Returns((ProfileInfo?)null);

        // Act
        _sut.ForceRestoreCache();

        // Assert
        _cacheProtector.Received().ForceRestore(@"C:\Game\WTF");
    }

    [Test]
    public void ForceRestoreCache_SeedsMissingCacheFilesFromActiveProfile()
    {
        // Arrange
        var profile = new ProfileInfo { Id = "hero", FolderPath = @"C:\Profiles\hero" };
        _profileManager.DetectCurrentProfile().Returns(profile);
        _fileSystem.DirectoryExists(@"C:\Profiles\hero").Returns(true);

        var profileCacheFile = @"C:\Profiles\hero\Account\Test\cache.md5";
        _cacheProtector
            .CollectCacheFiles(@"C:\Profiles\hero")
            .Returns([profileCacheFile]);

        var wtfTarget = @"C:\Game\WTF\Account\Test\cache.md5";
        _fileSystem.FileExists(wtfTarget).Returns(false);
        _fileSystem.DirectoryExists(@"C:\Game\WTF\Account\Test").Returns(true);

        // Act
        _sut.ForceRestoreCache();

        // Assert
        _fileSystem.Received().CopyFile(profileCacheFile, wtfTarget);
        _cacheProtector.Received().ForceRestore(@"C:\Game\WTF");
    }

    [Test]
    public void RestoreFromSaved_UnlocksAndRestoresActiveProfile()
    {
        // Arrange
        _cacheProtector.IsLocked.Returns(true);

        // Act
        _sut.RestoreFromSaved();

        // Assert
        Received.InOrder(() =>
        {
            _cacheProtector.Unlock();
            _profileManager.RestoreActiveProfile();
        });
    }

    [Test]
    public async Task SaveWithVersioningAsync_WhenVersioningEnabled_CreatesVersionThenSaves()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _fileSystem.DirectoryExists(@"C:\Profiles\hero").Returns(true);
        _cacheProtector.IsLocked.Returns(false);

        // Act
        await _sut.SaveWithVersioningAsync("hero", versioningEnabled: true);

        // Assert
        Received.InOrder(() =>
        {
            _ = _versionService.CreateVersionAsync("hero");
            _profileManager.SaveCurrentAsProfile("hero");
        });
    }

    [Test]
    public async Task SaveWithVersioningAsync_WhenVersioningDisabled_SavesWithoutCreatingVersion()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _cacheProtector.IsLocked.Returns(false);

        // Act
        await _sut.SaveWithVersioningAsync("hero", versioningEnabled: false);

        // Assert
        await _versionService.DidNotReceive().CreateVersionAsync(Arg.Any<string>());
        _profileManager.Received().SaveCurrentAsProfile("hero");
    }

    [Test]
    public async Task SaveWithVersioningAsync_WhenWtfFolderMissing_SkipsSaveAndLogs()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\Game\WTF").Returns(false);
        var logMessages = new List<string>();
        _sut.Log += logMessages.Add;

        // Act
        await _sut.SaveWithVersioningAsync("hero", versioningEnabled: true);

        // Assert
        _profileManager.DidNotReceive().SaveCurrentAsProfile(Arg.Any<string>());
        logMessages.ShouldHaveSingleItem();
        logMessages[0].ShouldContain("WTF folder not found");
    }

    [Test]
    public async Task SaveWithVersioningAsync_UnlocksCacheBeforeSaving()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _fileSystem.DirectoryExists(@"C:\Profiles\hero").Returns(false);
        _cacheProtector.IsLocked.Returns(true);

        // Act
        await _sut.SaveWithVersioningAsync("hero", versioningEnabled: false);

        // Assert
        _cacheProtector.Received().Unlock();
    }

    [Test]
    public async Task WaitForWowExitAndCleanupAsync_WhenProcessExits_UnlocksCache()
    {
        // Arrange
        _cacheProtector.IsLocked.Returns(true);
        _processMonitor.WaitForExitAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        // Act
        await _sut.WaitForWowExitAndCleanupAsync(postExitDelayMs: 0, CancellationToken.None);

        // Assert
        _cacheProtector.Received().Unlock();
    }

    [Test]
    public async Task WaitForWowExitAndCleanupAsync_WhenCancelled_DoesNotUnlock()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _cacheProtector.IsLocked.Returns(true);
        _processMonitor
            .WaitForExitAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled(cts.Token));

        // Act
        await _sut.WaitForWowExitAndCleanupAsync(postExitDelayMs: 0, cts.Token);

        // Assert
        _cacheProtector.DidNotReceive().Unlock();
    }
}
