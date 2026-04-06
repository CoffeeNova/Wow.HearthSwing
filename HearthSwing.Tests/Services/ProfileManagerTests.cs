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
        _fs.GetFiles(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<SearchOption>())
            .Returns([]);
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
    public void DiscoverProfiles_WhenActiveMarkerPointsToAbsentFolder_AddsActiveProfile()
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
        result.Count.ShouldBe(2);
        var alice = result.First(p => p.Id == "Alice");
        alice.IsActive.ShouldBeTrue();
        alice.FolderPath.ShouldBe(@"C:\Game\Profiles\Alice");
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
        _fs.DidNotReceive().MoveDirectory(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public void SwitchTo_WhenNoWtfAndNoCurrentProfile_MovesTargetToWtf()
    {
        // Arrange
        var target = new ProfileInfo { Id = "Alice", FolderPath = @"C:\Game\Profiles\Alice" };
        _fs.DirectoryExists(@"C:\Game\Profiles\Alice").Returns(true);
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(false);
        _fs.DirectoryExists(@"C:\Game\Profiles").Returns(true);

        var logMessages = new List<string>();

        // Act
        _sut.SwitchTo(target, msg => logMessages.Add(msg));

        // Assert
        _fs.Received().MoveDirectory(@"C:\Game\Profiles\Alice", @"C:\Game\WTF");
        _fs.Received().WriteAllText(@"C:\Game\Profiles\.active", "Alice");
        logMessages.ShouldContain(m => m.Contains("Profile switched to"));
    }

    [Test]
    public void SwitchTo_WhenCurrentProfileExists_ParksCurrentThenActivatesTarget()
    {
        // Arrange — set up "Bob" as active
        var markerPath = @"C:\Game\Profiles\.active";
        _fs.FileExists(markerPath).Returns(true);
        _fs.ReadAllText(markerPath).Returns("Bob");
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _fs.DirectoryExists(@"C:\Game\Profiles\Bob").Returns(false);

        var target = new ProfileInfo { Id = "Alice", FolderPath = @"C:\Game\Profiles\Alice" };
        _fs.DirectoryExists(@"C:\Game\Profiles\Alice").Returns(true);
        _fs.DirectoryExists(@"C:\Game\Profiles").Returns(true);

        var logMessages = new List<string>();

        // Act
        _sut.SwitchTo(target, msg => logMessages.Add(msg));

        // Assert
        Received.InOrder(() =>
        {
            _fs.MoveDirectory(@"C:\Game\WTF", @"C:\Game\Profiles\Bob");
            _fs.MoveDirectory(@"C:\Game\Profiles\Alice", @"C:\Game\WTF");
        });
        logMessages.ShouldContain(m => m.Contains("Parking"));
        logMessages.ShouldContain(m => m.Contains("Activating"));
    }

    [Test]
    public void SwitchTo_WhenParkedFolderAlreadyExists_ThrowsInvalidOperationException()
    {
        // Arrange — "Bob" is active, but Bob's profile folder already exists (broken state)
        var markerPath = @"C:\Game\Profiles\.active";
        _fs.FileExists(markerPath).Returns(true);
        _fs.ReadAllText(markerPath).Returns("Bob");
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _fs.DirectoryExists(@"C:\Game\Profiles\Bob").Returns(true);

        var target = new ProfileInfo { Id = "Alice", FolderPath = @"C:\Game\Profiles\Alice" };
        _fs.DirectoryExists(@"C:\Game\Profiles\Alice").Returns(true);

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => _sut.SwitchTo(target, _ => { }));
        ex.Message.ShouldContain("Cannot park current profile");
    }

    [Test]
    public void SwitchTo_WhenParkFails_ThrowsWithOriginalMessage()
    {
        // Arrange
        var markerPath = @"C:\Game\Profiles\.active";
        _fs.FileExists(markerPath).Returns(true);
        _fs.ReadAllText(markerPath).Returns("Bob");
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _fs.DirectoryExists(@"C:\Game\Profiles\Bob").Returns(false);
        _fs.When(x => x.MoveDirectory(@"C:\Game\WTF", @"C:\Game\Profiles\Bob"))
            .Do(_ => throw new IOException("disk full"));

        var target = new ProfileInfo { Id = "Alice", FolderPath = @"C:\Game\Profiles\Alice" };
        _fs.DirectoryExists(@"C:\Game\Profiles\Alice").Returns(true);

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => _sut.SwitchTo(target, _ => { }));
        ex.Message.ShouldContain("Failed to park current profile");
        ex.Message.ShouldContain("disk full");
    }

    [Test]
    public void SwitchTo_WhenActivationFails_RollsBackParkedProfile()
    {
        // Arrange — "Bob" is active, parking succeeds, activation fails
        var markerPath = @"C:\Game\Profiles\.active";
        _fs.FileExists(markerPath).Returns(true);
        _fs.ReadAllText(markerPath).Returns("Bob");
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _fs.DirectoryExists(@"C:\Game\Profiles\Bob").Returns(false);
        _fs.DirectoryExists(@"C:\Game\Profiles\Alice").Returns(true);
        _fs.DirectoryExists(@"C:\Game\Profiles").Returns(true);

        // First MoveDirectory (park) succeeds, second (activate) fails
        var callCount = 0;
        _fs.When(x => x.MoveDirectory(Arg.Any<string>(), Arg.Any<string>()))
            .Do(_ =>
            {
                callCount++;
                if (callCount == 2)
                    throw new IOException("permission denied");
            });

        var target = new ProfileInfo { Id = "Alice", FolderPath = @"C:\Game\Profiles\Alice" };
        var logMessages = new List<string>();

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() =>
            _sut.SwitchTo(target, msg => logMessages.Add(msg))
        );
        ex.Message.ShouldContain("Failed to activate target profile");
        logMessages.ShouldContain(m => m.Contains("Rolling back"));

        // Rollback = 3rd MoveDirectory call
        callCount.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Test]
    public void SwitchTo_WhenWtfExistsButNoActiveMarker_BacksUpWtfBeforeSwitch()
    {
        // Arrange — WTF exists but no .active marker
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _fs.DirectoryExists(@"C:\Game\Profiles").Returns(true);

        var target = new ProfileInfo { Id = "Alice", FolderPath = @"C:\Game\Profiles\Alice" };
        _fs.DirectoryExists(@"C:\Game\Profiles\Alice").Returns(true);

        var logMessages = new List<string>();

        // Act
        _sut.SwitchTo(target, msg => logMessages.Add(msg));

        // Assert
        logMessages.ShouldContain(m => m.Contains("No active profile known"));
        logMessages.ShouldContain(m => m.Contains("Backing up"));
        // First move = backup WTF → _backup_*, second = Alice → WTF
        _fs.Received(2).MoveDirectory(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public void SwitchTo_WhenBackupFails_ThrowsWithoutModifyingTarget()
    {
        // Arrange — WTF exists, no marker, backup move fails
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _fs.DirectoryExists(@"C:\Game\Profiles").Returns(true);
        _fs.When(x => x.MoveDirectory(Arg.Any<string>(), Arg.Any<string>()))
            .Do(_ => throw new IOException("backup failed"));

        var target = new ProfileInfo { Id = "Alice", FolderPath = @"C:\Game\Profiles\Alice" };
        _fs.DirectoryExists(@"C:\Game\Profiles\Alice").Returns(true);

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => _sut.SwitchTo(target, _ => { }));
        ex.Message.ShouldContain("Failed to back up current WTF");
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
    public void SaveCurrentAsProfile_WhenProfileAlreadyExists_DeletesThenCopies()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _fs.DirectoryExists(@"C:\Game\Profiles").Returns(true);
        _fs.DirectoryExists(@"C:\Game\Profiles\Test").Returns(true);
        _fs.GetFiles(@"C:\Game\WTF", "*", SearchOption.TopDirectoryOnly)
            .Returns([]);
        _fs.GetDirectories(@"C:\Game\WTF").Returns([]);

        var logMessages = new List<string>();

        // Act
        _sut.SaveCurrentAsProfile("Test", msg => logMessages.Add(msg));

        // Assert
        _fs.Received().DeleteDirectory(@"C:\Game\Profiles\Test", true);
        logMessages.ShouldContain(m => m.Contains("Overwriting"));
    }

    [Test]
    public void SaveCurrentAsProfile_WhenProfilesPathMissing_CreatesIt()
    {
        // Arrange
        _fs.DirectoryExists(@"C:\Game\WTF").Returns(true);
        _fs.DirectoryExists(@"C:\Game\Profiles").Returns(false);
        _fs.GetFiles(@"C:\Game\WTF", "*", SearchOption.TopDirectoryOnly)
            .Returns([]);
        _fs.GetDirectories(@"C:\Game\WTF").Returns([]);

        // Act
        _sut.SaveCurrentAsProfile("Test", _ => { });

        // Assert
        _fs.Received().CreateDirectory(@"C:\Game\Profiles");
    }
}
