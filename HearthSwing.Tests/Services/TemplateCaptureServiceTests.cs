using System.IO;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Models.Templates;
using HearthSwing.Models.WoW;
using HearthSwing.Services;
using NSubstitute;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class TemplateCaptureServiceTests
{
    private IFixture _fixture = null!;
    private ITemplateCatalog _catalog = null!;
    private IFileSystem _fs = null!;
    private CapturingLogger<TemplateCaptureService> _logger = null!;
    private TemplateCaptureService _sut = null!;

    private const string AccountPath = @"C:\WTF\Account\MainAccount";
    private const string CharFolder = @"C:\WTF\Account\MainAccount\Firemaw\Thrall";
    private const string TemplateRoot = @"C:\Profiles\.templates\Warlock";

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _catalog = _fixture.Freeze<ITemplateCatalog>();
        _fs = _fixture.Freeze<IFileSystem>();
        _logger = new CapturingLogger<TemplateCaptureService>();

        _fs.DirectoryExists(Arg.Any<string>()).Returns(false);
        _fs.FileExists(Arg.Any<string>()).Returns(true);
        _fs.GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>()).Returns([]);
        _fs.ReadAllText(Arg.Any<string>()).Returns("name=\"Thrall\" realm=\"Firemaw\"");

        _sut = new TemplateCaptureService(
            _catalog,
            new TemplateTokenizer(),
            new TemplateFileClassifier(),
            _fs,
            _logger
        );
    }

    private static TemplateSummary Template(TemplateKind kind) =>
        new()
        {
            Id = "Warlock",
            Name = "Warlock - TBC",
            Kind = kind,
            RootPath = TemplateRoot,
            SourceAccountName = "MainAccount",
            SourceRealmName = kind == TemplateKind.Character ? "Firemaw" : null,
            SourceCharacterName = kind == TemplateKind.Character ? "Thrall" : null,
        };

    private WowCharacter SourceCharacter =>
        new()
        {
            AccountName = "MainAccount",
            RealmName = "Firemaw",
            CharacterName = "Thrall",
            FolderPath = CharFolder,
        };

    private WowAccount SourceAccount =>
        new() { AccountName = "MainAccount", FolderPath = AccountPath };

    private void StubCharacterCatalog()
    {
        var template = Template(TemplateKind.Character);
        _catalog
            .Create("Warlock - TBC", TemplateKind.Character, "MainAccount", "Firemaw", "Thrall")
            .Returns(template);
        _catalog.GetById("Warlock").Returns(template);
    }

    private void StubAccountCatalog()
    {
        var template = Template(TemplateKind.Account);
        _catalog
            .Create("Warlock - TBC", TemplateKind.Account, "MainAccount", null, null)
            .Returns(template);
        _catalog.GetById("Warlock").Returns(template);
    }

    [Test]
    public void CreateCharacterTemplate_TokenizesCharacterFilesUnderTokenFolders()
    {
        // Arrange
        StubCharacterCatalog();
        _fs.DirectoryExists(CharFolder).Returns(true);
        _fs.GetFiles(CharFolder, "*", SearchOption.AllDirectories)
            .Returns([@"C:\WTF\Account\MainAccount\Firemaw\Thrall\SavedVariables\CharAddon.lua"]);

        // Act
        _sut.CreateCharacterTemplate(SourceCharacter, "Warlock - TBC");

        // Assert
        _fs.Received()
            .WriteAllText(
                @"C:\Profiles\.templates\Warlock\Character\__REALM__\__CHAR__\SavedVariables\CharAddon.lua",
                "name=\"{{CHAR}}\" realm=\"{{REALM}}\""
            );
    }

    [Test]
    public void CreateCharacterTemplate_CopiesNonTokenizableFilesByteForByte()
    {
        // Arrange
        StubCharacterCatalog();
        _fs.DirectoryExists(CharFolder).Returns(true);
        _fs.GetFiles(CharFolder, "*", SearchOption.AllDirectories)
            .Returns([@"C:\WTF\Account\MainAccount\Firemaw\Thrall\cache.md5"]);

        // Act
        _sut.CreateCharacterTemplate(SourceCharacter, "Warlock - TBC");

        // Assert
        _fs.Received()
            .CopyFile(
                @"C:\WTF\Account\MainAccount\Firemaw\Thrall\cache.md5",
                @"C:\Profiles\.templates\Warlock\Character\__REALM__\__CHAR__\cache.md5"
            );
        _fs.DidNotReceive()
            .WriteAllText(Arg.Is<string>(s => s.EndsWith("cache.md5")), Arg.Any<string>());
    }

    [Test]
    public void CreateCharacterTemplate_UpdatesLastUpdatedAndReturnsSummary()
    {
        // Arrange
        StubCharacterCatalog();
        _fs.DirectoryExists(CharFolder).Returns(true);

        // Act
        var result = _sut.CreateCharacterTemplate(SourceCharacter, "Warlock - TBC");

        // Assert
        result.Id.ShouldBe("Warlock");
        _catalog.Received().UpdateLastUpdated("Warlock", Arg.Any<DateTimeOffset>());
        _logger.HasInformation(m => m.Contains("Captured character template")).ShouldBeTrue();
    }

    [Test]
    public void CreateCharacterTemplate_CopiesSharedAccountFilesIntoSharedFolder()
    {
        // Arrange
        StubCharacterCatalog();
        _fs.DirectoryExists(CharFolder).Returns(true);
        _fs.DirectoryExists(AccountPath).Returns(true);
        _fs.DirectoryExists(Path.Combine(AccountPath, "SavedVariables")).Returns(true);
        _fs.GetFiles(AccountPath, "*", SearchOption.TopDirectoryOnly).Returns([]);
        _fs.GetFiles(Path.Combine(AccountPath, "SavedVariables"), "*", SearchOption.AllDirectories)
            .Returns([@"C:\WTF\Account\MainAccount\SavedVariables\Addon.lua"]);

        // Act
        _sut.CreateCharacterTemplate(SourceCharacter, "Warlock - TBC");

        // Assert
        _fs.Received()
            .CopyFile(
                @"C:\WTF\Account\MainAccount\SavedVariables\Addon.lua",
                @"C:\Profiles\.templates\Warlock\Shared\SavedVariables\Addon.lua"
            );
    }

    [Test]
    public void CreateAccountTemplate_CopiesAccountFilesUnderAccountFolderWithoutTokenizing()
    {
        // Arrange
        StubAccountCatalog();
        _fs.DirectoryExists(AccountPath).Returns(true);
        _fs.DirectoryExists(Path.Combine(AccountPath, "SavedVariables")).Returns(true);
        _fs.GetFiles(AccountPath, "*", SearchOption.TopDirectoryOnly).Returns([]);
        _fs.GetFiles(Path.Combine(AccountPath, "SavedVariables"), "*", SearchOption.AllDirectories)
            .Returns([@"C:\WTF\Account\MainAccount\SavedVariables\Addon.lua"]);

        // Act
        _sut.CreateAccountTemplate(SourceAccount, "Warlock - TBC");

        // Assert
        _fs.Received()
            .CopyFile(
                @"C:\WTF\Account\MainAccount\SavedVariables\Addon.lua",
                @"C:\Profiles\.templates\Warlock\Account\SavedVariables\Addon.lua"
            );
        _fs.DidNotReceive().WriteAllText(Arg.Any<string>(), Arg.Any<string>());
        _logger.HasInformation(m => m.Contains("Captured account template")).ShouldBeTrue();
    }

    [Test]
    public void CreateCharacterTemplate_WhenTemplateNameBlank_Throws()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => _sut.CreateCharacterTemplate(SourceCharacter, "  "));
    }

    [Test]
    public void CreateAccountTemplate_WhenTemplateNameBlank_Throws()
    {
        // Act & Assert
        Should.Throw<ArgumentException>(() => _sut.CreateAccountTemplate(SourceAccount, "  "));
    }

    [Test]
    public void UpdateCharacterTemplate_ClearsContentAndReCapturesInPlace()
    {
        // Arrange
        var template = Template(TemplateKind.Character);
        _catalog.GetById("Warlock").Returns(template);
        _fs.DirectoryExists(TemplateRoot + @"\Character").Returns(true);
        _fs.DirectoryExists(TemplateRoot + @"\Shared").Returns(true);
        _fs.DirectoryExists(CharFolder).Returns(true);
        _fs.DirectoryExists(AccountPath).Returns(true);
        _fs.DirectoryExists(Path.Combine(AccountPath, "SavedVariables")).Returns(true);
        _fs.GetFiles(TemplateRoot + @"\Character", "*", SearchOption.AllDirectories).Returns([]);
        _fs.GetFiles(TemplateRoot + @"\Shared", "*", SearchOption.AllDirectories).Returns([]);
        _fs.GetFiles(CharFolder, "*", SearchOption.AllDirectories)
            .Returns([@"C:\WTF\Account\MainAccount\Firemaw\Thrall\SavedVariables\CharAddon.lua"]);
        _fs.GetFiles(AccountPath, "*", SearchOption.TopDirectoryOnly).Returns([]);
        _fs.GetFiles(Path.Combine(AccountPath, "SavedVariables"), "*", SearchOption.AllDirectories)
            .Returns([@"C:\WTF\Account\MainAccount\SavedVariables\Addon.lua"]);

        // Act
        _sut.UpdateCharacterTemplate(template, SourceCharacter);

        // Assert
        _fs.Received().DeleteDirectory(TemplateRoot + @"\Character", true);
        _fs.Received().DeleteDirectory(TemplateRoot + @"\Shared", true);
        _fs.Received()
            .WriteAllText(
                @"C:\Profiles\.templates\Warlock\Character\__REALM__\__CHAR__\SavedVariables\CharAddon.lua",
                "name=\"{{CHAR}}\" realm=\"{{REALM}}\""
            );
        _fs.Received()
            .CopyFile(
                @"C:\WTF\Account\MainAccount\SavedVariables\Addon.lua",
                @"C:\Profiles\.templates\Warlock\Shared\SavedVariables\Addon.lua"
            );
        _catalog.Received().UpdateLastUpdated("Warlock", Arg.Any<DateTimeOffset>());
        _logger.HasInformation(m => m.Contains("Updated character template")).ShouldBeTrue();
    }

    [Test]
    public void UpdateAccountTemplate_ClearsContentAndReCapturesInPlace()
    {
        // Arrange
        var template = Template(TemplateKind.Account);
        _catalog.GetById("Warlock").Returns(template);
        _fs.DirectoryExists(TemplateRoot + @"\Account").Returns(true);
        _fs.DirectoryExists(AccountPath).Returns(true);
        _fs.DirectoryExists(Path.Combine(AccountPath, "SavedVariables")).Returns(true);
        _fs.GetFiles(TemplateRoot + @"\Account", "*", SearchOption.AllDirectories).Returns([]);
        _fs.GetFiles(AccountPath, "*", SearchOption.TopDirectoryOnly).Returns([]);
        _fs.GetFiles(Path.Combine(AccountPath, "SavedVariables"), "*", SearchOption.AllDirectories)
            .Returns([@"C:\WTF\Account\MainAccount\SavedVariables\Addon.lua"]);

        // Act
        _sut.UpdateAccountTemplate(template, SourceAccount);

        // Assert
        _fs.Received().DeleteDirectory(TemplateRoot + @"\Account", true);
        _fs.Received()
            .CopyFile(
                @"C:\WTF\Account\MainAccount\SavedVariables\Addon.lua",
                @"C:\Profiles\.templates\Warlock\Account\SavedVariables\Addon.lua"
            );
        _catalog.Received().UpdateLastUpdated("Warlock", Arg.Any<DateTimeOffset>());
        _logger.HasInformation(m => m.Contains("Updated account template")).ShouldBeTrue();
    }
}
