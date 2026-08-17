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
    private const string TemplateSharedRoot = @"C:\Profiles\.templates\Warlock\Shared";
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
    public void ApplyCharacterTemplate_AlsoAppliesSharedAccountSettingsToTargetAccountRoot()
    {
        // Arrange
        _fs.DirectoryExists(TemplateCharRoot).Returns(true);
        _fs.DirectoryExists(TemplateSharedRoot).Returns(true);
        _fs.DirectoryExists(TemplateSharedRoot + @"\SavedVariables").Returns(true);
        _fs.DirectoryExists(TargetAccountFolder).Returns(true);
        _fs.GetFiles(TemplateCharRoot, "*", SearchOption.AllDirectories)
            .Returns([TemplateCharRoot + @"\SavedVariables\Addon.lua"]);
        _fs.ReadAllText(TemplateCharRoot + @"\SavedVariables\Addon.lua")
            .Returns("name=\"{{CHAR}}\" realm=\"{{REALM}}\"");
        _fs.GetFiles(TemplateSharedRoot, "*", SearchOption.TopDirectoryOnly)
            .Returns([TemplateSharedRoot + @"\config-cache.wtf"]);

        // Act
        _sut.ApplyCharacterTemplate(CharacterTemplate, TargetCharacter);

        // Assert
        _replacer
            .Received()
            .ReplaceDirectory(
                TemplateSharedRoot + @"\SavedVariables",
                TargetAccountFolder + @"\SavedVariables"
            );
        _fs.Received()
            .CopyFile(
                TemplateSharedRoot + @"\config-cache.wtf",
                TargetAccountFolder + @"\config-cache.wtf"
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

    [Test]
    public void ApplyCharacterTemplate_CacheOnly_WritesOnlyCacheFilesWithoutReplacingDirectory()
    {
        // Arrange
        var cacheFile = TemplateCharRoot + @"\Account\bindings-cache.wtf";
        var ignoredFile = TemplateCharRoot + @"\SavedVariables\Addon.lua";

        _fs.DirectoryExists(TemplateCharRoot).Returns(true);
        _fs.GetFiles(TemplateCharRoot, "*", SearchOption.AllDirectories)
            .Returns([cacheFile, ignoredFile]);
        _fs.ReadAllText(cacheFile).Returns("name=\"{{CHAR}}\" realm=\"{{REALM}}\"");

        // Act
        _sut.ApplyCharacterTemplate(
            CharacterTemplate,
            TargetCharacter,
            TemplateApplyScope.CacheOnly
        );

        // Assert
        _replacer.DidNotReceive().ReplaceDirectory(Arg.Any<string>(), Arg.Any<string>());
        _fs.Received()
            .WriteAllText(
                TargetCharFolder + @"\Account\bindings-cache.wtf",
                "name=\"Jaina\" realm=\"Gehennas\""
            );
        _fs.DidNotReceive()
            .WriteAllText(Arg.Is<string>(p => p.Contains("Addon.lua")), Arg.Any<string>());
    }

    [Test]
    public void ApplyCharacterTemplate_CacheOnly_AlsoWritesSharedCacheFilesToAccountRoot()
    {
        // Arrange
        var cacheFile = TemplateSharedRoot + @"\bindings-cache.wtf";
        var ignoredFile = TemplateSharedRoot + @"\SavedVariables\Addon.lua";

        _fs.DirectoryExists(TemplateCharRoot).Returns(true);
        _fs.DirectoryExists(TemplateSharedRoot).Returns(true);
        _fs.FileExists(cacheFile).Returns(true);
        _fs.GetFiles(TemplateCharRoot, "*", SearchOption.AllDirectories)
            .Returns([TemplateCharRoot + @"\SavedVariables\Addon.lua"]);
        _fs.GetFiles(TemplateSharedRoot, "*", SearchOption.AllDirectories)
            .Returns([cacheFile, ignoredFile]);
        _fs.ReadAllBytes(cacheFile).Returns([4, 5, 6]);

        // Act
        _sut.ApplyCharacterTemplate(
            CharacterTemplate,
            TargetCharacter,
            TemplateApplyScope.CacheOnly
        );

        // Assert
        _fs.Received()
            .WriteAllBytes(TargetAccountFolder + @"\bindings-cache.wtf", Arg.Any<byte[]>());
        _fs.DidNotReceive()
            .WriteAllBytes(Arg.Is<string>(p => p.Contains("Addon.lua")), Arg.Any<byte[]>());
    }

    [Test]
    public void ApplyCharacterTemplate_WhenIncludeAccountScopedDisabled_DoesNotApplySharedFolder()
    {
        // Arrange
        _fs.DirectoryExists(TemplateCharRoot).Returns(true);
        _fs.DirectoryExists(TemplateSharedRoot).Returns(true);
        _fs.DirectoryExists(TemplateSharedRoot + @"\SavedVariables").Returns(true);
        _fs.GetFiles(TemplateCharRoot, "*", SearchOption.AllDirectories)
            .Returns([TemplateCharRoot + @"\SavedVariables\Addon.lua"]);
        _fs.ReadAllText(TemplateCharRoot + @"\SavedVariables\Addon.lua")
            .Returns("name=\"{{CHAR}}\" realm=\"{{REALM}}\"");

        // Act
        _sut.ApplyCharacterTemplate(
            CharacterTemplate,
            TargetCharacter,
            TemplateApplyScope.Full,
            includeAccountScoped: false
        );

        // Assert
        _replacer
            .DidNotReceive()
            .ReplaceDirectory(
                TemplateSharedRoot + @"\SavedVariables",
                TargetAccountFolder + @"\SavedVariables"
            );
        _fs.DidNotReceive()
            .CopyFile(
                Arg.Is<string>(path =>
                    path.StartsWith(TemplateSharedRoot, StringComparison.Ordinal)
                ),
                Arg.Any<string>()
            );
    }

    [Test]
    public void ApplyAccountTemplate_CacheOnly_WritesOnlyCacheFilesWithoutReplacingSavedVariables()
    {
        // Arrange
        var cacheFile = TemplateAccountRoot + @"\bindings-cache.wtf";
        var ignoredFile = TemplateAccountRoot + @"\SavedVariables\Addon.lua";

        _fs.DirectoryExists(TemplateAccountRoot).Returns(true);
        _fs.DirectoryExists(TemplateAccountRoot + @"\SavedVariables").Returns(true);
        _fs.DirectoryExists(TargetAccountFolder).Returns(true);
        _fs.GetFiles(TemplateAccountRoot, "*", SearchOption.AllDirectories)
            .Returns([cacheFile, ignoredFile]);
        _fs.ReadAllBytes(cacheFile).Returns([4, 5, 6]);

        // Act
        _sut.ApplyAccountTemplate(AccountTemplate, TargetAccount, TemplateApplyScope.CacheOnly);

        // Assert
        _replacer.DidNotReceive().ReplaceDirectory(Arg.Any<string>(), Arg.Any<string>());
        _fs.Received()
            .WriteAllBytes(TargetAccountFolder + @"\bindings-cache.wtf", Arg.Any<byte[]>());
        _fs.DidNotReceive()
            .WriteAllBytes(Arg.Is<string>(p => p.Contains("Addon.lua")), Arg.Any<byte[]>());
    }
}
