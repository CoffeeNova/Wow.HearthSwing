using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Services;
using NSubstitute;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class WtfInspectorTests
{
    private IFixture _fixture = null!;
    private IFileSystem _fileSystem = null!;
    private CapturingLogger<WtfInspector> _logger = null!;
    private WtfInspector _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _fileSystem = _fixture.Freeze<IFileSystem>();
        _logger = new CapturingLogger<WtfInspector>();

        _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(false);
        _fileSystem.GetDirectories(Arg.Any<string>()).Returns([]);

        _sut = new WtfInspector(_fileSystem, _logger);
    }

    [Test]
    public void Inspect_WhenGamePathIsMissing_ThrowsArgumentException()
    {
        // Arrange

        // Act & Assert
        Should.Throw<ArgumentException>(() => _sut.Inspect(string.Empty));
    }

    [Test]
    public void Inspect_WhenWtfDirectoryIsMissing_ThrowsInvalidOperationException()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\Game\WTF").Returns(false);

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => _sut.Inspect(@"C:\Game"));
        ex.Message.ShouldContain(@"C:\Game\WTF");
    }

    [Test]
    public void Inspect_WhenAccountDirectoryIsMissing_ReturnsInstallationWithoutAccounts()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _fileSystem.DirectoryExists(@"C:\Game\WTF\Account").Returns(false);

        // Act
        var result = _sut.Inspect(@"C:\Game");

        // Assert
        result.GamePath.ShouldBe(@"C:\Game");
        result.WtfPath.ShouldBe(@"C:\Game\WTF");
        result.Accounts.ShouldBeEmpty();
    }

    [Test]
    public void Inspect_WhenWtfContainsAccountsRealmsAndCharacters_ReturnsTypedHierarchy()
    {
        // Arrange
        _fileSystem.DirectoryExists(Arg.Any<string>())
            .Returns(callInfo =>
            {
                var path = callInfo.Arg<string>();
                return path is @"C:\Game\WTF" or @"C:\Game\WTF\Account";
            });
        _fileSystem.GetDirectories(Arg.Any<string>())
            .Returns(callInfo =>
            {
                var path = callInfo.Arg<string>();

                return path switch
                {
                    @"C:\Game\WTF\Account" =>
                    [
                        @"C:\Game\WTF\Account\Zulu",
                        @"C:\Game\WTF\Account\Alpha",
                        @"C:\Game\WTF\Account\.cache",
                    ],
                    @"C:\Game\WTF\Account\Alpha" =>
                    [
                        @"C:\Game\WTF\Account\Alpha\SavedVariables",
                        @"C:\Game\WTF\Account\Alpha\Firemaw",
                    ],
                    @"C:\Game\WTF\Account\Zulu" =>
                    [
                        @"C:\Game\WTF\Account\Zulu\Pyrewood",
                    ],
                    @"C:\Game\WTF\Account\Alpha\Firemaw" =>
                    [
                        @"C:\Game\WTF\Account\Alpha\Firemaw\CharacterB",
                        @"C:\Game\WTF\Account\Alpha\Firemaw\CharacterA",
                    ],
                    @"C:\Game\WTF\Account\Zulu\Pyrewood" =>
                    [
                        @"C:\Game\WTF\Account\Zulu\Pyrewood\CharacterZ",
                    ],
                    _ => [],
                };
            });

        // Act
        var result = _sut.Inspect(@"C:\Game");

        // Assert
        result.Accounts.Count.ShouldBe(2);
        result.Accounts[0].AccountName.ShouldBe("Alpha");
        result.Accounts[1].AccountName.ShouldBe("Zulu");

        result.Accounts[0].Realms.Count.ShouldBe(1);
        result.Accounts[0].Realms[0].RealmName.ShouldBe("Firemaw");
        result.Accounts[0].Realms[0].Characters.Count.ShouldBe(2);
        result.Accounts[0].Realms[0].Characters[0].CharacterName.ShouldBe("CharacterA");
        result.Accounts[0].Realms[0].Characters[1].CharacterName.ShouldBe("CharacterB");

        result.Accounts[1].Realms.Count.ShouldBe(1);
        result.Accounts[1].Realms[0].RealmName.ShouldBe("Pyrewood");
        result.Accounts[1].Realms[0].Characters[0].CharacterName.ShouldBe("CharacterZ");
    }
}
