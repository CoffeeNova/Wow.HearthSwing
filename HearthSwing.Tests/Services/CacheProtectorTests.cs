using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class CacheProtectorTests
{
    private IFixture _fixture = null!;
    private IFileSystem _fs = null!;
    private IAppLogger _logger = null!;
    private CacheProtector _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _fs = _fixture.Freeze<IFileSystem>();
        _logger = _fixture.Freeze<IAppLogger>();

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
    public void Lock_WhenAlreadyLocked_DoesNothing()
    {
        // Arrange
        var wtfPath = @"C:\Game\WTF";
        _fs.DirectoryExists(wtfPath).Returns(true);
        _sut.Lock(wtfPath);
        _fs.ClearReceivedCalls();

        // Act
        _sut.Lock(wtfPath);

        // Assert
        _fs.DidNotReceive()
            .GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>());
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
        _logger.Received().Log(Arg.Is<string>(m => m.Contains("Warning") && m.Contains("locked")));
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
            .Received()
            .Log(Arg.Is<string>(m => m.Contains("Locked") && m.Contains("cache files")));
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
        _logger.ClearReceivedCalls();

        // Act
        _sut.Unlock();

        // Assert
        _logger.Received().Log(Arg.Is<string>(m => m.Contains("unlocked")));
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
            .Received()
            .Log(Arg.Is<string>(m => m.Contains("Snapshot") && m.Contains("1 files")));
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

        _logger.ClearReceivedCalls();

        // Act
        _sut.ForceRestore(wtfPath);

        // Assert
        _fs.Received().WriteAllBytes(cacheFile, backupData);
        _fs.Received().SetLastWriteTime(cacheFile, Arg.Any<DateTime>());
        _sut.IsLocked.ShouldBeTrue();
        _logger
            .Received()
            .Log(Arg.Is<string>(m => m.Contains("Force-restored") && m.Contains("/reload")));
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

        _logger.ClearReceivedCalls();

        // Act
        _sut.ForceRestore(wtfPath);

        // Assert
        _logger
            .Received()
            .Log(Arg.Is<string>(m => m.Contains("Warning") && m.Contains("could not restore")));
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
}
