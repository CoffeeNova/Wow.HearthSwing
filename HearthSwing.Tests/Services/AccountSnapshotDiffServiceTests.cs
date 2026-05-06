using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Models.Accounts;
using HearthSwing.Models.WoW;
using HearthSwing.Services;
using NSubstitute;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class AccountSnapshotDiffServiceTests
{
    private IFixture _fixture = null!;
    private IFileSystem _fileSystem = null!;
    private AccountSnapshotDiffService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _fileSystem = _fixture.Freeze<IFileSystem>();

        _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(false);
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        _fileSystem.GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>()).Returns([]);
        _fileSystem.GetFileLength(Arg.Any<string>()).Returns(0L);
        _fileSystem.ReadAllBytes(Arg.Any<string>()).Returns([]);

        _sut = new AccountSnapshotDiffService(_fileSystem, new AccountSnapshotLayout(_fileSystem));
    }

    [Test]
    public void BuildDiff_WhenSavedAccountIsMissing_MarksLiveNodesAsNew()
    {
        // Arrange
        var liveAccount = BuildLiveAccount();
        ConfigureCommonLiveAccountFiles();

        // Act
        var result = _sut.BuildDiff(liveAccount, savedAccount: null);

        // Assert
        result.IsNewAccount.ShouldBeTrue();
        result.AccountSettingsStatus.ShouldBe(AccountSnapshotDiffStatus.New);
        result.Realms.ShouldHaveSingleItem();
        result.Realms[0].Status.ShouldBe(AccountSnapshotDiffStatus.New);
        result.Realms[0].Characters.ShouldHaveSingleItem();
        result.Realms[0].Characters[0].Status.ShouldBe(AccountSnapshotDiffStatus.New);
    }

    [Test]
    public void BuildDiff_WhenSavedSnapshotMatchesLive_MarksEverythingUnchanged()
    {
        // Arrange
        var liveAccount = BuildLiveAccount();
        var savedAccount = BuildSavedAccount();
        ConfigureCommonLiveAccountFiles();
        ConfigureMatchingSavedSnapshot();

        // Act
        var result = _sut.BuildDiff(liveAccount, savedAccount);

        // Assert
        result.IsNewAccount.ShouldBeFalse();
        result.AccountSettingsStatus.ShouldBe(AccountSnapshotDiffStatus.Unchanged);
        result.Realms[0].Status.ShouldBe(AccountSnapshotDiffStatus.Unchanged);
        result.Realms[0].Characters[0].Status.ShouldBe(AccountSnapshotDiffStatus.Unchanged);
        result.HasChanges.ShouldBeFalse();
    }

    [Test]
    public void BuildDiff_WhenCharacterContentsDiffer_MarksCharacterAndRealmModified()
    {
        // Arrange
        var liveAccount = BuildLiveAccount();
        var savedAccount = BuildSavedAccount();
        ConfigureCommonLiveAccountFiles();
        ConfigureMatchingSavedSnapshot(savedCharacterBytes: [9, 9, 9]);

        // Act
        var result = _sut.BuildDiff(liveAccount, savedAccount);

        // Assert
        result.AccountSettingsStatus.ShouldBe(AccountSnapshotDiffStatus.Unchanged);
        result.Realms[0].Status.ShouldBe(AccountSnapshotDiffStatus.Modified);
        result.Realms[0].Characters[0].Status.ShouldBe(AccountSnapshotDiffStatus.Modified);
        result.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void BuildDiff_WhenAccountSettingsDiffer_MarksAccountSettingsModified()
    {
        // Arrange
        var liveAccount = BuildLiveAccount();
        var savedAccount = BuildSavedAccount();
        ConfigureCommonLiveAccountFiles();
        ConfigureMatchingSavedSnapshot(savedAccountRootBytes: [7, 7, 7]);

        // Act
        var result = _sut.BuildDiff(liveAccount, savedAccount);

        // Assert
        result.AccountSettingsStatus.ShouldBe(AccountSnapshotDiffStatus.Modified);
        result.Realms[0].Characters[0].Status.ShouldBe(AccountSnapshotDiffStatus.Unchanged);
        result.HasChanges.ShouldBeTrue();
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

    private void ConfigureCommonLiveAccountFiles()
    {
        const string liveAccountPath = @"C:\Game\WTF\Account\Alpha";
        const string liveSavedVariablesPath = @"C:\Game\WTF\Account\Alpha\SavedVariables";
        const string liveCharacterPath = @"C:\Game\WTF\Account\Alpha\Firemaw\Hero";
        const string liveAccountRootFile = @"C:\Game\WTF\Account\Alpha\bindings-cache.wtf";
        const string liveSavedVariableFile = @"C:\Game\WTF\Account\Alpha\SavedVariables\Addon.lua";
        const string liveCharacterFile = @"C:\Game\WTF\Account\Alpha\Firemaw\Hero\layout-local.txt";

        _fileSystem.DirectoryExists(liveAccountPath).Returns(true);
        _fileSystem.DirectoryExists(liveSavedVariablesPath).Returns(true);
        _fileSystem.DirectoryExists(liveCharacterPath).Returns(true);

        _fileSystem.GetFiles(liveAccountPath, "*", SearchOption.TopDirectoryOnly)
            .Returns([liveAccountRootFile]);
        _fileSystem.GetFiles(liveSavedVariablesPath, "*", SearchOption.AllDirectories)
            .Returns([liveSavedVariableFile]);
        _fileSystem.GetFiles(liveCharacterPath, "*", SearchOption.AllDirectories)
            .Returns([liveCharacterFile]);

        _fileSystem.FileExists(liveAccountRootFile).Returns(true);
        _fileSystem.FileExists(liveSavedVariableFile).Returns(true);
        _fileSystem.FileExists(liveCharacterFile).Returns(true);

        _fileSystem.GetFileLength(liveAccountRootFile).Returns(3);
        _fileSystem.GetFileLength(liveSavedVariableFile).Returns(4);
        _fileSystem.GetFileLength(liveCharacterFile).Returns(5);

        _fileSystem.ReadAllBytes(liveAccountRootFile).Returns([1, 2, 3]);
        _fileSystem.ReadAllBytes(liveSavedVariableFile).Returns([4, 5, 6, 7]);
        _fileSystem.ReadAllBytes(liveCharacterFile).Returns([8, 9, 10, 11, 12]);
    }

    private void ConfigureMatchingSavedSnapshot(
        byte[]? savedAccountRootBytes = null,
        byte[]? savedCharacterBytes = null
    )
    {
        const string savedAccountPath = @"C:\Profiles\alpha\Account\Alpha";
        const string savedSavedVariablesPath = @"C:\Profiles\alpha\Account\Alpha\SavedVariables";
        const string savedCharacterPath = @"C:\Profiles\alpha\Account\Alpha\Firemaw\Hero";
        const string savedAccountRootFile = @"C:\Profiles\alpha\Account\Alpha\bindings-cache.wtf";
        const string savedSavedVariableFile = @"C:\Profiles\alpha\Account\Alpha\SavedVariables\Addon.lua";
        const string savedCharacterFile = @"C:\Profiles\alpha\Account\Alpha\Firemaw\Hero\layout-local.txt";

        savedAccountRootBytes ??= [1, 2, 3];
        savedCharacterBytes ??= [8, 9, 10, 11, 12];

        _fileSystem.DirectoryExists(savedAccountPath).Returns(true);
        _fileSystem.DirectoryExists(savedSavedVariablesPath).Returns(true);
        _fileSystem.DirectoryExists(savedCharacterPath).Returns(true);

        _fileSystem.GetFiles(savedAccountPath, "*", SearchOption.TopDirectoryOnly)
            .Returns([savedAccountRootFile]);
        _fileSystem.GetFiles(savedSavedVariablesPath, "*", SearchOption.AllDirectories)
            .Returns([savedSavedVariableFile]);
        _fileSystem.GetFiles(savedCharacterPath, "*", SearchOption.AllDirectories)
            .Returns([savedCharacterFile]);

        _fileSystem.FileExists(savedAccountRootFile).Returns(true);
        _fileSystem.FileExists(savedSavedVariableFile).Returns(true);
        _fileSystem.FileExists(savedCharacterFile).Returns(true);

        _fileSystem.GetFileLength(savedAccountRootFile).Returns(savedAccountRootBytes.Length);
        _fileSystem.GetFileLength(savedSavedVariableFile).Returns(4);
        _fileSystem.GetFileLength(savedCharacterFile).Returns(savedCharacterBytes.Length);

        _fileSystem.ReadAllBytes(savedAccountRootFile).Returns(savedAccountRootBytes);
        _fileSystem.ReadAllBytes(savedSavedVariableFile).Returns([4, 5, 6, 7]);
        _fileSystem.ReadAllBytes(savedCharacterFile).Returns(savedCharacterBytes);
    }
}