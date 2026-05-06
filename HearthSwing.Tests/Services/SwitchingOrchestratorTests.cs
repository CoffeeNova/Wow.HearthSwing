using System.IO;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Models.Accounts;
using HearthSwing.Services;
using HearthSwing.Models.WoW;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class SwitchingOrchestratorTests
{
    private IFixture _fixture = null!;
    private ISavedAccountCatalog _savedAccountCatalog = null!;
    private IAccountSnapshotSaveService _accountSnapshotSaveService = null!;
    private IAccountSwitchService _accountSwitchService = null!;
    private ICacheProtector _cacheProtector = null!;
    private IProcessMonitor _processMonitor = null!;
    private IFileSystem _fileSystem = null!;
    private IProfileVersionService _versionService = null!;
    private SwitchingOrchestrator _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _savedAccountCatalog = _fixture.Freeze<ISavedAccountCatalog>();
        _accountSnapshotSaveService = _fixture.Freeze<IAccountSnapshotSaveService>();
        _accountSwitchService = _fixture.Freeze<IAccountSwitchService>();
        _cacheProtector = _fixture.Freeze<ICacheProtector>();
        _processMonitor = _fixture.Freeze<IProcessMonitor>();
        _fileSystem = _fixture.Freeze<IFileSystem>();
        _versionService = _fixture.Freeze<IProfileVersionService>();

        _accountSwitchService.WtfPath.Returns(@"C:\Game\WTF");

        _sut = new SwitchingOrchestrator(
            _savedAccountCatalog,
            _accountSnapshotSaveService,
            _accountSwitchService,
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
    public void SwitchToSavedAccount_WhenCacheIsLocked_UnlocksBeforeSwitching()
    {
        // Arrange
        _cacheProtector.IsLocked.Returns(true);
        var target = new SavedAccountSummary
        {
            Id = "alpha",
            AccountName = "Alpha",
            RootPath = @"C:\Profiles\alpha",
            SnapshotPath = @"C:\Profiles\alpha\Account\Alpha",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        // Act
        _sut.SwitchTo(target);

        // Assert
        Received.InOrder(() =>
        {
            _cacheProtector.Unlock();
            _accountSwitchService.SwitchTo(target);
        });
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
    public void LockForLaunch_WhenActiveSavedAccountExists_LocksOnlyThatAccount()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _cacheProtector.IsLocked.Returns(false);
        _cacheProtector.ProtectedFileCount.Returns(2);
        _savedAccountCatalog.GetActiveAccount()
            .Returns(new ActiveAccountState { SavedAccountId = "alpha", AccountName = "Alpha" });

        // Act
        var result = _sut.LockForLaunch();

        // Assert
        _cacheProtector.Received().Lock(@"C:\Game\WTF", "Alpha");
        result.ShouldBe(2);
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
    public void ForceRestoreCache_SeedsMissingCacheFilesFromActiveSavedAccount()
    {
        // Arrange
        _savedAccountCatalog.GetActiveAccount()
            .Returns(new ActiveAccountState { SavedAccountId = "hero", AccountName = "Test" });
        _savedAccountCatalog.GetById("hero")
            .Returns(
                new SavedAccountSummary
                {
                    Id = "hero",
                    AccountName = "Test",
                    RootPath = @"C:\Profiles\hero",
                    SnapshotPath = @"C:\Profiles\hero\Account\Test",
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
        _fileSystem.DirectoryExists(@"C:\Profiles\hero").Returns(true);

        var profileCacheFile = @"C:\Profiles\hero\Account\Test\cache.md5";
        _cacheProtector
            .CollectCacheFiles(@"C:\Profiles\hero", "Test")
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
    public void RestoreFromSaved_WhenNoActiveSavedAccount_LogsWarningAndReturns()
    {
        // Arrange
        _cacheProtector.IsLocked.Returns(true);
        var logMessages = new List<string>();
        _sut.Log += logMessages.Add;

        // Act
        _sut.RestoreFromSaved();

        // Assert
        _cacheProtector.Received().Unlock();
        _accountSwitchService.DidNotReceive().RestoreActiveAccount();
        logMessages.ShouldHaveSingleItem();
        logMessages[0].ShouldContain("No active saved account");
    }

    [Test]
    public void RestoreFromSaved_WhenActiveSavedAccountExists_UsesAccountSwitchService()
    {
        // Arrange
        _cacheProtector.IsLocked.Returns(true);
        _savedAccountCatalog.GetActiveAccount()
            .Returns(new ActiveAccountState { SavedAccountId = "alpha", AccountName = "Alpha" });

        // Act
        _sut.RestoreFromSaved();

        // Assert
        Received.InOrder(() =>
        {
            _cacheProtector.Unlock();
            _accountSwitchService.RestoreActiveAccount();
        });
    }

    [Test]
    public async Task SaveAccountAsync_WhenExistingSavedAccountAndVersioningEnabled_CreatesVersionThenSaves()
    {
        // Arrange
        var liveAccount = new WowAccount
        {
            AccountName = "Alpha",
            FolderPath = @"C:\Game\WTF\Account\Alpha",
        };
        var savePlan = new AccountSavePlan { AccountName = "Alpha", SaveAccountSettings = true };
        var savedAccount = new SavedAccountSummary
        {
            Id = "alpha",
            AccountName = "Alpha",
            RootPath = @"C:\Profiles\alpha",
            SnapshotPath = @"C:\Profiles\alpha\Account\Alpha",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        _fileSystem.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _fileSystem.DirectoryExists(savedAccount.RootPath).Returns(true);
        _savedAccountCatalog.FindByAccountName("Alpha").Returns(savedAccount);

        // Act
        await _sut.SaveAccountAsync(liveAccount, savePlan, versioningEnabled: true);

        // Assert
        Received.InOrder(() =>
        {
            _ = _versionService.CreateVersionAsync("alpha");
            _accountSnapshotSaveService.Save(liveAccount, savePlan);
        });
    }

    [Test]
    public async Task SaveAccountAsync_WhenVersioningDisabled_SavesWithoutCreatingVersion()
    {
        // Arrange
        var liveAccount = new WowAccount
        {
            AccountName = "Alpha",
            FolderPath = @"C:\Game\WTF\Account\Alpha",
        };
        var savePlan = new AccountSavePlan { AccountName = "Alpha", SaveAccountSettings = true };
        _fileSystem.DirectoryExists(@"C:\Game\WTF").Returns(true);

        // Act
        await _sut.SaveAccountAsync(liveAccount, savePlan, versioningEnabled: false);

        // Assert
        await _versionService.DidNotReceive().CreateVersionAsync(Arg.Any<string>());
        _accountSnapshotSaveService.Received().Save(liveAccount, savePlan);
    }

    [Test]
    public async Task SaveAccountAsync_UnlocksCacheBeforeSaving()
    {
        // Arrange
        var liveAccount = new WowAccount
        {
            AccountName = "Alpha",
            FolderPath = @"C:\Game\WTF\Account\Alpha",
        };
        var savePlan = new AccountSavePlan { AccountName = "Alpha", SaveAccountSettings = true };
        _fileSystem.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _cacheProtector.IsLocked.Returns(true);

        // Act
        await _sut.SaveAccountAsync(liveAccount, savePlan, versioningEnabled: false);

        // Assert
        _cacheProtector.Received().Unlock();
    }

    [Test]
    public async Task SaveAccountAsync_WhenWtfMissing_SkipsAndLogs()
    {
        // Arrange
        var liveAccount = new WowAccount
        {
            AccountName = "Alpha",
            FolderPath = @"C:\Game\WTF\Account\Alpha",
        };
        var savePlan = new AccountSavePlan { AccountName = "Alpha", SaveAccountSettings = true };
        _fileSystem.DirectoryExists(@"C:\Game\WTF").Returns(false);
        var logMessages = new List<string>();
        _sut.Log += logMessages.Add;

        // Act
        var result = await _sut.SaveAccountAsync(liveAccount, savePlan, versioningEnabled: true);

        // Assert
        result.ShouldBeNull();
        _accountSnapshotSaveService.DidNotReceive().Save(Arg.Any<WowAccount>(), Arg.Any<AccountSavePlan>());
        logMessages.ShouldHaveSingleItem();
        logMessages[0].ShouldContain("WTF folder not found");
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
