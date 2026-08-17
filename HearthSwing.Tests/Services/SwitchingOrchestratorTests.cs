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
    private ISettingsService _settingsService = null!;
    private ICacheProtector _cacheProtector = null!;
    private IProcessMonitor _processMonitor = null!;
    private IFileSystem _fileSystem = null!;
    private SwitchingOrchestrator _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _settingsService = _fixture.Freeze<ISettingsService>();
        _cacheProtector = _fixture.Freeze<ICacheProtector>();
        _processMonitor = _fixture.Freeze<IProcessMonitor>();
        _fileSystem = _fixture.Freeze<IFileSystem>();

        _settingsService.Current.Returns(new AppSettings { GamePath = @"C:\Game" });

        _sut = new SwitchingOrchestrator(
            _settingsService,
            _cacheProtector,
            _processMonitor,
            _fileSystem
        );
    }

    [TearDown]
    public void TearDown()
    {
        _cacheProtector.Dispose();
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
        // Act
        _sut.ForceRestoreCache();

        // Assert
        _cacheProtector.Received().ForceRestore(@"C:\Game\WTF");
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
