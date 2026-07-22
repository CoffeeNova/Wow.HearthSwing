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
public class TemplateApplyServiceTests
{
    private IFixture _fixture = null!;
    private IDirectoryReplacer _replacer = null!;
    private IFileSystem _fs = null!;
    private CapturingLogger<TemplateApplyService> _logger = null!;
    private TemplateApplyService _sut = null!;

    private const string TemplateRoot = @"C:\Profiles\.templates\Warlock";
    private const string TemplateCharRoot =
        @"C:\Profiles\.templates\Warlock\Character\__REALM__\__CHAR__";
    private const string TemplateAccountRoot = @"C:\Profiles\.templates\Warlock\Account";
    private const string TargetCharFolder = @"C:\WTF\Account\AltAccount\Gehennas\Jaina";
    private const string TargetAccountFolder = @"C:\WTF\Account\AltAccount";

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _replacer = _fixture.Freeze<IDirectoryReplacer>();
        _fs = _fixture.Freeze<IFileSystem>();
        _logger = new CapturingLogger<TemplateApplyService>();

        _fs.DirectoryExists(Arg.Any<string>()).Returns(false);
        _fs.FileExists(Arg.Any<string>()).Returns(false);
        _fs.GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>()).Returns([]);

        _sut = new TemplateApplyService(
            _replacer,
            new TemplateTokenizer(),
            new TemplateFileClassifier(),
            _fs,
            _logger
        );
    }

    private static TemplateSummary CharacterTemplate =>
        new()
        {
            Id = "Warlock",
            Name = "Warlock - TBC",
            Kind = TemplateKind.Character,
            RootPath = TemplateRoot,
            SourceAccountName = "MainAccount",
            SourceRealmName = "Firemaw",
            SourceCharacterName = "Thrall",
        };

    private static TemplateSummary AccountTemplate =>
        new()
        {
            Id = "Warlock",
            Name = "Warlock - Account",
            Kind = TemplateKind.Account,
            RootPath = TemplateRoot,
            SourceAccountName = "MainAccount",
        };

    private static WowCharacter TargetCharacter =>
        new()
        {
            AccountName = "AltAccount",
            RealmName = "Gehennas",
            CharacterName = "Jaina",
            FolderPath = TargetCharFolder,
        };

    private static WowAccount TargetAccount =>
        new() { AccountName = "AltAccount", FolderPath = TargetAccountFolder };

    [Test]
    public void ApplyCharacterTemplate_ExpandsTokensIntoStagingAndReplacesTarget()
    {
        // Arrange
        _fs.DirectoryExists(TemplateCharRoot).Returns(true);
        _fs.GetFiles(TemplateCharRoot, "*", SearchOption.AllDirectories)
            .Returns([TemplateCharRoot + @"\SavedVariables\Addon.lua"]);
        _fs.ReadAllText(TemplateCharRoot + @"\SavedVariables\Addon.lua")
            .Returns("name=\"{{CHAR}}\" realm=\"{{REALM}}\"");

        // Act
        _sut.ApplyCharacterTemplate(CharacterTemplate, TargetCharacter);

        // Assert
        _fs.Received()
            .WriteAllText(
                Arg.Is<string>(s =>
                    s.Contains(".template-staging-") && s.EndsWith(@"SavedVariables\Addon.lua")
                ),
                "name=\"Jaina\" realm=\"Gehennas\""
            );
        _replacer
            .Received()
            .ReplaceDirectory(
                Arg.Is<string>(s => s.Contains(".template-staging-")),
                TargetCharFolder
            );
    }

    [Test]
    public void ApplyCharacterTemplate_WhenTemplateHasNoCharacterData_Throws()
    {
        // Arrange
        _fs.DirectoryExists(TemplateCharRoot).Returns(false);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() =>
            _sut.ApplyCharacterTemplate(CharacterTemplate, TargetCharacter)
        );
    }

    [Test]
    public void ApplyAccountTemplate_OverlaysSavedVariablesAndTopLevelFiles()
    {
        // Arrange
        _fs.DirectoryExists(TemplateAccountRoot).Returns(true);
        _fs.DirectoryExists(TemplateAccountRoot + @"\SavedVariables").Returns(true);
        _fs.DirectoryExists(TargetAccountFolder).Returns(true);
        _fs.GetFiles(TemplateAccountRoot, "*", SearchOption.TopDirectoryOnly)
            .Returns([TemplateAccountRoot + @"\config-cache.wtf"]);

        // Act
        _sut.ApplyAccountTemplate(AccountTemplate, TargetAccount);

        // Assert
        _replacer
            .Received()
            .ReplaceDirectory(
                TemplateAccountRoot + @"\SavedVariables",
                @"C:\WTF\Account\AltAccount\SavedVariables"
            );
        _fs.Received()
            .CopyFile(
                TemplateAccountRoot + @"\config-cache.wtf",
                @"C:\WTF\Account\AltAccount\config-cache.wtf"
            );
    }

    [Test]
    public void ApplyAccountTemplate_WhenTemplateHasNoAccountData_Throws()
    {
        // Arrange
        _fs.DirectoryExists(TemplateAccountRoot).Returns(false);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() =>
            _sut.ApplyAccountTemplate(AccountTemplate, TargetAccount)
        );
    }
}
