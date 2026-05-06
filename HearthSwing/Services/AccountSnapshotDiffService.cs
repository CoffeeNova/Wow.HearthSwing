using System.IO;
using HearthSwing.Models.Accounts;
using HearthSwing.Models.WoW;

namespace HearthSwing.Services;

/// <summary>
/// Compares live WoW account content against the last saved snapshot.
/// </summary>
public sealed class AccountSnapshotDiffService : IAccountSnapshotDiffService
{
    private readonly IFileSystem _fileSystem;
    private readonly IAccountSnapshotLayout _layout;

    public AccountSnapshotDiffService(IFileSystem fileSystem, IAccountSnapshotLayout layout)
    {
        _fileSystem = fileSystem;
        _layout = layout;
    }

    public AccountSnapshotDiff BuildDiff(WowAccount liveAccount, SavedAccountSummary? savedAccount)
    {
        ArgumentNullException.ThrowIfNull(liveAccount);

        if (
            savedAccount is not null
            && !savedAccount.AccountName.Equals(liveAccount.AccountName, StringComparison.OrdinalIgnoreCase)
        )
        {
            throw new InvalidOperationException(
                "Saved account metadata does not match the selected live account."
            );
        }

        var hasSavedSnapshot =
            savedAccount is not null && _fileSystem.DirectoryExists(savedAccount.SnapshotPath);
        var accountSettingsStatus = BuildAccountSettingsStatus(liveAccount, savedAccount, hasSavedSnapshot);
        var realms = liveAccount.Realms.Select(realm => BuildRealmDiff(realm, savedAccount, hasSavedSnapshot)).ToList();

        return new AccountSnapshotDiff
        {
            AccountName = liveAccount.AccountName,
            IsNewAccount = !hasSavedSnapshot,
            AccountSettingsStatus = accountSettingsStatus,
            Realms = realms,
        };
    }

    private RealmSnapshotDiff BuildRealmDiff(
        WowRealm liveRealm,
        SavedAccountSummary? savedAccount,
        bool hasSavedSnapshot
    )
    {
        var characters = liveRealm.Characters.Select(character =>
            BuildCharacterDiff(character, savedAccount, hasSavedSnapshot)
        ).ToList();

        return new RealmSnapshotDiff
        {
            RealmName = liveRealm.RealmName,
            Status = AggregateStatuses(characters.Select(character => character.Status)),
            Characters = characters,
        };
    }

    private CharacterSnapshotDiff BuildCharacterDiff(
        WowCharacter liveCharacter,
        SavedAccountSummary? savedAccount,
        bool hasSavedSnapshot
    )
    {
        var status = AccountSnapshotDiffStatus.New;

        if (hasSavedSnapshot)
        {
            var savedCharacterPath = Path.Combine(
                savedAccount!.SnapshotPath,
                liveCharacter.RealmName,
                liveCharacter.CharacterName
            );

            status = _fileSystem.DirectoryExists(savedCharacterPath)
                ? CompareRelativeFileSets(
                    liveCharacter.FolderPath,
                    _layout.CollectCharacterRelativePaths(liveCharacter.FolderPath),
                    savedCharacterPath,
                    _layout.CollectCharacterRelativePaths(savedCharacterPath)
                )
                : AccountSnapshotDiffStatus.New;
        }

        return new CharacterSnapshotDiff
        {
            RealmName = liveCharacter.RealmName,
            CharacterName = liveCharacter.CharacterName,
            FolderPath = liveCharacter.FolderPath,
            Status = status,
        };
    }

    private AccountSnapshotDiffStatus BuildAccountSettingsStatus(
        WowAccount liveAccount,
        SavedAccountSummary? savedAccount,
        bool hasSavedSnapshot
    )
    {
        var liveRelativePaths = _layout.CollectAccountSettingsRelativePaths(liveAccount.FolderPath);
        if (!hasSavedSnapshot)
            return liveRelativePaths.Count > 0
                ? AccountSnapshotDiffStatus.New
                : AccountSnapshotDiffStatus.Unchanged;

        var savedRelativePaths = _layout.CollectAccountSettingsRelativePaths(savedAccount!.SnapshotPath);

        return CompareRelativeFileSets(
            liveAccount.FolderPath,
            liveRelativePaths,
            savedAccount.SnapshotPath,
            savedRelativePaths
        );
    }

    private AccountSnapshotDiffStatus CompareRelativeFileSets(
        string liveBasePath,
        IReadOnlyCollection<string> liveRelativePaths,
        string savedBasePath,
        IReadOnlyCollection<string> savedRelativePaths
    )
    {
        var liveSet = liveRelativePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var savedSet = savedRelativePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!liveSet.SetEquals(savedSet))
            return AccountSnapshotDiffStatus.Modified;

        foreach (var relativePath in liveSet.Order())
        {
            var livePath = Path.Combine(liveBasePath, relativePath);
            var savedPath = Path.Combine(savedBasePath, relativePath);

            if (!_fileSystem.FileExists(savedPath))
                return AccountSnapshotDiffStatus.Modified;

            if (_fileSystem.GetFileLength(livePath) != _fileSystem.GetFileLength(savedPath))
                return AccountSnapshotDiffStatus.Modified;

            if (!_fileSystem.ReadAllBytes(livePath).SequenceEqual(_fileSystem.ReadAllBytes(savedPath)))
                return AccountSnapshotDiffStatus.Modified;
        }

        return AccountSnapshotDiffStatus.Unchanged;
    }

    private static AccountSnapshotDiffStatus AggregateStatuses(
        IEnumerable<AccountSnapshotDiffStatus> statuses
    )
    {
        var materialized = statuses.ToList();
        if (materialized.Count == 0 || materialized.All(status => status == AccountSnapshotDiffStatus.Unchanged))
            return AccountSnapshotDiffStatus.Unchanged;

        return materialized.All(status => status == AccountSnapshotDiffStatus.New)
            ? AccountSnapshotDiffStatus.New
            : AccountSnapshotDiffStatus.Modified;
    }
}