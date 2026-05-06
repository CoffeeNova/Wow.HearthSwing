using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class CacheProtectorTests
{
    private IFixture _fixture = null!;
    private IFileSystem _fs = null!;
    private CapturingLogger<CacheProtector> _logger = null!;
    private CacheProtector _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _fs = _fixture.Freeze<IFileSystem>();
        _logger = new CapturingLogger<CacheProtector>();

        _fs.FileExists(Arg.Any<string>()).Returns(false);
        _fs.DirectoryExists(Arg.Any<string>()).Returns(false);
        _fs.GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>()).Returns([]);

        _sut = new CacheProtector(_fs, _logger);
    }

    [TearDown]
    public void TearDown()
    {
        _sut.Dispose();
    }

    [Test]
    public void CollectCacheFiles_WhenWtfDoesNotExist_ReturnsEmptyList()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(false);

        // Act
        var result = _sut.CollectCacheFiles(@"C:\Game\WTF");

        // Assert
        result.ShouldBeEmpty();
    }

    [Test]
    public void CollectCacheFiles_WhenCacheFilesExist_ReturnsMatchingFiles()
    {
        // Arrange
        var wtfPath = @"C:\Game\WTF";
        _fs.DirectoryExists(wtfPath).Returns(true);
        _fs.GetFiles(wtfPath, "bindings-cache.wtf", SearchOption.AllDirectories)
            .Returns([@"C:\Game\WTF\Account\X\bindings-cache.wtf"]);
        _fs.GetFiles(wtfPath, "config-cache.wtf", SearchOption.AllDirectories)
            .Returns([@"C:\Game\WTF\Account\X\config-cache.wtf"]);

        // Act
        var result = _sut.CollectCacheFiles(wtfPath);

        // Assert
        result.Count.ShouldBe(2);
        result.ShouldContain(@"C:\Game\WTF\Account\X\bindings-cache.wtf");
        result.ShouldContain(@"C:\Game\WTF\Account\X\config-cache.wtf");
    }

    [Test]
    public void CollectCacheFiles_WhenDuplicatesExist_DeduplicatesByPath()
    {
        // Arrange
        var wtfPath = @"C:\Game\WTF";
        var filePath = @"C:\Game\WTF\Account\X\bindings-cache.wtf";
        _fs.DirectoryExists(wtfPath).Returns(true);
        _fs.GetFiles(wtfPath, "bindings-cache.wtf", SearchOption.AllDirectories)
            .Returns([filePath, filePath]);

        // Act
        var result = _sut.CollectCacheFiles(wtfPath);

        // Assert
        result.Count.ShouldBe(1);
    }

    [Test]
    public void CollectCacheFiles_WhenUnauthorizedAccessException_SkipsAndContinues()
    {
        // Arrange
        var wtfPath = @"C:\Game\WTF";
        _fs.DirectoryExists(wtfPath).Returns(true);
        _fs.GetFiles(wtfPath, "bindings-cache.wtf", SearchOption.AllDirectories)
            .Throws(new UnauthorizedAccessException("no access"));
        _fs.GetFiles(wtfPath, "config-cache.wtf", SearchOption.AllDirectories)
            .Returns([@"C:\Game\WTF\config-cache.wtf"]);

        // Act
        var result = _sut.CollectCacheFiles(wtfPath);

        // Assert
        result.Count.ShouldBe(1);
    }

    [Test]
    public void Lock_WhenNotLocked_SetsIsLockedTrue()
    {
        // Arrange
        var wtfPath = @"C:\Game\WTF";
        _fs.DirectoryExists(wtfPath).Returns(true);

        // Act
        _sut.Lock(wtfPath);

        // Assert
        _sut.IsLocked.ShouldBeTrue();
    }

    [Test]
    public void Lock_WhenAlreadyLocked_RefreshesProtectionForTheNewPath()
    {
        // Arrange
        var firstWtfPath = @"C:\Game\WTF";
        var secondWtfPath = @"D:\OtherGame\WTF";
        var firstCacheFile = @"C:\Game\WTF\Account\X\bindings-cache.wtf";
        var secondCacheFile = @"D:\OtherGame\WTF\Account\Y\bindings-cache.wtf";

        _fs.DirectoryExists(firstWtfPath).Returns(true);
        _fs.DirectoryExists(secondWtfPath).Returns(true);
        _fs.GetFiles(firstWtfPath, "bindings-cache.wtf", SearchOption.AllDirectories)
            .Returns([firstCacheFile]);
        _fs.GetFiles(secondWtfPath, "bindings-cache.wtf", SearchOption.AllDirectories)
            .Returns([secondCacheFile]);
        _fs.ReadAllBytes(firstCacheFile).Returns([1]);
        _fs.ReadAllBytes(secondCacheFile).Returns([2]);
        _fs.FileExists(firstCacheFile).Returns(true);
        _fs.FileExists(secondCacheFile).Returns(true);
        _fs.GetAttributes(firstCacheFile).Returns(FileAttributes.Normal, FileAttributes.ReadOnly);
        _fs.GetAttributes(secondCacheFile).Returns(FileAttributes.Normal);

        _sut.Lock(firstWtfPath);
        _fs.ClearReceivedCalls();

        // Act
        _sut.Lock(secondWtfPath);

        // Assert
        _fs.Received().ReadAllBytes(secondCacheFile);
        _fs.Received()
            .SetAttributes(
                firstCacheFile,
                Arg.Is<FileAttributes>(attributes => (attributes & FileAttributes.ReadOnly) == 0)
            );
        _sut.ProtectedFileCount.ShouldBe(1);
    }

    [Test]
    public void Lock_BacksUpAndProtectsFiles()
    {
        // Arrange
        var wtfPath = @"C:\Game\WTF";
        var cacheFile = @"C:\Game\WTF\Account\X\bindings-cache.wtf";
        _fs.DirectoryExists(wtfPath).Returns(true);
        _fs.GetFiles(wtfPath, "bindings-cache.wtf", SearchOption.AllDirectories)
            .Returns([cacheFile]);
        _fs.ReadAllBytes(cacheFile).Returns([1, 2, 3]);
        _fs.FileExists(cacheFile).Returns(true);
        _fs.GetAttributes(cacheFile).Returns(FileAttributes.Normal);

        // Act
        _sut.Lock(wtfPath);

        // Assert
        _fs.Received().ReadAllBytes(cacheFile);
        _fs.Received().SetLastWriteTime(cacheFile, Arg.Any<DateTime>());
        _fs.Received()
            .SetAttributes(
                cacheFile,
                Arg.Is<FileAttributes>(a => (a & FileAttributes.ReadOnly) != 0)
            );
        _sut.ProtectedFileCount.ShouldBe(1);
    }

    [Test]
    public void Lock_WhenBackupFails_LogsWarningAndContinues()
    {
        // Arrange
        var wtfPath = @"C:\Game\WTF";
        var badFile = @"C:\Game\WTF\bad-cache.wtf";
        var goodFile = @"C:\Game\WTF\config-cache.wtf";
        _fs.DirectoryExists(wtfPath).Returns(true);
        _fs.GetFiles(wtfPath, "bindings-cache.wtf", SearchOption.AllDirectories).Returns([badFile]);
        _fs.GetFiles(wtfPath, "config-cache.wtf", SearchOption.AllDirectories).Returns([goodFile]);
        _fs.ReadAllBytes(badFile).Throws(new IOException("locked"));
        _fs.ReadAllBytes(goodFile).Returns([4, 5]);
        _fs.FileExists(goodFile).Returns(true);
        _fs.GetAttributes(goodFile).Returns(FileAttributes.Normal);

        // Act
        _sut.Lock(wtfPath);

        // Assert
        _logger.HasWarning(m => m.Contains("Could not back up") && m.Contains("locked")).ShouldBeTrue();
        _sut.ProtectedFileCount.ShouldBe(1);
    }

    [Test]
    public void Lock_LogsProtectedFileCount()
    {
        // Arrange
        var wtfPath = @"C:\Game\WTF";
        _fs.DirectoryExists(wtfPath).Returns(true);

        // Act
        _sut.Lock(wtfPath);

        // Assert
        _logger
            .HasInformation(m => m.Contains("Locked") && m.Contains("cache files")).ShouldBeTrue();
    }

    [Test]
    public void Unlock_WhenLocked_SetsIsLockedFalseAndClearsBackups()
    {
        // Arrange
        var wtfPath = @"C:\Game\WTF";
        _fs.DirectoryExists(wtfPath).Returns(true);
        _sut.Lock(wtfPath);

        // Act
        _sut.Unlock();

        // Assert
        _sut.IsLocked.ShouldBeFalse();
        _sut.ProtectedFileCount.ShouldBe(0);
    }

    [Test]
    public void Unlock_WhenLocked_RemovesReadOnlyFromBackedUpFiles()
    {
        // Arrange
        var wtfPath = @"C:\Game\WTF";
        var cacheFile = @"C:\Game\WTF\Account\X\bindings-cache.wtf";
        _fs.DirectoryExists(wtfPath).Returns(true);
        _fs.GetFiles(wtfPath, "bindings-cache.wtf", SearchOption.AllDirectories)
            .Returns([cacheFile]);
        _fs.ReadAllBytes(cacheFile).Returns([1]);
        _fs.FileExists(cacheFile).Returns(true);
        _fs.GetAttributes(cacheFile).Returns(FileAttributes.Normal);
        _sut.Lock(wtfPath);

        _fs.ClearReceivedCalls();
        _fs.FileExists(cacheFile).Returns(true);
        _fs.GetAttributes(cacheFile).Returns(FileAttributes.ReadOnly);

        // Act
        _sut.Unlock();

        // Assert
        _fs.Received()
            .SetAttributes(
                cacheFile,
                Arg.Is<FileAttributes>(a => (a & FileAttributes.ReadOnly) == 0)
            );
    }

    [Test]
    public void Unlock_WhenNotLocked_DoesNothing()
    {
        // Act
        _sut.Unlock();

        // Assert
        _sut.IsLocked.ShouldBeFalse();
    }

    [Test]
    public void Unlock_LogsUnlocked()
    {
        // Arrange
        var wtfPath = @"C:\Game\WTF";
        _fs.DirectoryExists(wtfPath).Returns(true);
        _sut.Lock(wtfPath);
        // Act
        _sut.Unlock();

        // Assert
        _logger.HasInformation(m => m.Contains("unlocked")).ShouldBeTrue();
    }

    [Test]
    public void ForceRestore_WhenNoBackups_SnapshotsCurrentState()
    {
        // Arrange
        var wtfPath = @"C:\Game\WTF";
        var cacheFile = @"C:\Game\WTF\Account\X\config-cache.wtf";
        _fs.DirectoryExists(wtfPath).Returns(true);
        _fs.GetFiles(wtfPath, "config-cache.wtf", SearchOption.AllDirectories).Returns([cacheFile]);
        _fs.ReadAllBytes(cacheFile).Returns([10, 20]);

        // Act
        _sut.ForceRestore(wtfPath);

        // Assert
        _logger
            .HasInformation(m => m.Contains("Snapshot") && m.Contains("1 files")).ShouldBeTrue();
        _sut.ProtectedFileCount.ShouldBe(1);
    }

    [Test]
    public void ForceRestore_WhenBackupsExist_RestoresAndRelocks()
    {
        // Arrange
        var wtfPath = @"C:\Game\WTF";
        var cacheFile = @"C:\Game\WTF\Account\X\bindings-cache.wtf";
        var backupData = new byte[] { 1, 2, 3 };
        _fs.DirectoryExists(wtfPath).Returns(true);
        _fs.GetFiles(wtfPath, "bindings-cache.wtf", SearchOption.AllDirectories)
            .Returns([cacheFile]);
        _fs.ReadAllBytes(cacheFile).Returns(backupData);
        _fs.FileExists(cacheFile).Returns(true);
        _fs.GetAttributes(cacheFile).Returns(FileAttributes.Normal);

        _sut.Lock(wtfPath);
        _fs.ClearReceivedCalls();
        _fs.FileExists(cacheFile).Returns(true);
        _fs.GetAttributes(cacheFile).Returns(FileAttributes.ReadOnly);
        _fs.DirectoryExists(wtfPath).Returns(true);

        // Act
        _sut.ForceRestore(wtfPath);

        // Assert
        _fs.Received().WriteAllBytes(cacheFile, backupData);
        _fs.Received().SetLastWriteTime(cacheFile, Arg.Any<DateTime>());
        _sut.IsLocked.ShouldBeTrue();
        _logger
            .HasInformation(m => m.Contains("Force-restored") && m.Contains("/reload")).ShouldBeTrue();
    }

    [Test]
    public void ForceRestore_WhenRestoreFails_LogsWarningAndContinues()
    {
        // Arrange
        var wtfPath = @"C:\Game\WTF";
        var cacheFile = @"C:\Game\WTF\Account\X\bindings-cache.wtf";
        _fs.DirectoryExists(wtfPath).Returns(true);
        _fs.GetFiles(wtfPath, "bindings-cache.wtf", SearchOption.AllDirectories)
            .Returns([cacheFile]);
        _fs.ReadAllBytes(cacheFile).Returns([1]);
        _fs.FileExists(cacheFile).Returns(true);
        _fs.GetAttributes(cacheFile).Returns(FileAttributes.Normal);
        _sut.Lock(wtfPath);

        _fs.ClearReceivedCalls();
        _fs.DirectoryExists(wtfPath).Returns(true);
        _fs.FileExists(cacheFile).Returns(true);
        _fs.GetAttributes(cacheFile).Returns(FileAttributes.ReadOnly);
        // SetAttributes for removing read-only throws
        _fs.When(x =>
                x.SetAttributes(
                    cacheFile,
                    Arg.Is<FileAttributes>(a => (a & FileAttributes.ReadOnly) == 0)
                )
            )
            .Do(_ => throw new IOException("locked by process"));

        // Act
        _sut.ForceRestore(wtfPath);

        // Assert
        _logger
            .HasWarning(m => m.Contains("Could not restore")).ShouldBeTrue();
    }

    [Test]
    public void Dispose_WhenLocked_Unlocks()
    {
        // Arrange
        var wtfPath = @"C:\Game\WTF";
        _fs.DirectoryExists(wtfPath).Returns(true);
        _sut.Lock(wtfPath);

        // Act
        _sut.Dispose();

        // Assert
        _sut.IsLocked.ShouldBeFalse();
    }

    [Test]
    public void Dispose_WhenNotLocked_DoesNotThrow()
    {
        // Act & Assert
        Should.NotThrow(() => _sut.Dispose());
    }

    [Test]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        // Act & Assert
        _sut.Dispose();
        Should.NotThrow(() => _sut.Dispose());
    }

    // --- scope-aware CollectCacheFiles ---

    [Test]
    public void CollectCacheFiles_WhenPerAccount_OnlySearchesAccountSubtree()
    {
        // Arrange
        const string wtfPath = @"C:\Game\WTF";
        const string accountPath = @"C:\Game\WTF\Account\MyAccount";
        _fs.DirectoryExists(accountPath).Returns(true);
        _fs.GetFiles(accountPath, "bindings-cache.wtf", SearchOption.AllDirectories)
            .Returns([@"C:\Game\WTF\Account\MyAccount\bindings-cache.wtf"]);

        // Act
        var result = _sut.CollectCacheFiles(wtfPath, "MyAccount");

        // Assert
        result.ShouldContain(@"C:\Game\WTF\Account\MyAccount\bindings-cache.wtf");
        // Should never search the whole WTF root
        _fs.DidNotReceive().GetFiles(wtfPath, Arg.Any<string>(), Arg.Any<SearchOption>());
    }

    [Test]
    public void CollectCacheFiles_WhenScopedToAccount_DoesNotCollectSiblingAccountFiles()
    {
        // Arrange
        const string wtfPath = @"C:\Game\WTF";
        const string accountPath = @"C:\Game\WTF\Account\MyAccount";
        const string siblingAccountPath = @"C:\Game\WTF\Account\SiblingAccount";

        _fs.DirectoryExists(accountPath).Returns(true);
        _fs.DirectoryExists(siblingAccountPath).Returns(true);

        // Act
        _sut.CollectCacheFiles(wtfPath, "MyAccount");

        // Assert — sibling account path is never queried
        _fs.DidNotReceive().GetFiles(siblingAccountPath, Arg.Any<string>(), Arg.Any<SearchOption>());
    }

    // --- scope-aware Lock ---

    [Test]
    public void Lock_WhenPerAccount_OnlyProtectsFilesUnderAccountFolder()
    {
        // Arrange
        const string wtfPath = @"C:\Game\WTF";
        const string accountPath = @"C:\Game\WTF\Account\MyAccount";
        const string accountFile = @"C:\Game\WTF\Account\MyAccount\bindings-cache.wtf";
        _fs.DirectoryExists(accountPath).Returns(true);
        _fs.GetFiles(accountPath, "bindings-cache.wtf", SearchOption.AllDirectories)
            .Returns([accountFile]);
        _fs.ReadAllBytes(accountFile).Returns([1]);
        _fs.FileExists(accountFile).Returns(true);
        _fs.GetAttributes(accountFile).Returns(FileAttributes.Normal);

        // Act
        _sut.Lock(wtfPath, "MyAccount");

        // Assert
        _sut.ProtectedFileCount.ShouldBe(1);
        _fs.Received().ReadAllBytes(accountFile);
        _fs.DidNotReceive().GetFiles(wtfPath, Arg.Any<string>(), Arg.Any<SearchOption>());
    }

    [Test]
    public void Lock_WhenSwitchingBetweenAccounts_ClearsOldAccountBackupsAndProtectsNew()
    {
        // Arrange
        const string wtfPath = @"C:\Game\WTF";
        const string accountAPath = @"C:\Game\WTF\Account\AccountA";
        const string accountBPath = @"C:\Game\WTF\Account\AccountB";
        const string fileA = @"C:\Game\WTF\Account\AccountA\bindings-cache.wtf";
        const string fileB = @"C:\Game\WTF\Account\AccountB\bindings-cache.wtf";

        _fs.DirectoryExists(accountAPath).Returns(true);
        _fs.DirectoryExists(accountBPath).Returns(true);
        _fs.GetFiles(accountAPath, "bindings-cache.wtf", SearchOption.AllDirectories)
            .Returns([fileA]);
        _fs.GetFiles(accountBPath, "bindings-cache.wtf", SearchOption.AllDirectories)
            .Returns([fileB]);
        _fs.ReadAllBytes(fileA).Returns([1]);
        _fs.ReadAllBytes(fileB).Returns([2]);
        _fs.FileExists(fileA).Returns(true);
        _fs.FileExists(fileB).Returns(true);
        _fs.GetAttributes(Arg.Any<string>()).Returns(FileAttributes.Normal);

        _sut.Lock(wtfPath, "AccountA");
        _fs.ClearReceivedCalls();

        // Act — switch to AccountB
        _sut.Lock(wtfPath, "AccountB");

        // Assert — old account A read-only attribute is removed
        _fs.Received()
            .SetAttributes(fileA, Arg.Is<FileAttributes>(a => (a & FileAttributes.ReadOnly) == 0));
        // New account B is backed up
        _fs.Received().ReadAllBytes(fileB);
        // Only account B is protected now
        _sut.ProtectedFileCount.ShouldBe(1);
    }

    [Test]
    public void Unlock_AfterAccountLock_ClearsScopeState()
    {
        // Arrange
        const string wtfPath = @"C:\Game\WTF";
        const string accountPath = @"C:\Game\WTF\Account\MyAccount";
        _fs.DirectoryExists(accountPath).Returns(true);
        _sut.Lock(wtfPath, "MyAccount");

        // Act
        _sut.Unlock();

        // Assert
        _sut.IsLocked.ShouldBeFalse();
        _sut.ProtectedFileCount.ShouldBe(0);
    }
}
