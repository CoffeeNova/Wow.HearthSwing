using System.IO;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Models;
using HearthSwing.Services;
using NSubstitute;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class LegacyDataCleanupServiceTests
{
    private IFixture _fixture = null!;
    private ISettingsService _settings = null!;
    private IFileSystem _fileSystem = null!;
    private CapturingLogger<LegacyDataCleanupService> _logger = null!;
    private LegacyDataCleanupService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _settings = _fixture.Freeze<ISettingsService>();
        _fileSystem = _fixture.Freeze<IFileSystem>();
        _logger = new CapturingLogger<LegacyDataCleanupService>();

        _settings.Current.Returns(new AppSettings { ProfilesPath = @"C:\Profiles" });
        _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(false);
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        _fileSystem.GetDirectories(Arg.Any<string>()).Returns([]);
        _fileSystem
            .GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>())
            .Returns([]);

        _sut = new LegacyDataCleanupService(_settings, _fileSystem, _logger);
    }

    [Test]
    public void Discover_WhenLegacyItemsExist_ReturnsLegacyFoldersAndMarkerFile()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\Profiles").Returns(true);
        _fileSystem
            .GetDirectories(@"C:\Profiles")
            .Returns([
                @"C:\Profiles\.templates",
                @"C:\Profiles\.history",
                @"C:\Profiles\.versions",
                @"C:\Profiles\.template-versions",
                @"C:\Profiles\acc1",
                @"C:\Profiles\acc2",
            ]);
        _fileSystem.FileExists(@"C:\Profiles\acc1\account.json").Returns(true);
        _fileSystem.FileExists(@"C:\Profiles\acc2\account.json").Returns(true);
        _fileSystem.DirectoryExists(@"C:\Profiles\acc1\Account").Returns(true);
        _fileSystem.DirectoryExists(@"C:\Profiles\acc2\Account").Returns(true);
        _fileSystem
            .GetDirectories(@"C:\Profiles\acc1\Account")
            .Returns([@"C:\Profiles\acc1\Account\acc1"]);
        _fileSystem
            .GetDirectories(@"C:\Profiles\acc2\Account")
            .Returns([@"C:\Profiles\acc2\Account\acc2"]);
        _fileSystem.DirectoryExists(@"C:\Profiles\acc1\Account\acc1\SavedVariables").Returns(true);
        _fileSystem.DirectoryExists(@"C:\Profiles\acc2\Account\acc2\SavedVariables").Returns(true);
        _fileSystem.FileExists(@"C:\Profiles\.active-account.json").Returns(true);

        // Act
        var result = _sut.Discover();

        // Assert
        result.Directories.ShouldBe([
            @"C:\Profiles\.template-versions",
            @"C:\Profiles\.versions",
            @"C:\Profiles\acc1",
            @"C:\Profiles\acc2",
        ]);
        result.Files.ShouldBe([@"C:\Profiles\.active-account.json"]);
    }

    [Test]
    public void Cleanup_WhenLegacyItemsExist_DeletesThem()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\Profiles").Returns(true);
        _fileSystem
            .GetDirectories(@"C:\Profiles")
            .Returns([@"C:\Profiles\.versions", @"C:\Profiles\acc1"]);
        _fileSystem.FileExists(@"C:\Profiles\acc1\account.json").Returns(true);
        _fileSystem.DirectoryExists(@"C:\Profiles\acc1\Account").Returns(true);
        _fileSystem
            .GetDirectories(@"C:\Profiles\acc1\Account")
            .Returns([@"C:\Profiles\acc1\Account\acc1"]);
        _fileSystem.DirectoryExists(@"C:\Profiles\acc1\Account\acc1\SavedVariables").Returns(true);
        _fileSystem.FileExists(@"C:\Profiles\.active-account.json").Returns(true);
        _fileSystem.DirectoryExists(@"C:\Profiles\.versions").Returns(true);
        _fileSystem.DirectoryExists(@"C:\Profiles\acc1").Returns(true);
        _fileSystem
            .GetFiles(@"C:\Profiles\.versions", "*", SearchOption.AllDirectories)
            .Returns([]);
        _fileSystem.GetFiles(@"C:\Profiles\acc1", "*", SearchOption.AllDirectories).Returns([]);
        _fileSystem
            .GetAttributes(@"C:\Profiles\.active-account.json")
            .Returns(FileAttributes.ReadOnly);

        // Act
        var result = _sut.Cleanup();

        // Assert
        result.TotalCount.ShouldBe(3);
        _fileSystem.Received().DeleteDirectory(@"C:\Profiles\.versions", true);
        _fileSystem.Received().DeleteDirectory(@"C:\Profiles\acc1", true);
        _fileSystem.Received().DeleteFile(@"C:\Profiles\.active-account.json");
        _logger
            .HasInformation(message => message.Contains("Removed 3 legacy storage item(s)."))
            .ShouldBeTrue();
    }

    [Test]
    public void Discover_WhenFolderDoesNotMatchLegacyShape_DoesNotIncludeIt()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\Profiles").Returns(true);
        _fileSystem.GetDirectories(@"C:\Profiles").Returns([@"C:\Profiles\notes"]);
        _fileSystem.DirectoryExists(@"C:\Profiles\notes\SavedVariables").Returns(false);
        _fileSystem.GetDirectories(@"C:\Profiles\notes").Returns([]);

        // Act
        var result = _sut.Discover();

        // Assert
        result.HasItems.ShouldBeFalse();
        result.Directories.ShouldBeEmpty();
        result.Files.ShouldBeEmpty();
    }

    [Test]
    public void Discover_WhenFolderIsUnrelatedNestedTree_DoesNotIncludeIt()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\Profiles").Returns(true);
        _fileSystem.GetDirectories(@"C:\Profiles").Returns([@"C:\Profiles\Exports"]);

        _fileSystem.FileExists(@"C:\Profiles\Exports\account.json").Returns(false);
        _fileSystem.DirectoryExists(@"C:\Profiles\Exports\Account").Returns(false);

        // Simulate nested folders that previously matched generic depth heuristic.
        _fileSystem.GetDirectories(@"C:\Profiles\Exports").Returns([@"C:\Profiles\Exports\2026"]);
        _fileSystem
            .GetDirectories(@"C:\Profiles\Exports\2026")
            .Returns([@"C:\Profiles\Exports\2026\July"]);

        // Act
        var result = _sut.Discover();

        // Assert
        result.HasItems.ShouldBeFalse();
        result.Directories.ShouldBeEmpty();
        result.Files.ShouldBeEmpty();
    }
}
