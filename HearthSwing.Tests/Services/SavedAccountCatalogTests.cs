using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Models;
using HearthSwing.Models.Accounts;
using HearthSwing.Services;
using NSubstitute;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class SavedAccountCatalogTests
{
    private IFixture _fixture = null!;
    private ISettingsService _settings = null!;
    private IFileSystem _fileSystem = null!;
    private CapturingLogger<SavedAccountCatalog> _logger = null!;
    private SavedAccountCatalog _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _settings = _fixture.Freeze<ISettingsService>();
        _fileSystem = _fixture.Freeze<IFileSystem>();
        _logger = new CapturingLogger<SavedAccountCatalog>();

        _settings.Current.Returns(new AppSettings { ProfilesPath = @"C:\Profiles" });
        _fileSystem.DirectoryExists(Arg.Any<string>()).Returns(false);
        _fileSystem.FileExists(Arg.Any<string>()).Returns(false);
        _fileSystem.GetDirectories(Arg.Any<string>()).Returns([]);

        _sut = new SavedAccountCatalog(_settings, _fileSystem, _logger);
    }

    [Test]
    public void DiscoverAccounts_WhenStorageRootMissing_ReturnsEmptyList()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\Profiles").Returns(false);

        // Act
        var result = _sut.DiscoverAccounts();

        // Assert
        result.ShouldBeEmpty();
    }

    [Test]
    public void EnsureAccount_WhenAccountIsNew_CreatesStorageAndMetadata()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\Profiles").Returns(false, false);

        // Act
        var result = _sut.EnsureAccount("Alpha Account");

        // Assert
        result.Id.ShouldBe("Alpha-Account");
        result.AccountName.ShouldBe("Alpha Account");
        result.RootPath.ShouldBe(@"C:\Profiles\Alpha-Account");
        result.SnapshotPath.ShouldBe(@"C:\Profiles\Alpha-Account\Account\Alpha Account");
        _fileSystem.Received().CreateDirectory(@"C:\Profiles");
        _fileSystem.Received().CreateDirectory(@"C:\Profiles\Alpha-Account\Account\Alpha Account");
        _fileSystem
            .Received()
            .WriteAllText(
                @"C:\Profiles\Alpha-Account\account.json",
                Arg.Is<string>(json =>
                    json.Contains("Alpha-Account") && json.Contains("Alpha Account")
                )
            );
        _logger.HasInformation(message => message.Contains("Created saved account")).ShouldBeTrue();
    }

    [Test]
    public void EnsureAccount_WhenMatchingAccountAlreadyExists_ReturnsExistingSummary()
    {
        // Arrange
        const string rootPath = @"C:\Profiles\alpha-account";
        const string metadataPath = @"C:\Profiles\alpha-account\account.json";
        const string metadataJson = """
            {
              "Id": "alpha-account",
              "AccountName": "Alpha",
              "CreatedAtUtc": "2026-05-06T10:00:00+00:00"
            }
            """;

        _fileSystem.DirectoryExists(@"C:\Profiles").Returns(true);
        _fileSystem.GetDirectories(@"C:\Profiles").Returns([rootPath]);
        _fileSystem.FileExists(metadataPath).Returns(true);
        _fileSystem.ReadAllText(metadataPath).Returns(metadataJson);

        // Act
        var result = _sut.EnsureAccount("Alpha");

        // Assert
        result.Id.ShouldBe("alpha-account");
        result.AccountName.ShouldBe("Alpha");
        _fileSystem.DidNotReceive().CreateDirectory(@"C:\Profiles\Alpha\Account\Alpha");
    }

    [Test]
    public void DiscoverAccounts_WhenDirectoryHasNoMetadata_ThrowsUnsupportedStorageError()
    {
        // Arrange
        _fileSystem.DirectoryExists(@"C:\Profiles").Returns(true);
        _fileSystem.GetDirectories(@"C:\Profiles").Returns([@"C:\Profiles\legacy"]);

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => _sut.DiscoverAccounts());
        ex.Message.ShouldContain("Unsupported saved-account storage");
    }

    [Test]
    public void DiscoverAccounts_WhenActiveMarkerMatchesAccount_MarksSummaryAsActive()
    {
        // Arrange
        const string rootPath = @"C:\Profiles\alpha-account";
        const string metadataPath = @"C:\Profiles\alpha-account\account.json";
        const string markerPath = @"C:\Profiles\.active-account.json";
        const string metadataJson = """
            {
              "Id": "alpha-account",
              "AccountName": "Alpha",
              "CreatedAtUtc": "2026-05-06T10:00:00+00:00"
            }
            """;
        const string markerJson = """
            {
              "SavedAccountId": "alpha-account",
              "AccountName": "Alpha"
            }
            """;

        _fileSystem.DirectoryExists(@"C:\Profiles").Returns(true);
        _fileSystem.GetDirectories(@"C:\Profiles").Returns([rootPath]);
        _fileSystem.FileExists(metadataPath).Returns(true);
        _fileSystem.ReadAllText(metadataPath).Returns(metadataJson);
        _fileSystem.FileExists(markerPath).Returns(true);
        _fileSystem
            .ReadAllText(markerPath)
            .Returns(callInfo =>
            {
                var path = callInfo.Arg<string>();
                return path == markerPath ? markerJson : metadataJson;
            });

        // Act
        var result = _sut.DiscoverAccounts();

        // Assert
        result.ShouldHaveSingleItem();
        result[0].IsActive.ShouldBeTrue();
    }

    [Test]
    public void SetActiveAccount_ThenGetActiveAccount_RoundTripsState()
    {
        // Arrange
        const string markerPath = @"C:\Profiles\.active-account.json";
        string? markerJson = null;

        _fileSystem.DirectoryExists(@"C:\Profiles").Returns(true);
        _fileSystem
            .When(fs => fs.WriteAllText(markerPath, Arg.Any<string>()))
            .Do(callInfo => markerJson = callInfo.ArgAt<string>(1));
        _fileSystem.FileExists(markerPath).Returns(_ => markerJson is not null);
        _fileSystem.ReadAllText(markerPath).Returns(_ => markerJson ?? string.Empty);

        var activeAccount = new ActiveAccountState
        {
            SavedAccountId = "alpha-account",
            AccountName = "Alpha",
        };

        // Act
        _sut.SetActiveAccount(activeAccount);
        var result = _sut.GetActiveAccount();

        // Assert
        result.ShouldNotBeNull();
        result.SavedAccountId.ShouldBe("alpha-account");
        result.AccountName.ShouldBe("Alpha");
    }
}
