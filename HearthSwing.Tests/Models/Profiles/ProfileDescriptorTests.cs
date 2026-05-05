using HearthSwing.Models.Profiles;
using Shouldly;

namespace HearthSwing.Tests.Models.Profiles;

[TestFixture]
public class ProfileDescriptorTests
{
    [Test]
    public void Validate_WhenGranularityIsFullWtf_AllowsDescriptorWithoutAccountContext()
    {
        // Arrange
        var descriptor = new ProfileDescriptor
        {
            Id = "alpha",
            Granularity = ProfileGranularity.FullWtf,
            SnapshotPath = @"C:\Profiles\alpha",
        };

        // Act & Assert
        Should.NotThrow(() => descriptor.Validate());
    }

    [Test]
    public void Validate_WhenGranularityIsPerAccount_RequiresAccountName()
    {
        // Arrange
        var descriptor = new ProfileDescriptor
        {
            Id = "alpha",
            Granularity = ProfileGranularity.PerAccount,
            SnapshotPath = @"C:\Profiles\alpha",
        };

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => descriptor.Validate());
        ex.Message.ShouldContain("Account name is required");
    }

    [Test]
    public void Validate_WhenGranularityIsPerCharacter_RequiresAllCharacterCoordinates()
    {
        // Arrange
        var descriptor = new ProfileDescriptor
        {
            Id = "alpha",
            Granularity = ProfileGranularity.PerCharacter,
            AccountName = "AccountA",
            SnapshotPath = @"C:\Profiles\alpha",
        };

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => descriptor.Validate());
        ex.Message.ShouldContain("Account, realm, and character names are required");
    }

    [Test]
    public void ToActiveProfileState_MapsDescriptorCoordinates()
    {
        // Arrange
        var descriptor = new ProfileDescriptor
        {
            Id = "alpha",
            LocalProfileId = "ihar",
            Granularity = ProfileGranularity.PerCharacter,
            AccountName = "AccountA",
            RealmName = "Firemaw",
            CharacterName = "CharacterA",
            SnapshotPath = @"C:\Profiles\alpha",
            VersionId = "v-1",
        };

        // Act
        var activeState = descriptor.ToActiveProfileState();

        // Assert
        activeState.Id.ShouldBe("alpha");
        activeState.LocalProfileId.ShouldBe("ihar");
        activeState.Granularity.ShouldBe(ProfileGranularity.PerCharacter);
        activeState.AccountName.ShouldBe("AccountA");
        activeState.RealmName.ShouldBe("Firemaw");
        activeState.CharacterName.ShouldBe("CharacterA");
        activeState.SnapshotPath.ShouldBe(@"C:\Profiles\alpha");
        activeState.VersionId.ShouldBe("v-1");
    }
}
