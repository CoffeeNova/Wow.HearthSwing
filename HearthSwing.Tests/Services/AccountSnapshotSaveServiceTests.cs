using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Models.Accounts;
using HearthSwing.Models.WoW;
using HearthSwing.Services;
using NSubstitute;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class AccountSnapshotSaveServiceTests
{
    private IFixture _fixture = null!;
    private ISavedAccountCatalog _catalog = null!;
    private IFileSystem _fileSystem = null!;
    private CapturingLogger<AccountSnapshotSaveService> _logger = null!;
    private AccountSnapshotSaveService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _catalog = _fixture.Freeze<ISavedAccountCatalog>();
        _fileSystem = _fixture.Freeze<IFileSystem>();
        _logger = new CapturingLogger<AccountSnapshotSaveService>();

        _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(false);
        _fileSystem.GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>()).Returns([]);
        _fileSystem.GetDirectories(Arg.Any<string>()).Returns([]);
        _fileSystem.GetAttributes(Arg.Any<string>()).Returns(FileAttributes.Normal);

        _sut = new AccountSnapshotSaveService(_catalog, _fileSystem, _logger);
    }

    [Test]
    public void Save_WhenAccountIsNew_CopiesWholeAccountAndUpdatesCatalog()
    {
        // Arrange
        var liveAccount = BuildLiveAccount();
        var savedAccount = BuildSavedAccount();

        _catalog.FindByAccountName("Alpha").Returns((SavedAccountSummary?)null);
        _catalog.EnsureAccount("Alpha").Returns(savedAccount);
        _catalog.GetById(savedAccount.Id).Returns(savedAccount with { LastSavedUtc = DateTimeOffset.UtcNow });

        _fileSystem.DirectoryExists(savedAccount.SnapshotPath).Returns(false);
        _fileSystem.GetFiles(liveAccount.FolderPath, "*", SearchOption.TopDirectoryOnly)
            .Returns([@"C:\Game\WTF\Account\Alpha\bindings-cache.wtf"]);
        _fileSystem.GetDirectories(liveAccount.FolderPath)
            .Returns([
                @"C:\Game\WTF\Account\Alpha\SavedVariables",
                @"C:\Game\WTF\Account\Alpha\Firemaw",
            ]);
        _fileSystem.GetFiles(@"C:\Game\WTF\Account\Alpha\SavedVariables", "*", SearchOption.TopDirectoryOnly)
            .Returns([@"C:\Game\WTF\Account\Alpha\SavedVariables\Addon.lua"]);
        _fileSystem.GetDirectories(@"C:\Game\WTF\Account\Alpha\SavedVariables").Returns([]);
        _fileSystem.GetFiles(@"C:\Game\WTF\Account\Alpha\Firemaw", "*", SearchOption.TopDirectoryOnly)
            .Returns([]);
        _fileSystem.GetDirectories(@"C:\Game\WTF\Account\Alpha\Firemaw")
            .Returns([@"C:\Game\WTF\Account\Alpha\Firemaw\Hero"]);
        _fileSystem.GetFiles(@"C:\Game\WTF\Account\Alpha\Firemaw\Hero", "*", SearchOption.TopDirectoryOnly)
            .Returns([@"C:\Game\WTF\Account\Alpha\Firemaw\Hero\layout-local.txt"]);
        _fileSystem.GetDirectories(@"C:\Game\WTF\Account\Alpha\Firemaw\Hero").Returns([]);

        var savePlan = new AccountSavePlan { AccountName = "Alpha" };

        // Act
        var result = _sut.Save(liveAccount, savePlan);

        // Assert
        result.Id.ShouldBe(savedAccount.Id);
        _fileSystem.Received().CopyFile(
            @"C:\Game\WTF\Account\Alpha\bindings-cache.wtf",
            @"C:\Profiles\alpha\Account\Alpha\bindings-cache.wtf"
        );
        _fileSystem.Received().CopyFile(
            @"C:\Game\WTF\Account\Alpha\Firemaw\Hero\layout-local.txt",
            @"C:\Profiles\alpha\Account\Alpha\Firemaw\Hero\layout-local.txt"
        );
        _catalog.Received().UpdateLastSaved(savedAccount.Id, Arg.Any<DateTimeOffset>());
        _catalog.Received().SetActiveAccount(
            Arg.Is<ActiveAccountState>(state =>
                state.SavedAccountId == savedAccount.Id && state.AccountName == "Alpha"
            )
        );
    }

    [Test]
    public void Save_WhenExistingAccountAndNoSelections_SkipsWrite()
    {
        // Arrange
        var liveAccount = BuildLiveAccount();
        var savedAccount = BuildSavedAccount();

        _catalog.FindByAccountName("Alpha").Returns(savedAccount);
        _catalog.EnsureAccount("Alpha").Returns(savedAccount);
        _fileSystem.DirectoryExists(savedAccount.SnapshotPath).Returns(true);

        var savePlan = new AccountSavePlan { AccountName = "Alpha" };

        // Act
        var result = _sut.Save(liveAccount, savePlan);

        // Assert
        result.Id.ShouldBe(savedAccount.Id);
        _catalog.DidNotReceive().UpdateLastSaved(savedAccount.Id, Arg.Any<DateTimeOffset>());
        _fileSystem.DidNotReceive().CopyFile(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public void Save_WhenExistingAccountAndCharacterSelected_ReplacesOnlySelectedCharacter()
    {
        // Arrange
        var liveAccount = BuildLiveAccount();
        var savedAccount = BuildSavedAccount();

        _catalog.FindByAccountName("Alpha").Returns(savedAccount);
        _catalog.EnsureAccount("Alpha").Returns(savedAccount);
        _catalog.GetById(savedAccount.Id).Returns(savedAccount);
        _fileSystem.DirectoryExists(savedAccount.SnapshotPath).Returns(true);
        _fileSystem.DirectoryExists(@"C:\Profiles\alpha\Account\Alpha\Firemaw\Hero").Returns(true);
        _fileSystem.GetFiles(@"C:\Profiles\alpha\Account\Alpha\Firemaw\Hero", "*", SearchOption.AllDirectories)
            .Returns([]);
        _fileSystem.GetFiles(@"C:\Game\WTF\Account\Alpha\Firemaw\Hero", "*", SearchOption.TopDirectoryOnly)
            .Returns([@"C:\Game\WTF\Account\Alpha\Firemaw\Hero\layout-local.txt"]);
        _fileSystem.GetDirectories(@"C:\Game\WTF\Account\Alpha\Firemaw\Hero").Returns([]);
        _fileSystem.GetFiles(@"C:\Profiles\alpha\Account\Alpha\Firemaw\Hero", "*", SearchOption.TopDirectoryOnly)
            .Returns([@"C:\Profiles\alpha\Account\Alpha\Firemaw\Hero\layout-local.txt"]);
        _fileSystem.GetDirectories(@"C:\Profiles\alpha\Account\Alpha\Firemaw\Hero").Returns([]);

        var savePlan = new AccountSavePlan
        {
            AccountName = "Alpha",
            SelectedCharacters = [new CharacterSaveSelection { RealmName = "Firemaw", CharacterName = "Hero" }],
        };

        // Act
        _sut.Save(liveAccount, savePlan);

        // Assert
        _fileSystem.Received().DeleteDirectory(@"C:\Profiles\alpha\Account\Alpha\Firemaw\Hero", true);
        _fileSystem.Received().CopyFile(
            @"C:\Game\WTF\Account\Alpha\Firemaw\Hero\layout-local.txt",
            @"C:\Profiles\alpha\Account\Alpha\Firemaw\Hero\layout-local.txt"
        );
        _fileSystem.DidNotReceive().DeleteDirectory(@"C:\Profiles\alpha\Account\Alpha\SavedVariables", true);
    }

    private static WowAccount BuildLiveAccount()
    {
        return new WowAccount
        {
            AccountName = "Alpha",
            FolderPath = @"C:\Game\WTF\Account\Alpha",
            Realms =
            [
                new WowRealm
                {
                    AccountName = "Alpha",
                    RealmName = "Firemaw",
                    FolderPath = @"C:\Game\WTF\Account\Alpha\Firemaw",
                    Characters =
                    [
                        new WowCharacter
                        {
                            AccountName = "Alpha",
                            RealmName = "Firemaw",
                            CharacterName = "Hero",
                            FolderPath = @"C:\Game\WTF\Account\Alpha\Firemaw\Hero",
                        },
                    ],
                },
            ],
        };
    }

    private static SavedAccountSummary BuildSavedAccount()
    {
        return new SavedAccountSummary
        {
            Id = "alpha",
            AccountName = "Alpha",
            RootPath = @"C:\Profiles\alpha",
            SnapshotPath = @"C:\Profiles\alpha\Account\Alpha",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }
}