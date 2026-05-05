using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Models;
using HearthSwing.Models.Profiles;
using HearthSwing.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class ProfileVersionServiceTests
{
    private IFixture _fixture = null!;
    private IFileSystem _fs = null!;
    private ISettingsService _settings = null!;
    private CapturingLogger<ProfileVersionService> _logger = null!;
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

        _logger = new CapturingLogger<ProfileVersionService>();
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
        _logger.HasWarning(m => m.Contains("not found")).ShouldBeTrue();
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
        _logger.HasInformation(m => m.Contains("Version") && m.Contains("created")).ShouldBeTrue();
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
        _logger.HasInformation(m => m.Contains("restored")).ShouldBeTrue();
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
        _logger.HasInformation(m => m.Contains("deleted")).ShouldBeTrue();
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

    // ─── Scope-aware (descriptor-based) versioning ─────────────────────────────

    [Test]
    public async Task CreateVersion_Descriptor_WhenSnapshotNotFound_LogsWarningAndSkips()
    {
        // Arrange
        var descriptor = new ProfileDescriptor
        {
            Id = "Ihar__character__Acc__Realm__CharA",
            LocalProfileId = "Ihar",
            Granularity = ProfileGranularity.PerCharacter,
            AccountName = "Acc",
            RealmName = "Realm",
            CharacterName = "CharA",
            SnapshotPath = @"C:\Profiles\Ihar__character__Acc__Realm__CharA",
        };

        _fs.DirectoryExists(descriptor.SnapshotPath).Returns(false);

        // Act
        await _sut.CreateVersionAsync(descriptor);

        // Assert
        _logger.HasWarning(m => m.Contains("not found")).ShouldBeTrue();
        await _archive
            .DidNotReceive()
            .CompressDirectoryAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task CreateVersion_Descriptor_WhenSnapshotExists_CompressesAndWritesMeta()
    {
        // Arrange
        var descriptor = new ProfileDescriptor
        {
            Id = "Ihar__character__Acc__Realm__CharA",
            LocalProfileId = "Ihar",
            Granularity = ProfileGranularity.PerCharacter,
            AccountName = "Acc",
            RealmName = "Realm",
            CharacterName = "CharA",
            SnapshotPath = @"C:\Profiles\Ihar__character__Acc__Realm__CharA",
        };

        _fs.DirectoryExists(descriptor.SnapshotPath).Returns(true);

        // Act
        await _sut.CreateVersionAsync(descriptor);

        // Assert — archive created under descriptor's version folder
        await _archive
            .Received()
            .CompressDirectoryAsync(
                descriptor.SnapshotPath,
                Arg.Is<string>(s =>
                    s.Contains(".versions")
                    && s.Contains(descriptor.Id)
                    && s.EndsWith(".tar.gz")
                ),
                Arg.Any<CancellationToken>()
            );

        // Meta JSON sidecar written
        _fs.Received()
            .WriteAllText(
                Arg.Is<string>(s => s.Contains(descriptor.Id) && s.EndsWith(".meta.json")),
                Arg.Is<string>(s => s.Contains("Ihar") && s.Contains("PerCharacter"))
            );

        _logger.HasInformation(m => m.Contains("created") && m.Contains(descriptor.Id)).ShouldBeTrue();
    }

    [Test]
    public void GetVersions_Descriptor_WhenNoVersionsDirectory_ReturnsEmptyList()
    {
        // Arrange
        var descriptor = new ProfileDescriptor
        {
            Id = "Ihar__account__Acc",
            LocalProfileId = "Ihar",
            Granularity = ProfileGranularity.PerAccount,
            AccountName = "Acc",
            SnapshotPath = @"C:\Profiles\Ihar__account__Acc",
        };

        _fs.DirectoryExists(@"C:\Profiles\.versions\Ihar__account__Acc").Returns(false);

        // Act
        var result = _sut.GetVersions(descriptor);

        // Assert
        result.ShouldBeEmpty();
    }

    [Test]
    public void GetVersions_Descriptor_WithMetaSidecar_PopulatesScopeFields()
    {
        // Arrange
        var descriptor = new ProfileDescriptor
        {
            Id = "Ihar__account__Acc",
            LocalProfileId = "Ihar",
            Granularity = ProfileGranularity.PerAccount,
            AccountName = "Acc",
            SnapshotPath = @"C:\Profiles\Ihar__account__Acc",
        };

        const string versionId = "20260101_100000";
        const string versionDir = @"C:\Profiles\.versions\Ihar__account__Acc";
        var archivePath = $@"{versionDir}\{versionId}.tar.gz";
        var metaPath = $@"{versionDir}\{versionId}.meta.json";
        const string metaJson = """{"LocalProfileId":"Ihar","Granularity":"PerAccount","AccountName":"Acc","RealmName":null,"CharacterName":null}""";

        _fs.DirectoryExists(versionDir).Returns(true);
        _fs.GetFiles(versionDir, "*.tar.gz", SearchOption.TopDirectoryOnly).Returns([archivePath]);
        _fs.FileExists(metaPath).Returns(true);
        _fs.ReadAllText(metaPath).Returns(metaJson);

        // Act
        var result = _sut.GetVersions(descriptor);

        // Assert
        result.Count.ShouldBe(1);
        result[0].LocalProfileId.ShouldBe("Ihar");
        result[0].Granularity.ShouldBe(ProfileGranularity.PerAccount);
        result[0].AccountName.ShouldBe("Acc");
    }
}
