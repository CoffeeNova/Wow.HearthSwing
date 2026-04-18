using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Models;
using HearthSwing.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class ProfileManagerTests
{
    private IFixture _fixture = null!;
    private IFileSystem _fs = null!;
    private ISettingsService _settings = null!;
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

        _sut = new ProfileManager(_settings, _fs);
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
    public void SwitchTo_WhenTargetFolderNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        var target = new ProfileInfo { Id = "Alice", FolderPath = @"C:\Game\Profiles\Alice" };
        _fs.DirectoryExists(@"C:\Game\Profiles\Alice").Returns(false);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => _sut.SwitchTo(target, _ => { }));
    }

    [Test]
    public void SwitchTo_WhenTargetIsAlreadyActive_LogsAndReturnsEarly()
    {
        // Arrange
        var markerPath = @"C:\Game\Profiles\.active";
        _fs.FileExists(markerPath).Returns(true);
        _fs.ReadAllText(markerPath).Returns("Alice");

        var target = new ProfileInfo { Id = "Alice", FolderPath = @"C:\Game\Profiles\Alice" };
        var logMessages = new List<string>();

        // Act
        _sut.SwitchTo(target, msg => logMessages.Add(msg));

        // Assert
        logMessages.ShouldContain(m => m.Contains("already active"));
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

        var logMessages = new List<string>();

        // Act
        _sut.SwitchTo(target, msg => logMessages.Add(msg));

        // Assert
        _fs.DidNotReceive().DeleteDirectory(@"C:\Game\WTF", true);
        _fs.Received().CreateDirectory(@"C:\Game\WTF");
        _fs.Received().CopyFile(@"C:\Game\Profiles\Alice\Config.wtf", @"C:\Game\WTF\Config.wtf");
        _fs.Received().WriteAllText(@"C:\Game\Profiles\.active", "Alice");
        logMessages.ShouldContain(m => m.Contains("Profile switched to"));
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

        var logMessages = new List<string>();

        // Act
        _sut.SwitchTo(target, msg => logMessages.Add(msg));

        // Assert
        Received.InOrder(() =>
        {
            _fs.DeleteDirectory(@"C:\Game\WTF", true);
            _fs.CreateDirectory(@"C:\Game\WTF");
            _fs.CopyFile(@"C:\Game\Profiles\Alice\Config.wtf", @"C:\Game\WTF\Config.wtf");
        });
        logMessages.ShouldContain(m => m.Contains("Removing current WTF"));
        logMessages.ShouldContain(m => m.Contains("Activating"));
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
        _sut.SwitchTo(target, _ => { });

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
        _sut.SwitchTo(target, _ => { });

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

        var logMessages = new List<string>();

        // Act
        _sut.SwitchTo(target, msg => logMessages.Add(msg));

        // Assert
        _fs.Received().DeleteDirectory(@"C:\Game\WTF", true);
        _fs.Received().CreateDirectory(@"C:\Game\WTF");
        logMessages.ShouldContain(m => m.Contains("Removing current WTF"));
        logMessages.ShouldContain(m => m.Contains("Profile switched to"));
    }

    [Test]
    public void SaveCurrentAsProfile_WhenWtfNotFound_ThrowsInvalidOperationException()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(false);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => _sut.SaveCurrentAsProfile("Test", _ => { }));
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
        _sut.SaveCurrentAsProfile("Test", _ => { });

        // Assert
        _fs.Received().CreateDirectory(@"C:\Game\Profiles\Test");
        _fs.Received().CopyFile(@"C:\Game\WTF\Config.wtf", @"C:\Game\Profiles\Test\Config.wtf");
        _fs.Received().WriteAllText(@"C:\Game\Profiles\.active", "Test");
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

        var logMessages = new List<string>();

        // Act
        _sut.SaveCurrentAsProfile("Test", msg => logMessages.Add(msg));

        // Assert
        _fs.Received().GetFiles(@"C:\Game\Profiles\Test", "*", SearchOption.AllDirectories);
        _fs.Received().GetAttributes(readOnlyFile);
        _fs.Received().SetAttributes(readOnlyFile, FileAttributes.None);
        _fs.Received().DeleteDirectory(@"C:\Game\Profiles\Test", true);
        logMessages.ShouldContain(m => m.Contains("Overwriting"));
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
        _sut.SaveCurrentAsProfile("Test", _ => { });

        // Assert
        _fs.Received().CreateDirectory(@"C:\Game\Profiles");
    }

    [Test]
    public void RestoreActiveProfile_WhenNoActiveMarker_ThrowsInvalidOperationException()
    {
        // Arrange — no .active marker file

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => _sut.RestoreActiveProfile(_ => { }));
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
        var ex = Should.Throw<InvalidOperationException>(() => _sut.RestoreActiveProfile(_ => { }));
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

        var logMessages = new List<string>();

        // Act
        _sut.RestoreActiveProfile(msg => logMessages.Add(msg));

        // Assert
        Received.InOrder(() =>
        {
            _fs.DeleteDirectory(@"C:\Game\WTF", true);
            _fs.CreateDirectory(@"C:\Game\WTF");
            _fs.CopyFile(@"C:\Game\Profiles\Alice\Config.wtf", @"C:\Game\WTF\Config.wtf");
        });
        logMessages.ShouldContain(m => m.Contains("restored"));
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
        _sut.RestoreActiveProfile(_ => { });

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
        _sut.RestoreActiveProfile(_ => { });

        // Assert
        _fs.Received().SetAttributes(readOnlyFile, FileAttributes.None);
        _fs.Received().DeleteDirectory(@"C:\Game\WTF", true);
    }
}
