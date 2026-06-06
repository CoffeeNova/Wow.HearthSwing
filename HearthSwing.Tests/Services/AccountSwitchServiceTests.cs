using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Models;
using HearthSwing.Models.Accounts;
using HearthSwing.Services;
using NSubstitute;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class AccountSwitchServiceTests
{
    private IFixture _fixture = null!;
    private ISettingsService _settings = null!;
    private ISavedAccountCatalog _catalog = null!;
    private IFileSystem _fileSystem = null!;
    private CapturingLogger<AccountSwitchService> _logger = null!;
    private AccountSwitchService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _settings = _fixture.Freeze<ISettingsService>();
        _catalog = _fixture.Freeze<ISavedAccountCatalog>();
        _fileSystem = _fixture.Freeze<IFileSystem>();
        _logger = new CapturingLogger<AccountSwitchService>();

        _settings.Current.Returns(
            new AppSettings { GamePath = @"C:\Game", ProfilesPath = @"C:\Profiles" }
        );
        _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(false);
        _fileSystem
            .GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>())
            .Returns([]);
        _fileSystem.GetDirectories(Arg.Any<string>()).Returns([]);
        _fileSystem.GetAttributes(Arg.Any<string>()).Returns(FileAttributes.Normal);

        _sut = new AccountSwitchService(_settings, _catalog, _fileSystem, _logger);
    }

    [Test]
    public void SwitchTo_WhenSnapshotExists_CopiesSnapshotToLiveAccountAndMarksActive()
    {
        // Arrange
        var savedAccount = BuildSavedAccount();
        _fileSystem.DirectoryExists(savedAccount.SnapshotPath).Returns(true);
        _fileSystem
            .GetFiles(savedAccount.SnapshotPath, "*", SearchOption.TopDirectoryOnly)
            .Returns([@"C:\Profiles\alpha\Account\Alpha\bindings-cache.wtf"]);
        _fileSystem
            .GetDirectories(savedAccount.SnapshotPath)
            .Returns([@"C:\Profiles\alpha\Account\Alpha\Firemaw"]);
        _fileSystem
            .GetFiles(
                @"C:\Profiles\alpha\Account\Alpha\Firemaw",
                "*",
                SearchOption.TopDirectoryOnly
            )
            .Returns([]);
        _fileSystem
            .GetDirectories(@"C:\Profiles\alpha\Account\Alpha\Firemaw")
            .Returns([@"C:\Profiles\alpha\Account\Alpha\Firemaw\Hero"]);
        _fileSystem
            .GetFiles(
                @"C:\Profiles\alpha\Account\Alpha\Firemaw\Hero",
                "*",
                SearchOption.TopDirectoryOnly
            )
            .Returns([@"C:\Profiles\alpha\Account\Alpha\Firemaw\Hero\layout-local.txt"]);
        _fileSystem.GetDirectories(@"C:\Profiles\alpha\Account\Alpha\Firemaw\Hero").Returns([]);

        // Act
        _sut.SwitchTo(savedAccount);

        // Assert
        _fileSystem
            .Received()
            .CopyFile(
                @"C:\Profiles\alpha\Account\Alpha\bindings-cache.wtf",
                @"C:\Game\WTF\Account\Alpha\bindings-cache.wtf"
            );
        _catalog
            .Received()
            .SetActiveAccount(
                Arg.Is<ActiveAccountState>(state =>
                    state.SavedAccountId == "alpha" && state.AccountName == "Alpha"
                )
            );
    }

    [Test]
    public void RestoreActiveAccount_WhenActiveAccountExists_ReappliesSavedSnapshot()
    {
        // Arrange
        var savedAccount = BuildSavedAccount();
        _catalog
            .GetActiveAccount()
            .Returns(new ActiveAccountState { SavedAccountId = "alpha", AccountName = "Alpha" });
        _catalog.GetById("alpha").Returns(savedAccount);
        _fileSystem.DirectoryExists(savedAccount.SnapshotPath).Returns(true);
        _fileSystem
            .GetFiles(savedAccount.SnapshotPath, "*", SearchOption.TopDirectoryOnly)
            .Returns([]);
        _fileSystem.GetDirectories(savedAccount.SnapshotPath).Returns([]);

        // Act
        _sut.RestoreActiveAccount();

        // Assert
        _catalog.Received().GetById("alpha");
        _catalog.Received().SetActiveAccount(Arg.Any<ActiveAccountState>());
    }

    [Test]
    public void RestoreActiveAccount_WhenNoActiveAccount_Throws()
    {
        // Arrange
        _catalog.GetActiveAccount().Returns((ActiveAccountState?)null);

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => _sut.RestoreActiveAccount());
        ex.Message.ShouldContain("No active saved account");
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
