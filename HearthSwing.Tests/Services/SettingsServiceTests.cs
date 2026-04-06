using System.Text.Json;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using HearthSwing.Models;
using HearthSwing.Services;
using NSubstitute;
using Shouldly;

namespace HearthSwing.Tests.Services;

[TestFixture]
public class SettingsServiceTests
{
    private IFixture _fixture = null!;
    private IFileSystem _fs = null!;
    private SettingsService _sut = null!;

    private const string SettingsPath = @"C:\App\AppSettings.json";

    [SetUp]
    public void SetUp()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _fs = _fixture.Freeze<IFileSystem>();

        _fs.FileExists(Arg.Any<string>()).Returns(false);

        _sut = new SettingsService(_fs, SettingsPath);
    }

    [Test]
    public void Load_WhenFileDoesNotExist_CreatesDefaultsAndSaves()
    {
        // Arrange
        _fs.FileExists(SettingsPath).Returns(false);

        // Act
        _sut.Load();

        // Assert
        _sut.Current.ShouldNotBeNull();
        _sut.Current.UnlockDelaySeconds.ShouldBe(120);
        _fs.Received().WriteAllText(SettingsPath, Arg.Any<string>());
    }

    [Test]
    public void Load_WhenFileExists_DeserializesSettings()
    {
        // Arrange
        _fs.FileExists(SettingsPath).Returns(true);
        _fs.ReadAllText(SettingsPath)
            .Returns(
                """
                {
                    "GamePath": "C:\\Game",
                    "ProfilesPath": "C:\\Game\\Profiles",
                    "UnlockDelaySeconds": 60
                }
                """
            );

        // Act
        _sut.Load();

        // Assert
        _sut.Current.GamePath.ShouldBe(@"C:\Game");
        _sut.Current.ProfilesPath.ShouldBe(@"C:\Game\Profiles");
        _sut.Current.UnlockDelaySeconds.ShouldBe(60);
    }

    [Test]
    public void Load_WhenFileIsCorrupt_FallsBackToDefaults()
    {
        // Arrange
        _fs.FileExists(SettingsPath).Returns(true);
        _fs.ReadAllText(SettingsPath).Returns("not json");

        // Act
        _sut.Load();

        // Assert
        _sut.Current.ShouldNotBeNull();
        _sut.Current.UnlockDelaySeconds.ShouldBe(120);
    }

    [Test]
    public void Load_WhenJsonDeserializesToNull_FallsBackToDefaults()
    {
        // Arrange
        _fs.FileExists(SettingsPath).Returns(true);
        _fs.ReadAllText(SettingsPath).Returns("null");

        // Act
        _sut.Load();

        // Assert
        _sut.Current.ShouldNotBeNull();
        _sut.Current.UnlockDelaySeconds.ShouldBe(120);
    }

    [Test]
    public void Save_WritesValidJsonToFile()
    {
        // Arrange
        _sut.Current.GamePath = @"C:\Game";
        _sut.Current.ProfilesPath = @"C:\Game\Profiles";
        _sut.Current.UnlockDelaySeconds = 90;

        string? capturedJson = null;
        _fs.When(x => x.WriteAllText(SettingsPath, Arg.Any<string>()))
            .Do(ci => capturedJson = ci.ArgAt<string>(1));

        // Act
        _sut.Save();

        // Assert
        capturedJson.ShouldNotBeNull();
        var deserialized = JsonSerializer.Deserialize<AppSettings>(capturedJson);
        deserialized.ShouldNotBeNull();
        deserialized.GamePath.ShouldBe(@"C:\Game");
        deserialized.UnlockDelaySeconds.ShouldBe(90);
    }

    [Test]
    public void Save_ProducesIndentedJson()
    {
        // Act
        _sut.Save();

        // Assert
        _fs.Received()
            .WriteAllText(
                SettingsPath,
                Arg.Is<string>(json => json.Contains("\n") && json.Contains("  "))
            );
    }

    [Test]
    public void Load_WhenFileHasExtraProperties_IgnoresThemGracefully()
    {
        // Arrange
        _fs.FileExists(SettingsPath).Returns(true);
        _fs.ReadAllText(SettingsPath)
            .Returns(
                """
                {
                    "GamePath": "C:\\Game",
                    "ProfilesPath": "C:\\Game\\Profiles",
                    "UnlockDelaySeconds": 60,
                    "SomeUnknownProperty": "value"
                }
                """
            );

        // Act
        _sut.Load();

        // Assert
        _sut.Current.GamePath.ShouldBe(@"C:\Game");
        _sut.Current.UnlockDelaySeconds.ShouldBe(60);
    }
}
