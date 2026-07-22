using System.IO;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Models;
using HearthSwing.Services;
using NSubstitute;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class TemplateVersionServiceTests
{
    private IFixture _fixture = null!;
    private IFileSystem _fs = null!;
    private ISettingsService _settings = null!;
    private CapturingLogger<TemplateVersionService> _logger = null!;
    private IArchiveService _archive = null!;
    private TemplateVersionService _sut = null!;

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

        _logger = new CapturingLogger<TemplateVersionService>();
        _archive = _fixture.Freeze<IArchiveService>();

        _sut = new TemplateVersionService(_fs, _settings, _logger, _archive);
    }

    [Test]
    public async Task CreateVersion_WhenTemplateFolderMissing_LogsWarningAndSkips()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Profiles\.templates\Warlock").Returns(false);

        // Act
        await _sut.CreateVersionAsync("Warlock");

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
    public async Task CreateVersion_WhenTemplateExists_CompressesToTemplateVersionsFolder()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Profiles\.templates\Warlock").Returns(true);

        // Act
        await _sut.CreateVersionAsync("Warlock");

        // Assert
        _fs.Received()
            .CreateDirectory(
                Arg.Is<string>(s => s.Contains(".template-versions") && s.Contains("Warlock"))
            );
        await _archive
            .Received()
            .CompressDirectoryAsync(
                @"C:\Profiles\.templates\Warlock",
                Arg.Is<string>(s =>
                    s.Contains(".template-versions")
                    && s.Contains("Warlock")
                    && s.EndsWith(".tar.gz")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public void GetVersions_WhenVersionsExist_ReturnsSortedDescByDate()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Profiles\.template-versions\Warlock").Returns(true);
        _fs.GetFiles(
                @"C:\Profiles\.template-versions\Warlock",
                "*.tar.gz",
                SearchOption.TopDirectoryOnly
            )
            .Returns([
                @"C:\Profiles\.template-versions\Warlock\20260101_100000.tar.gz",
                @"C:\Profiles\.template-versions\Warlock\20260115_120000.tar.gz",
            ]);

        // Act
        var result = _sut.GetVersions("Warlock");

        // Assert
        result.Count.ShouldBe(2);
        result[0].VersionId.ShouldBe("20260115_120000");
        result[1].VersionId.ShouldBe("20260101_100000");
    }

    [Test]
    public async Task RestoreVersion_DeletesExistingTemplateAndExtractsArchive()
    {
        // Arrange
        var version = new ProfileVersion
        {
            VersionId = "20260115_120000",
            ProfileId = "Warlock",
            CreatedAt = new DateTime(2026, 1, 15, 12, 0, 0),
            ArchivePath = @"C:\Profiles\.template-versions\Warlock\20260115_120000.tar.gz",
        };
        _fs.DirectoryExists(@"C:\Profiles\.templates\Warlock").Returns(true);

        // Act
        await _sut.RestoreVersionAsync(version);

        // Assert
        _fs.Received().DeleteDirectory(@"C:\Profiles\.templates\Warlock", true);
        _fs.Received().CreateDirectory(@"C:\Profiles\.templates\Warlock");
        await _archive
            .Received()
            .ExtractToDirectoryAsync(
                @"C:\Profiles\.template-versions\Warlock\20260115_120000.tar.gz",
                @"C:\Profiles\.templates\Warlock",
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public void PruneVersions_WhenOverLimit_DeletesOldestVersions()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Profiles\.template-versions\Warlock").Returns(true);
        _fs.GetFiles(
                @"C:\Profiles\.template-versions\Warlock",
                "*.tar.gz",
                SearchOption.TopDirectoryOnly
            )
            .Returns([
                @"C:\Profiles\.template-versions\Warlock\20260101_100000.tar.gz",
                @"C:\Profiles\.template-versions\Warlock\20260102_100000.tar.gz",
                @"C:\Profiles\.template-versions\Warlock\20260103_100000.tar.gz",
            ]);
        _fs.FileExists(Arg.Is<string>(s => s.Contains(".template-versions"))).Returns(true);

        // Act
        _sut.PruneVersions("Warlock", 2);

        // Assert
        _fs.Received()
            .DeleteFile(@"C:\Profiles\.template-versions\Warlock\20260101_100000.tar.gz");
        _fs.DidNotReceive()
            .DeleteFile(@"C:\Profiles\.template-versions\Warlock\20260103_100000.tar.gz");
    }
}
