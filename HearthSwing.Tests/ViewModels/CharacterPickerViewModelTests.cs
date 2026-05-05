using HearthSwing.Models.Profiles;
using HearthSwing.Models.WoW;
using HearthSwing.ViewModels;
using Shouldly;

namespace HearthSwing.Tests.ViewModels;

[TestFixture]
public class CharacterPickerViewModelTests
{
    private static WowInstallation BuildInstallation(
        string[] accountNames = null!,
        string[]? realms = null,
        string[]? characters = null
    )
    {
        accountNames ??= ["Account1"];
        realms ??= ["Realm1"];
        characters ??= ["CharA", "CharB"];

        var accounts = accountNames.Select(account =>
            new WowAccount
            {
                AccountName = account,
                FolderPath = @$"C:\WTF\Account\{account}",
                Realms = realms
                    .Select(realm => new WowRealm
                    {
                        AccountName = account,
                        RealmName = realm,
                        FolderPath = @$"C:\WTF\Account\{account}\{realm}",
                        Characters = characters
                            .Select(c => new WowCharacter
                            {
                                AccountName = account,
                                RealmName = realm,
                                CharacterName = c,
                                FolderPath = @$"C:\WTF\Account\{account}\{realm}\{c}",
                            })
                            .ToList(),
                    })
                    .ToList(),
            }
        ).ToList();

        return new WowInstallation
        {
            GamePath = @"C:\Game",
            WtfPath = @"C:\Game\WTF",
            Accounts = accounts,
        };
    }

    // ─── Refresh ────────────────────────────────────────────────────────────────

    [Test]
    public void Refresh_PopulatesAccountList()
    {
        // Arrange
        var sut = new CharacterPickerViewModel();
        var installation = BuildInstallation(["AccountA", "AccountB"]);

        // Act
        sut.Refresh(installation);

        // Assert
        sut.Accounts.ShouldBe(["AccountA", "AccountB"]);
    }

    [Test]
    public void Refresh_WithSingleAccount_AutoSelectsIt()
    {
        // Arrange
        var sut = new CharacterPickerViewModel();
        var installation = BuildInstallation(["OnlyAccount"]);

        // Act
        sut.Refresh(installation);

        // Assert
        sut.SelectedAccount.ShouldBe("OnlyAccount");
    }

    [Test]
    public void Refresh_WithMultipleAccounts_DoesNotAutoSelect()
    {
        // Arrange
        var sut = new CharacterPickerViewModel();
        var installation = BuildInstallation(["A", "B"]);

        // Act
        sut.Refresh(installation);

        // Assert
        sut.SelectedAccount.ShouldBeNull();
    }

    [Test]
    public void Refresh_PreservesPreviousAccountSelection()
    {
        // Arrange
        var sut = new CharacterPickerViewModel();
        sut.Refresh(BuildInstallation(["A", "B"]));
        sut.SelectedAccount = "B";

        // Act — refresh again with the same accounts
        sut.Refresh(BuildInstallation(["A", "B"]));

        // Assert
        sut.SelectedAccount.ShouldBe("B");
    }

    // ─── Cascade selection ──────────────────────────────────────────────────────

    [Test]
    public void SelectedAccount_PopulatesRealms()
    {
        // Arrange
        var sut = new CharacterPickerViewModel();
        sut.Refresh(BuildInstallation(realms: ["Realm1", "Realm2"]));

        // Act
        sut.SelectedAccount = "Account1";

        // Assert
        sut.Realms.ShouldBe(["Realm1", "Realm2"]);
    }

    [Test]
    public void SelectedAccount_WithSingleRealm_AutoSelectsRealm()
    {
        // Arrange
        var sut = new CharacterPickerViewModel();
        sut.Refresh(BuildInstallation(realms: ["OnlyRealm"]));

        // Act
        sut.SelectedAccount = "Account1";

        // Assert
        sut.SelectedRealm.ShouldBe("OnlyRealm");
    }

    [Test]
    public void SelectedRealm_PopulatesCharacters()
    {
        // Arrange
        var sut = new CharacterPickerViewModel();
        sut.Refresh(BuildInstallation());
        sut.SelectedAccount = "Account1";

        // Act
        sut.SelectedRealm = "Realm1";

        // Assert
        sut.Characters.ShouldBe(["CharA", "CharB"]);
    }

    [Test]
    public void SelectedRealm_WithSingleCharacter_AutoSelectsCharacter()
    {
        // Arrange
        var sut = new CharacterPickerViewModel();
        sut.Refresh(BuildInstallation(characters: ["OnlyChar"]));
        sut.SelectedAccount = "Account1";

        // Act
        sut.SelectedRealm = "Realm1";

        // Assert
        sut.SelectedCharacter.ShouldBe("OnlyChar");
    }

    [Test]
    public void SettingSelectedAccountToNull_ClearsRealmsAndCharacters()
    {
        // Arrange
        var sut = new CharacterPickerViewModel();
        sut.Refresh(BuildInstallation());
        sut.SelectedAccount = "Account1";
        sut.SelectedRealm = "Realm1";

        // Act
        sut.SelectedAccount = null;

        // Assert
        sut.Realms.ShouldBeEmpty();
        sut.Characters.ShouldBeEmpty();
    }

    // ─── Auto save mode ─────────────────────────────────────────────────────────

    [Test]
    public void SaveMode_DefaultsToFullWtfWhenNothingSelected()
    {
        // Arrange / Act
        var sut = new CharacterPickerViewModel();
        sut.Refresh(BuildInstallation(["A", "B"]));

        // Assert
        sut.SaveMode.ShouldBe(ProfileGranularity.FullWtf);
    }

    [Test]
    public void SaveMode_BecomesPerAccountWhenAccountSelected()
    {
        // Arrange
        var sut = new CharacterPickerViewModel();
        sut.Refresh(BuildInstallation(["A", "B"]));

        // Act
        sut.SelectedAccount = "A";

        // Assert
        sut.SaveMode.ShouldBe(ProfileGranularity.PerAccount);
    }

    [Test]
    public void SaveMode_BecomesPerCharacterWhenCharacterSelected()
    {
        // Arrange
        var sut = new CharacterPickerViewModel();
        sut.Refresh(BuildInstallation());
        sut.SelectedAccount = "Account1";
        sut.SelectedRealm = "Realm1";

        // Act
        sut.SelectedCharacter = "CharA";

        // Assert
        sut.SaveMode.ShouldBe(ProfileGranularity.PerCharacter);
    }

    [Test]
    public void SaveMode_ReturnsToPerAccountWhenCharacterCleared()
    {
        // Arrange
        var sut = new CharacterPickerViewModel();
        sut.Refresh(BuildInstallation());
        sut.SelectedAccount = "Account1";
        sut.SelectedRealm = "Realm1";
        sut.SelectedCharacter = "CharA";

        // Act
        sut.SelectedCharacter = null;

        // Assert
        sut.SaveMode.ShouldBe(ProfileGranularity.PerAccount);
    }

    // ─── CanBuildDescriptor ─────────────────────────────────────────────────────

    [Test]
    public void CanBuildDescriptor_FalseWhenLocalProfileIdEmpty()
    {
        // Arrange
        var sut = new CharacterPickerViewModel { SaveMode = ProfileGranularity.FullWtf };

        // Assert
        sut.CanBuildDescriptor.ShouldBeFalse();
    }

    [Test]
    public void CanBuildDescriptor_TrueForFullWtfWithLocalProfileId()
    {
        // Arrange
        var sut = new CharacterPickerViewModel
        {
            LocalProfileId = "Ihar",
            SaveMode = ProfileGranularity.FullWtf,
        };

        // Assert
        sut.CanBuildDescriptor.ShouldBeTrue();
    }

    [Test]
    public void CanBuildDescriptor_FalseForPerAccountWithoutAccount()
    {
        // Arrange
        var sut = new CharacterPickerViewModel
        {
            LocalProfileId = "Ihar",
            SaveMode = ProfileGranularity.PerAccount,
        };

        // Assert
        sut.CanBuildDescriptor.ShouldBeFalse();
    }

    [Test]
    public void CanBuildDescriptor_TrueForPerAccountWithAccount()
    {
        // Arrange
        var sut = new CharacterPickerViewModel
        {
            LocalProfileId = "Ihar",
            SaveMode = ProfileGranularity.PerAccount,
        };
        sut.Refresh(BuildInstallation());
        sut.SelectedAccount = "Account1";

        // Assert
        sut.CanBuildDescriptor.ShouldBeTrue();
    }

    [Test]
    public void CanBuildDescriptor_FalseForPerCharacterWithoutAllFields()
    {
        // Arrange — directly set properties: PerCharacter requires all three coordinates
        var sut = new CharacterPickerViewModel
        {
            LocalProfileId = "Ihar",
            // SelectedAccount, SelectedRealm, SelectedCharacter are null
            SaveMode = ProfileGranularity.PerCharacter,
        };

        // Verify preconditions
        sut.SaveMode.ShouldBe(ProfileGranularity.PerCharacter);
        sut.SelectedAccount.ShouldBeNull();
        sut.SelectedRealm.ShouldBeNull();
        sut.SelectedCharacter.ShouldBeNull();

        // Assert — missing account/realm/character
        sut.CanBuildDescriptor.ShouldBeFalse();
    }

    // ─── TryBuildDescriptor ─────────────────────────────────────────────────────

    [Test]
    public void TryBuildDescriptor_ReturnsNullWhenCannotBuild()
    {
        // Arrange
        var sut = new CharacterPickerViewModel();

        // Act
        var descriptor = sut.TryBuildDescriptor(@"C:\Profiles");

        // Assert
        descriptor.ShouldBeNull();
    }

    [Test]
    public void TryBuildDescriptor_FullWtf_BuildsCorrectId()
    {
        // Arrange
        var sut = new CharacterPickerViewModel
        {
            LocalProfileId = "Ihar",
            SaveMode = ProfileGranularity.FullWtf,
        };

        // Act
        var descriptor = sut.TryBuildDescriptor(@"C:\Profiles");

        // Assert
        descriptor.ShouldNotBeNull();
        descriptor.Id.ShouldBe("Ihar");
        descriptor.Granularity.ShouldBe(ProfileGranularity.FullWtf);
        descriptor.SnapshotPath.ShouldBe(@"C:\Profiles\Ihar");
    }

    [Test]
    public void TryBuildDescriptor_PerAccount_BuildsCorrectId()
    {
        // Arrange
        var sut = new CharacterPickerViewModel
        {
            LocalProfileId = "Ihar",
            SaveMode = ProfileGranularity.PerAccount,
        };
        sut.Refresh(BuildInstallation(["MyAccount"]));
        sut.SelectedAccount = "MyAccount";

        // Act
        var descriptor = sut.TryBuildDescriptor(@"C:\Profiles");

        // Assert
        descriptor.ShouldNotBeNull();
        descriptor.Id.ShouldBe("Ihar__account__MyAccount");
        descriptor.Granularity.ShouldBe(ProfileGranularity.PerAccount);
        descriptor.AccountName.ShouldBe("MyAccount");
        descriptor.SnapshotPath.ShouldBe(@"C:\Profiles\Ihar__account__MyAccount");
    }

    [Test]
    public void TryBuildDescriptor_PerCharacter_BuildsCorrectId()
    {
        // Arrange
        var sut = new CharacterPickerViewModel
        {
            LocalProfileId = "Ihar",
            SaveMode = ProfileGranularity.PerCharacter,
        };
        sut.Refresh(BuildInstallation(["MyAccount"], ["Firemaw"], ["CharA"]));
        sut.SelectedAccount = "MyAccount";
        sut.SelectedRealm = "Firemaw";
        sut.SelectedCharacter = "CharA";

        // Act
        var descriptor = sut.TryBuildDescriptor(@"C:\Profiles");

        // Assert
        descriptor.ShouldNotBeNull();
        descriptor.Id.ShouldBe("Ihar__character__MyAccount__Firemaw__CharA");
        descriptor.Granularity.ShouldBe(ProfileGranularity.PerCharacter);
        descriptor.AccountName.ShouldBe("MyAccount");
        descriptor.RealmName.ShouldBe("Firemaw");
        descriptor.CharacterName.ShouldBe("CharA");
        descriptor.DisplayName.ShouldBe("Ihar / CharA (Firemaw)");
    }
}
