using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Models;
using HearthSwing.Models.Profiles;
using HearthSwing.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using System.Text.Json;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class ProfileManagerTests
{
    private IFixture _fixture = null!;
    private IFileSystem _fs = null!;
    private ISettingsService _settings = null!;
    private CapturingLogger<ProfileManager> _logger = null!;
    private ProfileManager _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _fs = _fixture.Freeze<IFileSystem>();

        _fs.FileExists(Arg.Any<string>()).Returns(false);
        _fs.DirectoryExists(Arg.Any<string>()).Returns(false);
        _fs.ReadAllText(Arg.Any<string>()).Returns(string.Empty);
        _fs.GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>()).Returns([]);
        _fs.GetDirectories(Arg.Any<string>()).Returns([]);

        _settings = _fixture.Freeze<ISettingsService>();
        _settings.Current.Returns(
            new AppSettings { GamePath = @"C:\Game", ProfilesPath = @"C:\Game\Profiles" }
        );

        _logger = new CapturingLogger<ProfileManager>();
        _sut = new ProfileManager(_settings, _fs, _logger);
    }

    private static bool IsActiveMarkerJson(string contents, string id, string snapshotPath)
    {
        using var document = JsonDocument.Parse(contents);
        var root = document.RootElement;

        return root.GetProperty("Id").GetString() == id
            && root.GetProperty("SnapshotPath").GetString() == snapshotPath;
    }

    private static bool IsActiveMarkerWithAccount(
        string contents,
        string id,
        ProfileGranularity granularity,
        string accountName
    )
    {
        try
        {
            using var doc = JsonDocument.Parse(contents);
            var root = doc.RootElement;
            return root.GetProperty("Id").GetString() == id
                && root.GetProperty("Granularity").GetInt32() == (int)granularity
                && root.GetProperty("AccountName").GetString() == accountName;
        }
        catch
        {
            return false;
        }
    }

    [Test]
    public void DiscoverProfiles_WhenProfilesPathDoesNotExist_ReturnsEmptyList()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Game\Profiles").Returns(false);

        // Act
        var result = _sut.DiscoverProfiles();

        // Assert
        result.ShouldBeEmpty();
    }

    [Test]
    public void DiscoverProfiles_WhenProfilesExist_ReturnsAllProfilesSorted()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Game\Profiles").Returns(true);
        _fs.GetDirectories(@"C:\Game\Profiles")
            .Returns([@"C:\Game\Profiles\Bob", @"C:\Game\Profiles\Alice"]);

        // Act
        var result = _sut.DiscoverProfiles();

        // Assert
        result.Count.ShouldBe(2);
        result[0].Id.ShouldBe("Alice");
        result[1].Id.ShouldBe("Bob");
    }

    [Test]
    public void DiscoverProfiles_WhenActiveMarkerPointsToAbsentFolder_DoesNotAddGhostEntry()
    {
        // Arrange
        var markerPath = @"C:\Game\Profiles\.active";
        _fs.DirectoryExists(@"C:\Game\Profiles").Returns(true);
        _fs.FileExists(markerPath).Returns(true);
        _fs.ReadAllText(markerPath).Returns("Alice");
        _fs.GetDirectories(@"C:\Game\Profiles").Returns([@"C:\Game\Profiles\Bob"]);

        // Act
        var result = _sut.DiscoverProfiles();

        // Assert
        result.Count.ShouldBe(1);
        result[0].Id.ShouldBe("Bob");
        result[0].IsActive.ShouldBeFalse();
    }

    [Test]
    public void DiscoverProfiles_WhenActiveMarkerMatchesExistingFolder_MarksItActive()
    {
        // Arrange
        var markerPath = @"C:\Game\Profiles\.active";
        _fs.DirectoryExists(@"C:\Game\Profiles").Returns(true);
        _fs.FileExists(markerPath).Returns(true);
        _fs.ReadAllText(markerPath).Returns("Alice");
        _fs.GetDirectories(@"C:\Game\Profiles")
            .Returns([@"C:\Game\Profiles\Alice", @"C:\Game\Profiles\Bob"]);

        // Act
        var result = _sut.DiscoverProfiles();

        // Assert
        result.Count.ShouldBe(2);
        result.First(p => p.Id == "Alice").IsActive.ShouldBeTrue();
        result.First(p => p.Id == "Bob").IsActive.ShouldBeFalse();
    }

    [Test]
    public void DiscoverProfiles_WhenNoActiveMarker_AllProfilesInactive()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Game\Profiles").Returns(true);
        _fs.GetDirectories(@"C:\Game\Profiles").Returns([@"C:\Game\Profiles\Alice"]);

        // Act
        var result = _sut.DiscoverProfiles();

        // Assert
        result.ShouldAllBe(p => !p.IsActive);
    }

    [Test]
    public void DetectCurrentProfile_WhenNoMarker_ReturnsNull()
    {
        // Act
        var result = _sut.DetectCurrentProfile();

        // Assert
        result.ShouldBeNull();
    }

    [Test]
    public void DetectCurrentProfile_WhenMarkerExists_ReturnsProfile()
    {
        // Arrange
        var markerPath = @"C:\Game\Profiles\.active";
        _fs.FileExists(markerPath).Returns(true);
        _fs.ReadAllText(markerPath).Returns("Alice");

        // Act
        var result = _sut.DetectCurrentProfile();

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe("Alice");
        result.IsActive.ShouldBeTrue();
        result.FolderPath.ShouldBe(@"C:\Game\Profiles\Alice");
    }

    [Test]
    public void DetectCurrentProfile_WhenMarkerReadThrows_ReturnsNull()
    {
        // Arrange
        var markerPath = @"C:\Game\Profiles\.active";
        _fs.FileExists(markerPath).Returns(true);
        _fs.ReadAllText(markerPath).Throws(new IOException("locked"));

        // Act
        var result = _sut.DetectCurrentProfile();

        // Assert
        result.ShouldBeNull();
    }

    [Test]
    public void DetectCurrentProfile_WhenMarkerContainsJson_UsesSnapshotPathFromActiveState()
    {
        // Arrange
        var markerPath = @"C:\Game\Profiles\.active";
        _fs.FileExists(markerPath).Returns(true);
        _fs.ReadAllText(markerPath)
            .Returns(
                """
                {
                  "Id": "Alice",
                  "Granularity": 0,
                  "SnapshotPath": "C:\\Snapshots\\Alice"
                }
                """
            );

        // Act
        var result = _sut.DetectCurrentProfile();

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe("Alice");
        result.FolderPath.ShouldBe(@"C:\Snapshots\Alice");
    }

    [Test]
    public void SwitchTo_WhenTargetFolderNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        var target = new ProfileInfo { Id = "Alice", FolderPath = @"C:\Game\Profiles\Alice" };
        _fs.DirectoryExists(@"C:\Game\Profiles\Alice").Returns(false);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => _sut.SwitchTo(target));
    }

    [Test]
    public void SwitchTo_WhenTargetIsAlreadyActive_LogsAndReturnsEarly()
    {
        // Arrange
        var markerPath = @"C:\Game\Profiles\.active";
        _fs.FileExists(markerPath).Returns(true);
        _fs.ReadAllText(markerPath).Returns("Alice");

        var target = new ProfileInfo { Id = "Alice", FolderPath = @"C:\Game\Profiles\Alice" };

        // Act
        _sut.SwitchTo(target);

        // Assert
        _logger.HasInformation(m => m.Contains("already active")).ShouldBeTrue();
        _fs.DidNotReceive().DeleteDirectory(Arg.Any<string>(), Arg.Any<bool>());
        _fs.DidNotReceive().CopyFile(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public void SwitchTo_WhenNoWtfExists_CopiesTargetToWtf()
    {
        // Arrange
        var target = new ProfileInfo { Id = "Alice", FolderPath = @"C:\Game\Profiles\Alice" };
        _fs.DirectoryExists(@"C:\Game\Profiles\Alice").Returns(true);
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(false);
        _fs.DirectoryExists(@"C:\Game\Profiles").Returns(true);
        _fs.GetFiles(@"C:\Game\Profiles\Alice", "*", SearchOption.TopDirectoryOnly)
            .Returns([@"C:\Game\Profiles\Alice\Config.wtf"]);
        _fs.GetDirectories(@"C:\Game\Profiles\Alice").Returns([]);

        // Act
        _sut.SwitchTo(target);

        // Assert
        _fs.DidNotReceive().DeleteDirectory(@"C:\Game\WTF", true);
        _fs.Received().CreateDirectory(@"C:\Game\WTF");
        _fs.Received().CopyFile(@"C:\Game\Profiles\Alice\Config.wtf", @"C:\Game\WTF\Config.wtf");
        _fs.Received()
            .WriteAllText(
                @"C:\Game\Profiles\.active",
                Arg.Is<string>(contents =>
                    IsActiveMarkerJson(contents, "Alice", @"C:\Game\Profiles\Alice")
                )
            );
        _logger.HasInformation(m => m.Contains("Profile switched to")).ShouldBeTrue();
    }

    [Test]
    public void SwitchTo_WhenWtfExists_DeletesWtfThenCopiesTarget()
    {
        // Arrange
        var markerPath = @"C:\Game\Profiles\.active";
        _fs.FileExists(markerPath).Returns(true);
        _fs.ReadAllText(markerPath).Returns("Bob");
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _fs.DirectoryExists(@"C:\Game\Profiles").Returns(true);

        var target = new ProfileInfo { Id = "Alice", FolderPath = @"C:\Game\Profiles\Alice" };
        _fs.DirectoryExists(@"C:\Game\Profiles\Alice").Returns(true);
        _fs.GetFiles(@"C:\Game\Profiles\Alice", "*", SearchOption.TopDirectoryOnly)
            .Returns([@"C:\Game\Profiles\Alice\Config.wtf"]);
        _fs.GetDirectories(@"C:\Game\Profiles\Alice").Returns([]);

        // Act
        _sut.SwitchTo(target);

        // Assert
        Received.InOrder(() =>
        {
            _fs.DeleteDirectory(@"C:\Game\WTF", true);
            _fs.CreateDirectory(@"C:\Game\WTF");
            _fs.CopyFile(@"C:\Game\Profiles\Alice\Config.wtf", @"C:\Game\WTF\Config.wtf");
        });
        _logger.HasInformation(m => m.Contains("Removing current WTF")).ShouldBeTrue();
        _logger.HasInformation(m => m.Contains("Activating")).ShouldBeTrue();
    }

    [Test]
    public void SwitchTo_WhenWtfHasReadOnlyFiles_ClearsAttributesBeforeDelete()
    {
        // Arrange
        var readOnlyFile = @"C:\Game\WTF\Account\macros-cache.old";
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _fs.DirectoryExists(@"C:\Game\Profiles").Returns(true);
        var target = new ProfileInfo { Id = "Alice", FolderPath = @"C:\Game\Profiles\Alice" };
        _fs.DirectoryExists(@"C:\Game\Profiles\Alice").Returns(true);
        _fs.GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>())
            .Returns(info =>
            {
                var dir = info.ArgAt<string>(0);
                var opt = info.ArgAt<SearchOption>(2);
                if (dir == @"C:\Game\WTF" && opt == SearchOption.AllDirectories)
                    return new[] { readOnlyFile };
                return Array.Empty<string>();
            });
        _fs.GetAttributes(readOnlyFile).Returns(FileAttributes.ReadOnly);

        // Act
        _sut.SwitchTo(target);

        // Assert
        _fs.Received().SetAttributes(readOnlyFile, FileAttributes.None);
        _fs.Received().DeleteDirectory(@"C:\Game\WTF", true);
    }

    [Test]
    public void SwitchTo_ProfileFolderRemainsInProfilesAfterSwitch()
    {
        // Arrange
        var target = new ProfileInfo { Id = "Alice", FolderPath = @"C:\Game\Profiles\Alice" };
        _fs.DirectoryExists(@"C:\Game\Profiles\Alice").Returns(true);
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(false);
        _fs.DirectoryExists(@"C:\Game\Profiles").Returns(true);
        _fs.GetFiles(@"C:\Game\Profiles\Alice", "*", SearchOption.TopDirectoryOnly).Returns([]);
        _fs.GetDirectories(@"C:\Game\Profiles\Alice").Returns([]);

        // Act
        _sut.SwitchTo(target);

        // Assert — profile folder is never moved or deleted
        _fs.DidNotReceive().DeleteDirectory(@"C:\Game\Profiles\Alice", Arg.Any<bool>());
        _fs.DidNotReceive().MoveDirectory(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public void SwitchTo_WhenWtfExistsButNoActiveMarker_DeletesWtfAndCopiesTarget()
    {
        // Arrange — WTF exists but no .active marker
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _fs.DirectoryExists(@"C:\Game\Profiles").Returns(true);

        var target = new ProfileInfo { Id = "Alice", FolderPath = @"C:\Game\Profiles\Alice" };
        _fs.DirectoryExists(@"C:\Game\Profiles\Alice").Returns(true);
        _fs.GetFiles(@"C:\Game\Profiles\Alice", "*", SearchOption.TopDirectoryOnly).Returns([]);
        _fs.GetDirectories(@"C:\Game\Profiles\Alice").Returns([]);

        // Act
        _sut.SwitchTo(target);

        // Assert
        _fs.Received().DeleteDirectory(@"C:\Game\WTF", true);
        _fs.Received().CreateDirectory(@"C:\Game\WTF");
        _logger.HasInformation(m => m.Contains("Removing current WTF")).ShouldBeTrue();
        _logger.HasInformation(m => m.Contains("Profile switched to")).ShouldBeTrue();
    }

    [Test]
    public void SwitchTo_WhenActivationFails_RestoresPreviousWtfFromRollbackSnapshot()
    {
        // Arrange
        string? rollbackPath = null;
        var target = new ProfileInfo { Id = "Alice", FolderPath = @"C:\Game\Profiles\Alice" };

        _fs.DirectoryExists(Arg.Any<string>())
            .Returns(callInfo =>
            {
                var path = callInfo.Arg<string>();
                return path == @"C:\Game\WTF"
                    || path == @"C:\Game\Profiles"
                    || path == @"C:\Game\Profiles\Alice"
                    || path == rollbackPath;
            });

        _fs.GetFiles(Arg.Any<string>(), "*", SearchOption.TopDirectoryOnly)
            .Returns(callInfo =>
            {
                var path = callInfo.ArgAt<string>(0);
                if (path == @"C:\Game\WTF")
                    return [@"C:\Game\WTF\Config.wtf"];

                if (path == @"C:\Game\Profiles\Alice")
                    return [@"C:\Game\Profiles\Alice\Config.wtf"];

                if (rollbackPath is not null && path == rollbackPath)
                    return [Path.Combine(rollbackPath, "Config.wtf")];

                return [];
            });

        _fs.GetDirectories(Arg.Any<string>()).Returns([]);
        _fs.When(fs =>
                fs.CreateDirectory(
                    Arg.Is<string>(path => path.Contains(@"\.rollback-WTF-", StringComparison.Ordinal))
                )
            )
            .Do(callInfo => rollbackPath = callInfo.Arg<string>());
        _fs.When(fs => fs.CopyFile(@"C:\Game\Profiles\Alice\Config.wtf", @"C:\Game\WTF\Config.wtf"))
            .Do(_ => throw new IOException("copy failed"));

        // Act
        var ex = Should.Throw<IOException>(() => _sut.SwitchTo(target));

        // Assert
        ex.Message.ShouldContain("copy failed");
        rollbackPath.ShouldNotBeNull();
        _fs.Received().CopyFile(@"C:\Game\WTF\Config.wtf", Path.Combine(rollbackPath, "Config.wtf"));
        _fs.Received().CopyFile(Path.Combine(rollbackPath, "Config.wtf"), @"C:\Game\WTF\Config.wtf");
        _fs.Received().DeleteDirectory(rollbackPath, true);
        _logger.HasWarning(m => m.Contains("Rollback completed")).ShouldBeTrue();
    }

    [Test]
    public void SaveCurrentAsProfile_WhenWtfNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(false);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => _sut.SaveCurrentAsProfile("Test"));
    }

    [Test]
    public void SaveCurrentAsProfile_WhenWtfExists_CopiesAndWritesMarker()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _fs.DirectoryExists(@"C:\Game\Profiles").Returns(true);
        _fs.DirectoryExists(@"C:\Game\Profiles\Test").Returns(false);
        _fs.GetFiles(@"C:\Game\WTF", "*", SearchOption.TopDirectoryOnly)
            .Returns([@"C:\Game\WTF\Config.wtf"]);
        _fs.GetDirectories(@"C:\Game\WTF").Returns([]);

        // Act
        _sut.SaveCurrentAsProfile("Test");

        // Assert
        _fs.Received().CreateDirectory(@"C:\Game\Profiles\Test");
        _fs.Received().CopyFile(@"C:\Game\WTF\Config.wtf", @"C:\Game\Profiles\Test\Config.wtf");
        _fs.Received()
            .WriteAllText(
                @"C:\Game\Profiles\.active",
                Arg.Is<string>(contents =>
                    IsActiveMarkerJson(contents, "Test", @"C:\Game\Profiles\Test")
                )
            );
    }

    [Test]
    public void SaveCurrentAsProfile_WhenProfileAlreadyExists_ClearsReadOnlyThenDeletes()
    {
        // Arrange
        var readOnlyFile = @"C:\Game\Profiles\Test\Account\macros-cache.old";
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _fs.DirectoryExists(@"C:\Game\Profiles").Returns(true);
        _fs.DirectoryExists(@"C:\Game\Profiles\Test").Returns(true);
        _fs.GetDirectories(Arg.Any<string>()).Returns([]);
        _fs.GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>())
            .Returns(info =>
            {
                var dir = info.ArgAt<string>(0);
                var opt = info.ArgAt<SearchOption>(2);
                if (dir == @"C:\Game\Profiles\Test" && opt == SearchOption.AllDirectories)
                    return new[] { readOnlyFile };
                return Array.Empty<string>();
            });
        _fs.GetAttributes(readOnlyFile).Returns(FileAttributes.ReadOnly);

        // Act
        _sut.SaveCurrentAsProfile("Test");

        // Assert
        _fs.Received().GetFiles(@"C:\Game\Profiles\Test", "*", SearchOption.AllDirectories);
        _fs.Received().GetAttributes(readOnlyFile);
        _fs.Received().SetAttributes(readOnlyFile, FileAttributes.None);
        _fs.Received().DeleteDirectory(@"C:\Game\Profiles\Test", true);
        _logger.HasInformation(m => m.Contains("Overwriting")).ShouldBeTrue();
    }

    [Test]
    public void SaveCurrentAsProfile_WhenProfilesPathMissing_CreatesIt()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _fs.DirectoryExists(@"C:\Game\Profiles").Returns(false);
        _fs.GetFiles(@"C:\Game\WTF", "*", SearchOption.TopDirectoryOnly).Returns([]);
        _fs.GetDirectories(@"C:\Game\WTF").Returns([]);

        // Act
        _sut.SaveCurrentAsProfile("Test");

        // Assert
        _fs.Received().CreateDirectory(@"C:\Game\Profiles");
    }

    [Test]
    public void RestoreActiveProfile_WhenNoActiveMarker_ThrowsInvalidOperationException()
    {
        // Arrange — no .active marker file

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => _sut.RestoreActiveProfile());
    }

    [Test]
    public void RestoreActiveProfile_WhenSavedProfileNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        var markerPath = @"C:\Game\Profiles\.active";
        _fs.FileExists(markerPath).Returns(true);
        _fs.ReadAllText(markerPath).Returns("Alice");
        _fs.DirectoryExists(@"C:\Game\Profiles\Alice").Returns(false);

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => _sut.RestoreActiveProfile());
        ex.Message.ShouldContain("Alice");
    }

    [Test]
    public void RestoreActiveProfile_WhenSavedProfileExists_DeletesWtfAndCopiesProfile()
    {
        // Arrange
        var markerPath = @"C:\Game\Profiles\.active";
        _fs.FileExists(markerPath).Returns(true);
        _fs.ReadAllText(markerPath).Returns("Alice");
        _fs.DirectoryExists(@"C:\Game\Profiles\Alice").Returns(true);
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _fs.GetFiles(@"C:\Game\Profiles\Alice", "*", SearchOption.TopDirectoryOnly)
            .Returns([@"C:\Game\Profiles\Alice\Config.wtf"]);
        _fs.GetDirectories(@"C:\Game\Profiles\Alice").Returns([]);

        // Act
        _sut.RestoreActiveProfile();

        // Assert
        Received.InOrder(() =>
        {
            _fs.DeleteDirectory(@"C:\Game\WTF", true);
            _fs.CreateDirectory(@"C:\Game\WTF");
            _fs.CopyFile(@"C:\Game\Profiles\Alice\Config.wtf", @"C:\Game\WTF\Config.wtf");
        });
        _logger.HasInformation(m => m.Contains("restored")).ShouldBeTrue();
    }

    [Test]
    public void RestoreActiveProfile_WhenWtfDoesNotExist_CopiesProfileWithoutDeletion()
    {
        // Arrange
        var markerPath = @"C:\Game\Profiles\.active";
        _fs.FileExists(markerPath).Returns(true);
        _fs.ReadAllText(markerPath).Returns("Alice");
        _fs.DirectoryExists(@"C:\Game\Profiles\Alice").Returns(true);
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(false);
        _fs.GetFiles(@"C:\Game\Profiles\Alice", "*", SearchOption.TopDirectoryOnly)
            .Returns([@"C:\Game\Profiles\Alice\Config.wtf"]);
        _fs.GetDirectories(@"C:\Game\Profiles\Alice").Returns([]);

        // Act
        _sut.RestoreActiveProfile();

        // Assert
        _fs.DidNotReceive().DeleteDirectory(@"C:\Game\WTF", Arg.Any<bool>());
        _fs.Received().CreateDirectory(@"C:\Game\WTF");
        _fs.Received().CopyFile(@"C:\Game\Profiles\Alice\Config.wtf", @"C:\Game\WTF\Config.wtf");
    }

    [Test]
    public void RestoreActiveProfile_WhenWtfHasReadOnlyFiles_ClearsAttributesBeforeDelete()
    {
        // Arrange
        var readOnlyFile = @"C:\Game\WTF\Account\bindings-cache.wtf";
        var markerPath = @"C:\Game\Profiles\.active";
        _fs.FileExists(markerPath).Returns(true);
        _fs.ReadAllText(markerPath).Returns("Alice");
        _fs.DirectoryExists(@"C:\Game\Profiles\Alice").Returns(true);
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _fs.GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>())
            .Returns(info =>
            {
                var dir = info.ArgAt<string>(0);
                var opt = info.ArgAt<SearchOption>(2);
                if (dir == @"C:\Game\WTF" && opt == SearchOption.AllDirectories)
                    return new[] { readOnlyFile };
                return Array.Empty<string>();
            });
        _fs.GetAttributes(readOnlyFile).Returns(FileAttributes.ReadOnly);
        _fs.GetDirectories(@"C:\Game\Profiles\Alice").Returns([]);

        // Act
        _sut.RestoreActiveProfile();

        // Assert
        _fs.Received().SetAttributes(readOnlyFile, FileAttributes.None);
        _fs.Received().DeleteDirectory(@"C:\Game\WTF", true);
    }

    [Test]
    public void SwitchTo_WhenFullWtfDescriptor_ReplacesEntireWtfDirectory()
    {
        // Arrange
        var descriptor = new ProfileDescriptor
        {
            Id = "ihar-full",
            Granularity = ProfileGranularity.FullWtf,
            SnapshotPath = @"C:\Game\Profiles\owners\ihar\full",
        };
        _fs.DirectoryExists(@"C:\Game\Profiles\owners\ihar\full").Returns(true);
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(false);
        _fs.GetFiles(@"C:\Game\Profiles\owners\ihar\full", "*", SearchOption.TopDirectoryOnly)
            .Returns([@"C:\Game\Profiles\owners\ihar\full\config-cache.wtf"]);
        _fs.GetDirectories(@"C:\Game\Profiles\owners\ihar\full").Returns([]);

        // Act
        _sut.SwitchTo(descriptor);

        // Assert
        _fs.Received().CreateDirectory(@"C:\Game\WTF");
        _fs.Received()
            .CopyFile(
                @"C:\Game\Profiles\owners\ihar\full\config-cache.wtf",
                @"C:\Game\WTF\config-cache.wtf"
            );
    }

    [Test]
    public void SwitchTo_WhenPerAccountDescriptor_ReplacesOnlyAccountSubtreeInWtf()
    {
        // Arrange
        const string snapshotAccountPath = @"C:\Game\Profiles\owners\ihar\account__MyAccount\Account\MyAccount";
        var descriptor = new ProfileDescriptor
        {
            Id = "ihar-account",
            Granularity = ProfileGranularity.PerAccount,
            AccountName = "MyAccount",
            SnapshotPath = @"C:\Game\Profiles\owners\ihar\account__MyAccount",
        };
        _fs.DirectoryExists(snapshotAccountPath).Returns(true);
        _fs.DirectoryExists(@"C:\Game\WTF\Account\MyAccount").Returns(false);
        _fs.GetFiles(snapshotAccountPath, "*", SearchOption.TopDirectoryOnly)
            .Returns([@"C:\Game\Profiles\owners\ihar\account__MyAccount\Account\MyAccount\config-cache.wtf"]);
        _fs.GetDirectories(snapshotAccountPath).Returns([]);

        // Act
        _sut.SwitchTo(descriptor);

        // Assert — only the account sub-directory in WTF is replaced, not the whole WTF root
        _fs.Received().CreateDirectory(@"C:\Game\WTF\Account\MyAccount");
        _fs.DidNotReceive().CreateDirectory(@"C:\Game\WTF");
        _fs.DidNotReceive().DeleteDirectory(@"C:\Game\WTF", Arg.Any<bool>());
    }

    [Test]
    public void SwitchTo_WhenPerAccountDescriptor_WritesActiveMarkerWithGranularityAndAccount()
    {
        // Arrange
        const string snapshotAccountPath = @"C:\Game\Profiles\owners\ihar\account__MyAccount\Account\MyAccount";
        var descriptor = new ProfileDescriptor
        {
            Id = "ihar-account",
            Granularity = ProfileGranularity.PerAccount,
            AccountName = "MyAccount",
            SnapshotPath = @"C:\Game\Profiles\owners\ihar\account__MyAccount",
        };
        _fs.DirectoryExists(snapshotAccountPath).Returns(true);
        _fs.GetFiles(snapshotAccountPath, "*", SearchOption.TopDirectoryOnly).Returns([]);
        _fs.GetDirectories(snapshotAccountPath).Returns([]);

        // Act
        _sut.SwitchTo(descriptor);

        // Assert
        _fs.Received()
            .WriteAllText(
                @"C:\Game\Profiles\.active",
                Arg.Is<string>(json =>
                    IsActiveMarkerWithAccount(
                        json,
                        "ihar-account",
                        ProfileGranularity.PerAccount,
                        "MyAccount"
                    )
                )
            );
    }

    [Test]
    public void SwitchTo_WhenPerAccountSnapshotFolderMissing_Throws()
    {
        // Arrange
        var descriptor = new ProfileDescriptor
        {
            Id = "ihar-account",
            Granularity = ProfileGranularity.PerAccount,
            AccountName = "MyAccount",
            SnapshotPath = @"C:\Game\Profiles\owners\ihar\account__MyAccount",
        };
        // snapshot account folder does not exist (default from SetUp)

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => _sut.SwitchTo(descriptor));
        ex.Message.ShouldContain("Account snapshot folder not found");
    }

    [Test]
    public void SwitchTo_WhenPerCharacterDescriptor_ReplacesOnlyCharacterSubtreeInWtf()
    {
        // Arrange
        const string snapshotCharPath =
            @"C:\snapshot\Account\MyAccount\Firemaw\HeroChar";
        var descriptor = new ProfileDescriptor
        {
            Id = "ihar-char",
            Granularity = ProfileGranularity.PerCharacter,
            AccountName = "MyAccount",
            RealmName = "Firemaw",
            CharacterName = "HeroChar",
            SnapshotPath = @"C:\snapshot",
        };
        _fs.DirectoryExists(snapshotCharPath).Returns(true);
        _fs.GetFiles(snapshotCharPath, "*", SearchOption.TopDirectoryOnly).Returns([]);
        _fs.GetDirectories(snapshotCharPath).Returns([]);
        // account-level files in snapshot
        _fs.DirectoryExists(@"C:\snapshot\Account\MyAccount").Returns(false);

        // Act
        _sut.SwitchTo(descriptor);

        // Assert — only the specific character subfolder is created
        _fs.Received().CreateDirectory(@"C:\Game\WTF\Account\MyAccount\Firemaw\HeroChar");
        _fs.DidNotReceive().CreateDirectory(@"C:\Game\WTF");
        _fs.DidNotReceive().DeleteDirectory(@"C:\Game\WTF", Arg.Any<bool>());
    }

    [Test]
    public void SwitchTo_WhenPerCharacterDescriptor_RestoresAccountLevelFilesFromSnapshot()
    {
        // Arrange
        const string snapshotCharPath = @"C:\snapshot\Account\MyAccount\Firemaw\HeroChar";
        const string snapshotAccountPath = @"C:\snapshot\Account\MyAccount";
        const string accountFile = @"C:\snapshot\Account\MyAccount\config-cache.wtf";
        var descriptor = new ProfileDescriptor
        {
            Id = "ihar-char",
            Granularity = ProfileGranularity.PerCharacter,
            AccountName = "MyAccount",
            RealmName = "Firemaw",
            CharacterName = "HeroChar",
            SnapshotPath = @"C:\snapshot",
        };
        _fs.DirectoryExists(snapshotCharPath).Returns(true);
        _fs.DirectoryExists(snapshotAccountPath).Returns(true);
        _fs.DirectoryExists(@"C:\Game\WTF\Account\MyAccount").Returns(true);
        _fs.GetFiles(snapshotCharPath, "*", SearchOption.TopDirectoryOnly).Returns([]);
        _fs.GetDirectories(snapshotCharPath).Returns([]);
        _fs.GetFiles(snapshotAccountPath, "*", SearchOption.TopDirectoryOnly)
            .Returns([accountFile]);

        // Act
        _sut.SwitchTo(descriptor);

        // Assert — account-level file is copied into live account folder
        _fs.Received()
            .CopyFile(accountFile, @"C:\Game\WTF\Account\MyAccount\config-cache.wtf");
    }

    [Test]
    public void SwitchTo_WhenPerCharacterDescriptor_DoesNotTouchSiblingCharacterFolders()
    {
        // Arrange
        const string snapshotCharPath = @"C:\snapshot\Account\MyAccount\Firemaw\HeroChar";
        const string siblingCharPath = @"C:\Game\WTF\Account\MyAccount\Firemaw\SiblingChar";
        var descriptor = new ProfileDescriptor
        {
            Id = "ihar-char",
            Granularity = ProfileGranularity.PerCharacter,
            AccountName = "MyAccount",
            RealmName = "Firemaw",
            CharacterName = "HeroChar",
            SnapshotPath = @"C:\snapshot",
        };
        _fs.DirectoryExists(snapshotCharPath).Returns(true);
        _fs.GetFiles(snapshotCharPath, "*", SearchOption.TopDirectoryOnly).Returns([]);
        _fs.GetDirectories(snapshotCharPath).Returns([]);
        _fs.DirectoryExists(@"C:\snapshot\Account\MyAccount").Returns(false);

        // Act
        _sut.SwitchTo(descriptor);

        // Assert — sibling character folder is never deleted or copied from
        _fs.DidNotReceive().DeleteDirectory(siblingCharPath, Arg.Any<bool>());
        _fs.DidNotReceive().CopyFile(Arg.Is<string>(s => s.Contains("SiblingChar")), Arg.Any<string>());
    }

    [Test]
    public void SwitchTo_WhenPerCharacterSnapshotFolderMissing_Throws()
    {
        // Arrange
        var descriptor = new ProfileDescriptor
        {
            Id = "ihar-char",
            Granularity = ProfileGranularity.PerCharacter,
            AccountName = "MyAccount",
            RealmName = "Firemaw",
            CharacterName = "HeroChar",
            SnapshotPath = @"C:\snapshot",
        };
        // character snapshot folder does not exist (default from SetUp)

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => _sut.SwitchTo(descriptor));
        ex.Message.ShouldContain("Character snapshot folder not found");
    }

    [Test]
    public void SaveCurrentAsProfile_WhenPerAccountDescriptor_CapturesAccountSubtreeToSnapshot()
    {
        // Arrange
        const string liveAccountPath = @"C:\Game\WTF\Account\MyAccount";
        const string snapshotAccountPath = @"C:\snapshot\Account\MyAccount";
        var descriptor = new ProfileDescriptor
        {
            Id = "ihar-account",
            Granularity = ProfileGranularity.PerAccount,
            AccountName = "MyAccount",
            SnapshotPath = @"C:\snapshot",
        };
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _fs.DirectoryExists(liveAccountPath).Returns(true);
        _fs.DirectoryExists(snapshotAccountPath).Returns(false);
        _fs.GetFiles(liveAccountPath, "*", SearchOption.TopDirectoryOnly)
            .Returns([@"C:\Game\WTF\Account\MyAccount\config-cache.wtf"]);
        _fs.GetDirectories(liveAccountPath).Returns([]);

        // Act
        _sut.SaveCurrentAsProfile(descriptor);

        // Assert — account subfolder is copied to snapshot; WTF root is not touched
        _fs.Received().CreateDirectory(snapshotAccountPath);
        _fs.Received()
            .CopyFile(
                @"C:\Game\WTF\Account\MyAccount\config-cache.wtf",
                @"C:\snapshot\Account\MyAccount\config-cache.wtf"
            );
        _fs.DidNotReceive().DeleteDirectory(@"C:\Game\WTF", Arg.Any<bool>());
    }

    [Test]
    public void SaveCurrentAsProfile_WhenPerAccountDescriptor_AccountFolderMissingInWtf_Throws()
    {
        // Arrange
        var descriptor = new ProfileDescriptor
        {
            Id = "ihar-account",
            Granularity = ProfileGranularity.PerAccount,
            AccountName = "MyAccount",
            SnapshotPath = @"C:\snapshot",
        };
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(true);
        // account folder does not exist (default from SetUp)

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(
            () => _sut.SaveCurrentAsProfile(descriptor)
        );
        ex.Message.ShouldContain("Account folder not found in WTF");
    }

    [Test]
    public void SaveCurrentAsProfile_WhenPerCharacterDescriptor_CapturesCharacterFolderAndAccountFiles()
    {
        // Arrange
        const string liveCharPath = @"C:\Game\WTF\Account\MyAccount\Firemaw\HeroChar";
        const string liveAccountPath = @"C:\Game\WTF\Account\MyAccount";
        const string accountFile = @"C:\Game\WTF\Account\MyAccount\config-cache.wtf";
        var descriptor = new ProfileDescriptor
        {
            Id = "ihar-char",
            Granularity = ProfileGranularity.PerCharacter,
            AccountName = "MyAccount",
            RealmName = "Firemaw",
            CharacterName = "HeroChar",
            SnapshotPath = @"C:\snapshot",
        };
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _fs.DirectoryExists(liveCharPath).Returns(true);
        _fs.DirectoryExists(liveAccountPath).Returns(true);
        _fs.GetFiles(liveCharPath, "*", SearchOption.TopDirectoryOnly)
            .Returns([@"C:\Game\WTF\Account\MyAccount\Firemaw\HeroChar\bindings-cache.wtf"]);
        _fs.GetDirectories(liveCharPath).Returns([]);
        _fs.GetFiles(liveAccountPath, "*", SearchOption.TopDirectoryOnly)
            .Returns([accountFile]);

        // Act
        _sut.SaveCurrentAsProfile(descriptor);

        // Assert — character folder is captured
        _fs.Received()
            .CopyFile(
                @"C:\Game\WTF\Account\MyAccount\Firemaw\HeroChar\bindings-cache.wtf",
                @"C:\snapshot\Account\MyAccount\Firemaw\HeroChar\bindings-cache.wtf"
            );
        // Assert — account-level files are also captured
        _fs.Received()
            .CopyFile(accountFile, @"C:\snapshot\Account\MyAccount\config-cache.wtf");
        // Assert — other character folders are not deleted
        _fs.DidNotReceive().DeleteDirectory(@"C:\Game\WTF", Arg.Any<bool>());
        _fs.DidNotReceive().DeleteDirectory(liveAccountPath, Arg.Any<bool>());
    }

    [Test]
    public void SaveCurrentAsProfile_WhenPerCharacterDescriptor_CharacterFolderMissingInWtf_Throws()
    {
        // Arrange
        var descriptor = new ProfileDescriptor
        {
            Id = "ihar-char",
            Granularity = ProfileGranularity.PerCharacter,
            AccountName = "MyAccount",
            RealmName = "Firemaw",
            CharacterName = "HeroChar",
            SnapshotPath = @"C:\snapshot",
        };
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(true);
        // character folder does not exist (default from SetUp)

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(
            () => _sut.SaveCurrentAsProfile(descriptor)
        );
        ex.Message.ShouldContain("Character folder not found in WTF");
    }
}
