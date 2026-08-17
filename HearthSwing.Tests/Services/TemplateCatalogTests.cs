using System.IO;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Models;
using HearthSwing.Models.Templates;
using HearthSwing.Services;
using NSubstitute;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class TemplateCatalogTests
{
    private IFixture _fixture = null!;
    private ISettingsService _settings = null!;
    private IFileSystem _fs = null!;
    private CapturingLogger<TemplateCatalog> _logger = null!;
    private TemplateCatalog _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _settings = _fixture.Freeze<ISettingsService>();
        _fs = _fixture.Freeze<IFileSystem>();
        _logger = new CapturingLogger<TemplateCatalog>();

        _settings.Current.Returns(new AppSettings { ProfilesPath = @"C:\Profiles" });
        _fs.DirectoryExists(Arg.Any<string>()).Returns(false);
        _fs.FileExists(Arg.Any<string>()).Returns(false);
        _fs.GetDirectories(Arg.Any<string>()).Returns([]);
        _fs.GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>()).Returns([]);

        _sut = new TemplateCatalog(_settings, _fs, _logger);
    }

    [Test]
    public void DiscoverTemplates_WhenStorageRootMissing_ReturnsEmptyList()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Profiles\.templates").Returns(false);

        // Act
        var result = _sut.DiscoverTemplates();

        // Assert
        result.ShouldBeEmpty();
    }

    [Test]
    public void DiscoverTemplates_WhenTemplatesExist_ReturnsSortedByName()
    {
        // Arrange
        const string warlockRoot = @"C:\Profiles\.templates\Warlock-TBC";
        const string mageRoot = @"C:\Profiles\.templates\Mage-Classic";
        _fs.DirectoryExists(@"C:\Profiles\.templates").Returns(true);
        _fs.GetDirectories(@"C:\Profiles\.templates").Returns([warlockRoot, mageRoot]);
        StubMetadata(warlockRoot, "Warlock-TBC", "Warlock - TBC");
        StubMetadata(mageRoot, "Mage-Classic", "Mage - Classic");

        // Act
        var result = _sut.DiscoverTemplates();

        // Assert
        result.Count.ShouldBe(2);
        result[0].Name.ShouldBe("Mage - Classic");
        result[1].Name.ShouldBe("Warlock - TBC");
    }

    [Test]
    public void Create_WhenNew_CreatesFolderAndMetadata()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Profiles\.templates").Returns(false);

        // Act
        var result = _sut.Create(
            "Warlock - TBC",
            TemplateKind.Character,
            "MainAccount",
            "Firemaw",
            "Thrall"
        );

        // Assert
        result.Id.ShouldBe("Warlock---TBC");
        result.Name.ShouldBe("Warlock - TBC");
        result.Kind.ShouldBe(TemplateKind.Character);
        result.RootPath.ShouldBe(@"C:\Profiles\.templates\Warlock---TBC");
        result.SourceCharacterName.ShouldBe("Thrall");
        _fs.Received().CreateDirectory(@"C:\Profiles\.templates");
        _fs.Received().CreateDirectory(@"C:\Profiles\.templates\Warlock---TBC");
        _fs.Received()
            .WriteAllText(
                @"C:\Profiles\.templates\Warlock---TBC\template.json",
                Arg.Is<string>(json => json.Contains("Warlock - TBC") && json.Contains("Firemaw"))
            );
        _logger.HasInformation(m => m.Contains("Created template")).ShouldBeTrue();
    }

    [Test]
    public void Create_WhenIdCollision_AppendsNumericSuffix()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Profiles\.templates").Returns(true);
        _fs.DirectoryExists(@"C:\Profiles\.templates\Warlock").Returns(true);

        // Act
        var result = _sut.Create(
            "Warlock",
            TemplateKind.Character,
            "MainAccount",
            "Firemaw",
            "Thrall"
        );

        // Assert
        result.Id.ShouldBe("Warlock-2");
        result.RootPath.ShouldBe(@"C:\Profiles\.templates\Warlock-2");
    }

    [Test]
    public void GetById_WhenTemplateMissing_ReturnsNull()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Profiles\.templates\ghost").Returns(false);

        // Act
        var result = _sut.GetById("ghost");

        // Assert
        result.ShouldBeNull();
    }

    [Test]
    public void Rename_UpdatesMetadataName()
    {
        // Arrange
        const string root = @"C:\Profiles\.templates\Warlock-TBC";
        _fs.DirectoryExists(root).Returns(true);
        StubMetadata(root, "Warlock-TBC", "Warlock - TBC");

        // Act
        _sut.Rename("Warlock-TBC", "Affliction - TBC");

        // Assert
        _fs.Received()
            .WriteAllText(
                @"C:\Profiles\.templates\Warlock-TBC\template.json",
                Arg.Is<string>(json => json.Contains("Affliction - TBC"))
            );
        _logger.HasInformation(m => m.Contains("Renamed template")).ShouldBeTrue();
    }

    [Test]
    public void Rename_WhenTemplateMissing_Throws()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Profiles\.templates\ghost").Returns(true);
        _fs.FileExists(@"C:\Profiles\.templates\ghost\template.json").Returns(false);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => _sut.Rename("ghost", "New"));
    }

    [Test]
    public void Delete_RemovesTemplateAndHistoryFolders()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Profiles\.templates\Warlock-TBC").Returns(true);
        _fs.DirectoryExists(@"C:\Profiles\.history\template\Warlock-TBC").Returns(true);
        _fs.DirectoryExists(@"C:\Profiles\.template-versions\Warlock-TBC").Returns(true);

        // Act
        _sut.Delete("Warlock-TBC");

        // Assert
        _fs.Received().DeleteDirectory(@"C:\Profiles\.templates\Warlock-TBC", true);
        _fs.Received().DeleteDirectory(@"C:\Profiles\.history\template\Warlock-TBC", true);
        _fs.Received().DeleteDirectory(@"C:\Profiles\.template-versions\Warlock-TBC", true);
        _logger.HasInformation(m => m.Contains("Deleted template")).ShouldBeTrue();
    }

    private void StubMetadata(string rootPath, string id, string name)
    {
        var metadataPath = Path.Combine(rootPath, "template.json");
        var json = $$"""
            {
              "Id": "{{id}}",
              "Name": "{{name}}",
              "Kind": "Character",
              "SourceAccountName": "MainAccount",
              "SourceRealmName": "Firemaw",
              "SourceCharacterName": "Thrall",
              "CreatedAtUtc": "2026-05-06T10:00:00+00:00",
              "SchemaVersion": 1
            }
            """;
        _fs.FileExists(metadataPath).Returns(true);
        _fs.ReadAllText(metadataPath).Returns(json);
    }
}
