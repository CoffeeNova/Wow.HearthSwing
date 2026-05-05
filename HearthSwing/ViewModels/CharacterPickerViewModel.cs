using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using HearthSwing.Models.Profiles;
using HearthSwing.Models.WoW;

namespace HearthSwing.ViewModels;

public partial class CharacterPickerViewModel : ObservableObject
{
    private WowInstallation? _installation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanBuildDescriptor))]
    private string _localProfileId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanBuildDescriptor))]
    private string? _selectedAccount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanBuildDescriptor))]
    private string? _selectedRealm;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanBuildDescriptor))]
    private string? _selectedCharacter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanBuildDescriptor))]
    private ProfileGranularity _saveMode = ProfileGranularity.FullWtf;

    [ObservableProperty]
    private bool _isCharacterModeEnabled;

    /// <summary>Two-way bridge for FullWtf <see cref="SaveMode"/> radio button.</summary>
    public bool IsSaveModeFullWtf
    {
        get => SaveMode == ProfileGranularity.FullWtf;
        set { if (value) SaveMode = ProfileGranularity.FullWtf; }
    }

    /// <summary>Two-way bridge for PerAccount <see cref="SaveMode"/> radio button.</summary>
    public bool IsSaveModePerAccount
    {
        get => SaveMode == ProfileGranularity.PerAccount;
        set { if (value) SaveMode = ProfileGranularity.PerAccount; }
    }

    /// <summary>Two-way bridge for PerCharacter <see cref="SaveMode"/> radio button.</summary>
    public bool IsSaveModePerCharacter
    {
        get => SaveMode == ProfileGranularity.PerCharacter;
        set { if (value) SaveMode = ProfileGranularity.PerCharacter; }
    }

    public ObservableCollection<string> Accounts { get; } = [];
    public ObservableCollection<string> Realms { get; } = [];
    public ObservableCollection<string> Characters { get; } = [];

    /// <summary>True when the current selection is complete enough to produce a valid descriptor.</summary>
    public bool CanBuildDescriptor =>
        !string.IsNullOrWhiteSpace(LocalProfileId)
        && SaveMode switch
        {
            ProfileGranularity.FullWtf => true,
            ProfileGranularity.PerAccount => !string.IsNullOrWhiteSpace(SelectedAccount),
            ProfileGranularity.PerCharacter =>
                !string.IsNullOrWhiteSpace(SelectedAccount)
                && !string.IsNullOrWhiteSpace(SelectedRealm)
                && !string.IsNullOrWhiteSpace(SelectedCharacter),
            _ => false,
        };

    public void Refresh(WowInstallation installation)
    {
        _installation = installation;

        var previousAccount = SelectedAccount;

        Accounts.Clear();
        foreach (var account in installation.Accounts)
            Accounts.Add(account.AccountName);

        // Re-select preserved value or auto-select the only option
        if (previousAccount is not null && Accounts.Contains(previousAccount))
            SelectedAccount = previousAccount;
        else
            SelectedAccount = Accounts.Count == 1 ? Accounts[0] : null;
    }

    partial void OnSelectedAccountChanged(string? value)
    {
        var previousRealm = SelectedRealm;
        Realms.Clear();
        Characters.Clear();

        if (value is null || _installation is null)
        {
            SelectedRealm = null;
            return;
        }

        var account = _installation.Accounts.FirstOrDefault(a => a.AccountName == value);
        if (account is null)
            return;

        foreach (var realm in account.Realms)
            Realms.Add(realm.RealmName);

        if (previousRealm is not null && Realms.Contains(previousRealm))
            SelectedRealm = previousRealm;
        else
            SelectedRealm = Realms.Count == 1 ? Realms[0] : null;

        ApplyDefaultSaveMode();
    }

    partial void OnSelectedRealmChanged(string? value)
    {
        var previousCharacter = SelectedCharacter;
        Characters.Clear();

        if (value is null || _installation is null || SelectedAccount is null)
        {
            SelectedCharacter = null;
            return;
        }

        var account = _installation.Accounts.FirstOrDefault(a => a.AccountName == SelectedAccount);
        var realm = account?.Realms.FirstOrDefault(r => r.RealmName == value);
        if (realm is null)
            return;

        foreach (var character in realm.Characters)
            Characters.Add(character.CharacterName);

        if (previousCharacter is not null && Characters.Contains(previousCharacter))
            SelectedCharacter = previousCharacter;
        else
            SelectedCharacter = Characters.Count == 1 ? Characters[0] : null;

        ApplyDefaultSaveMode();
    }

    partial void OnSelectedCharacterChanged(string? value) => ApplyDefaultSaveMode();

    partial void OnSaveModeChanged(ProfileGranularity value)
    {
        OnPropertyChanged(nameof(IsSaveModeFullWtf));
        OnPropertyChanged(nameof(IsSaveModePerAccount));
        OnPropertyChanged(nameof(IsSaveModePerCharacter));
    }

    private void ApplyDefaultSaveMode()
    {
        SaveMode = SelectedCharacter is not null
            ? ProfileGranularity.PerCharacter
            : SelectedAccount is not null
                ? ProfileGranularity.PerAccount
                : ProfileGranularity.FullWtf;
    }

    /// <summary>
    /// Builds a <see cref="ProfileDescriptor"/> from the current picker selection.
    /// Returns <c>null</c> when <see cref="CanBuildDescriptor"/> is false.
    /// </summary>
    public ProfileDescriptor? TryBuildDescriptor(string profilesPath)
    {
        if (!CanBuildDescriptor)
            return null;

        var id = BuildSnapshotId();
        var snapshotPath = Path.Combine(profilesPath, id);

        return new ProfileDescriptor
        {
            Id = id,
            LocalProfileId = LocalProfileId.Trim(),
            Granularity = SaveMode,
            AccountName = SelectedAccount,
            RealmName = SelectedRealm,
            CharacterName = SelectedCharacter,
            SnapshotPath = snapshotPath,
            DisplayName = BuildDisplayName(),
        };
    }

    private string BuildSnapshotId()
    {
        var owner = SanitizeName(LocalProfileId);
        return SaveMode switch
        {
            ProfileGranularity.PerAccount =>
                $"{owner}__account__{SanitizeName(SelectedAccount!)}",
            ProfileGranularity.PerCharacter =>
                $"{owner}__character__{SanitizeName(SelectedAccount!)}__{SanitizeName(SelectedRealm!)}__{SanitizeName(SelectedCharacter!)}",
            _ => owner,
        };
    }

    private string BuildDisplayName()
    {
        return SaveMode switch
        {
            ProfileGranularity.PerAccount => $"{LocalProfileId.Trim()} / {SelectedAccount}",
            ProfileGranularity.PerCharacter =>
                $"{LocalProfileId.Trim()} / {SelectedCharacter} ({SelectedRealm})",
            _ => LocalProfileId.Trim(),
        };
    }

    private static string SanitizeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }
}
