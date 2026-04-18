using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Models;
using HearthSwing.Services;
using NSubstitute;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class ProfileVersionServiceTests
{
    private IFixture _fixture = null!;
    private IFileSystem _fs = null!;
    private ISettingsService _settings = null!;
    private IAppLogger _logger = null!;
    private IArchiveService _archive = null!;
    private ProfileVersionService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _fs = _fixture.Freeze<IFileSystem>();

        _fs.FileExists(Arg.Any<string>()).Returns(false);
        _fs.DirectoryExists(Arg.Any<string>()).Returns(false);
        _fs.GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>()).Returns([]);
        _fs.GetDirectories(Arg.Any<string>()).Returns([]);

        _settings = _fixture.Freeze<ISettingsService>();
        _settings.Current.Returns(
            new AppSettings { ProfilesPath = @"C:\Profiles", MaxVersionsPerProfile = 5 }
        );

        _logger = _fixture.Freeze<IAppLogger>();
        _archive = _fixture.Freeze<IArchiveService>();

        _sut = new ProfileVersionService(_fs, _settings, _logger, _archive);
    }

    [Test]
    public async Task CreateVersion_WhenProfileFolderNotFound_LogsWarningAndSkips()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Profiles\Alice").Returns(false);

        // Act
        await _sut.CreateVersionAsync("Alice");

        // Assert
        _logger.Received().Log(Arg.Is<string>(m => m.Contains("not found")));
        await _archive
            .DidNotReceive()
            .CompressDirectoryAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task CreateVersion_WhenProfileExists_CompressesProfileToArchive()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Profiles\Alice").Returns(true);

        // Act
        await _sut.CreateVersionAsync("Alice");

        // Assert
        _fs.Received()
            .CreateDirectory(Arg.Is<string>(s => s.Contains(".versions") && s.Contains("Alice")));
        await _archive
            .Received()
            .CompressDirectoryAsync(
                @"C:\Profiles\Alice",
                Arg.Is<string>(s =>
                    s.Contains(".versions") && s.Contains("Alice") && s.EndsWith(".tar.gz")
                ),
                Arg.Any<CancellationToken>()
            );
        _logger.Received().Log(Arg.Is<string>(m => m.Contains("Version") && m.Contains("created")));
    }

    [Test]
    public void GetVersions_WhenNoVersionsDirectory_ReturnsEmptyList()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Profiles\.versions\Alice").Returns(false);

        // Act
        var result = _sut.GetVersions("Alice");

        // Assert
        result.ShouldBeEmpty();
    }

    [Test]
    public void GetVersions_WhenVersionsExist_ReturnsSortedDescByDate()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Profiles\.versions\Alice").Returns(true);
        _fs.GetFiles(@"C:\Profiles\.versions\Alice", "*.tar.gz", SearchOption.TopDirectoryOnly)
            .Returns([
                @"C:\Profiles\.versions\Alice\20260101_100000.tar.gz",
                @"C:\Profiles\.versions\Alice\20260115_120000.tar.gz",
                @"C:\Profiles\.versions\Alice\20260110_080000.tar.gz",
            ]);

        // Act
        var result = _sut.GetVersions("Alice");

        // Assert
        result.Count.ShouldBe(3);
        result[0].VersionId.ShouldBe("20260115_120000");
        result[1].VersionId.ShouldBe("20260110_080000");
        result[2].VersionId.ShouldBe("20260101_100000");
    }

    [Test]
    public void GetVersions_IgnoresInvalidFileNames()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Profiles\.versions\Alice").Returns(true);
        _fs.GetFiles(@"C:\Profiles\.versions\Alice", "*.tar.gz", SearchOption.TopDirectoryOnly)
            .Returns([
                @"C:\Profiles\.versions\Alice\20260101_100000.tar.gz",
                @"C:\Profiles\.versions\Alice\not-a-timestamp.tar.gz",
            ]);

        // Act
        var result = _sut.GetVersions("Alice");

        // Assert
        result.Count.ShouldBe(1);
        result[0].VersionId.ShouldBe("20260101_100000");
    }

    [Test]
    public async Task RestoreVersion_DeletesExistingProfileAndExtractsArchive()
    {
        // Arrange
        var version = new ProfileVersion
        {
            VersionId = "20260115_120000",
            ProfileId = "Alice",
            CreatedAt = new DateTime(2026, 1, 15, 12, 0, 0),
            ArchivePath = @"C:\Profiles\.versions\Alice\20260115_120000.tar.gz",
        };

        _fs.DirectoryExists(@"C:\Profiles\Alice").Returns(true);
        _fs.GetFiles(@"C:\Profiles\Alice", "*", SearchOption.AllDirectories).Returns([]);

        // Act
        await _sut.RestoreVersionAsync(version);

        // Assert
        _fs.Received().DeleteDirectory(@"C:\Profiles\Alice", true);
        _fs.Received().CreateDirectory(@"C:\Profiles\Alice");
        await _archive
            .Received()
            .ExtractToDirectoryAsync(
                @"C:\Profiles\.versions\Alice\20260115_120000.tar.gz",
                @"C:\Profiles\Alice",
                Arg.Any<CancellationToken>()
            );
        _logger.Received().Log(Arg.Is<string>(m => m.Contains("restored")));
    }

    [Test]
    public void DeleteVersion_WhenArchiveExists_DeletesIt()
    {
        // Arrange
        var version = new ProfileVersion
        {
            VersionId = "20260115_120000",
            ProfileId = "Alice",
            CreatedAt = new DateTime(2026, 1, 15, 12, 0, 0),
            ArchivePath = @"C:\Profiles\.versions\Alice\20260115_120000.tar.gz",
        };

        _fs.FileExists(@"C:\Profiles\.versions\Alice\20260115_120000.tar.gz").Returns(true);

        // Act
        _sut.DeleteVersion(version);

        // Assert
        _fs.Received().DeleteFile(@"C:\Profiles\.versions\Alice\20260115_120000.tar.gz");
        _logger.Received().Log(Arg.Is<string>(m => m.Contains("deleted")));
    }

    [Test]
    public void DeleteVersion_WhenArchiveDoesNotExist_DoesNothing()
    {
        // Arrange
        var version = new ProfileVersion
        {
            VersionId = "20260115_120000",
            ProfileId = "Alice",
            CreatedAt = new DateTime(2026, 1, 15, 12, 0, 0),
            ArchivePath = @"C:\Profiles\.versions\Alice\20260115_120000.tar.gz",
        };

        _fs.FileExists(@"C:\Profiles\.versions\Alice\20260115_120000.tar.gz").Returns(false);

        // Act
        _sut.DeleteVersion(version);

        // Assert
        _fs.DidNotReceive().DeleteFile(Arg.Any<string>());
    }

    [Test]
    public void PruneVersions_WhenUnderLimit_DoesNotDeleteAny()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Profiles\.versions\Alice").Returns(true);
        _fs.GetFiles(@"C:\Profiles\.versions\Alice", "*.tar.gz", SearchOption.TopDirectoryOnly)
            .Returns([
                @"C:\Profiles\.versions\Alice\20260101_100000.tar.gz",
                @"C:\Profiles\.versions\Alice\20260102_100000.tar.gz",
            ]);

        // Act
        _sut.PruneVersions("Alice", 5);

        // Assert
        _fs.DidNotReceive().DeleteFile(Arg.Any<string>());
    }

    [Test]
    public void PruneVersions_WhenOverLimit_DeletesOldestVersions()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Profiles\.versions\Alice").Returns(true);
        _fs.GetFiles(@"C:\Profiles\.versions\Alice", "*.tar.gz", SearchOption.TopDirectoryOnly)
            .Returns([
                @"C:\Profiles\.versions\Alice\20260101_100000.tar.gz",
                @"C:\Profiles\.versions\Alice\20260102_100000.tar.gz",
                @"C:\Profiles\.versions\Alice\20260103_100000.tar.gz",
            ]);
        _fs.FileExists(Arg.Is<string>(s => s.Contains(".versions"))).Returns(true);

        // Act
        _sut.PruneVersions("Alice", 2);

        // Assert — oldest (20260101) should be deleted, two newest kept
        _fs.Received().DeleteFile(@"C:\Profiles\.versions\Alice\20260101_100000.tar.gz");
        _fs.DidNotReceive().DeleteFile(@"C:\Profiles\.versions\Alice\20260102_100000.tar.gz");
        _fs.DidNotReceive().DeleteFile(@"C:\Profiles\.versions\Alice\20260103_100000.tar.gz");
    }
}
