using System.IO;
using HearthSwing.Models.Accounts;
using HearthSwing.Models.WoW;
using Microsoft.Extensions.Logging;

namespace HearthSwing.Services;

/// <summary>
/// Writes selective updates from a live WoW account into saved-account storage.
/// </summary>
public sealed class AccountSnapshotSaveService : IAccountSnapshotSaveService
{
    private const string RollbackFolderPrefix = ".rollback-";
    private const string SavedVariablesFolderName = "SavedVariables";

    private readonly ISavedAccountCatalog _catalog;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<AccountSnapshotSaveService> _logger;

    public AccountSnapshotSaveService(
        ISavedAccountCatalog catalog,
        IFileSystem fileSystem,
        ILogger<AccountSnapshotSaveService> logger
    )
    {
        _catalog = catalog;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public SavedAccountSummary Save(WowAccount liveAccount, AccountSavePlan savePlan)
    {
        ArgumentNullException.ThrowIfNull(liveAccount);
        ArgumentNullException.ThrowIfNull(savePlan);

        if (
            !liveAccount.AccountName.Equals(
                savePlan.AccountName,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new InvalidOperationException(
                "Save plan account name does not match the selected live account."
            );
        }

        var existingSavedAccount = _catalog.FindByAccountName(liveAccount.AccountName);
        var savedAccount = _catalog.EnsureAccount(liveAccount.AccountName);

        if (
            existingSavedAccount is null
            || !_fileSystem.DirectoryExists(existingSavedAccount.SnapshotPath)
        )
        {
            _logger.LogInformation(
                "Saving brand-new account snapshot for '{AccountName}'.",
                liveAccount.AccountName
            );
            ReplaceDirectoryWithRollback(
                liveAccount.FolderPath,
                savedAccount.SnapshotPath,
                "save new account snapshot"
            );
        }
        else
        {
            if (!savePlan.HasSelections)
            {
                _logger.LogInformation(
                    "No selected changes for saved account '{AccountName}' — skipping save.",
                    liveAccount.AccountName
                );
                return savedAccount;
            }

            if (savePlan.SaveAccountSettings)
                ReplaceAccountSettingsWithRollback(
                    liveAccount.FolderPath,
                    savedAccount.SnapshotPath
                );

            foreach (
                var selection in savePlan.SelectedCharacters.DistinctBy(selection =>
                    $"{selection.RealmName}\n{selection.CharacterName}"
                )
            )
            {
                ReplaceCharacterWithRollback(liveAccount, savedAccount, selection);
            }
        }

        var savedAtUtc = DateTimeOffset.UtcNow;
        _catalog.UpdateLastSaved(savedAccount.Id, savedAtUtc);
        _catalog.SetActiveAccount(
            new ActiveAccountState
            {
                SavedAccountId = savedAccount.Id,
                AccountName = savedAccount.AccountName,
            }
        );

        return _catalog.GetById(savedAccount.Id) ?? savedAccount;
    }

    private void ReplaceCharacterWithRollback(
        WowAccount liveAccount,
        SavedAccountSummary savedAccount,
        CharacterSaveSelection selection
    )
    {
        var liveCharacter = liveAccount
            .Realms.FirstOrDefault(realm =>
                realm.RealmName.Equals(selection.RealmName, StringComparison.OrdinalIgnoreCase)
            )
            ?.Characters.FirstOrDefault(character =>
                character.CharacterName.Equals(
                    selection.CharacterName,
                    StringComparison.OrdinalIgnoreCase
                )
            );

        if (liveCharacter is null)
        {
            throw new InvalidOperationException(
                $"Character '{selection.CharacterName}' on realm '{selection.RealmName}' was not found in the live account."
            );
        }

        var savedCharacterPath = Path.Combine(
            savedAccount.SnapshotPath,
            selection.RealmName,
            selection.CharacterName
        );

        ReplaceDirectoryWithRollback(
            liveCharacter.FolderPath,
            savedCharacterPath,
            $"save character '{selection.CharacterName}'"
        );
    }

    private void ReplaceAccountSettingsWithRollback(string liveAccountPath, string savedAccountPath)
    {
        var rollbackRoot = CreateRollbackPath(savedAccountPath, "account-settings");

        try
        {
            BackupAccountSettings(savedAccountPath, rollbackRoot);
            DeleteAccountSettings(savedAccountPath);
            CopyAccountSettings(liveAccountPath, savedAccountPath);
        }
        catch
        {
            DeleteAccountSettings(savedAccountPath);
            CopyAccountSettings(rollbackRoot, savedAccountPath);
            throw;
        }
        finally
        {
            CleanupTemporaryDirectory(rollbackRoot);
        }
    }

    private void BackupAccountSettings(string sourceAccountPath, string rollbackRoot)
    {
        if (!_fileSystem.DirectoryExists(sourceAccountPath))
            return;

        CopyAccountSettings(sourceAccountPath, rollbackRoot);
    }

    private void DeleteAccountSettings(string accountPath)
    {
        if (!_fileSystem.DirectoryExists(accountPath))
            return;

        foreach (
            var filePath in _fileSystem.GetFiles(accountPath, "*", SearchOption.TopDirectoryOnly)
        )
        {
            ClearReadOnlyIfNeeded(filePath);
            _fileSystem.DeleteFile(filePath);
        }

        var savedVariablesPath = Path.Combine(accountPath, SavedVariablesFolderName);
        if (_fileSystem.DirectoryExists(savedVariablesPath))
        {
            ClearReadOnlyAttributes(savedVariablesPath);
            _fileSystem.DeleteDirectory(savedVariablesPath, recursive: true);
        }
    }

    private void CopyAccountSettings(string sourceAccountPath, string destinationAccountPath)
    {
        if (!_fileSystem.DirectoryExists(sourceAccountPath))
            return;

        if (!_fileSystem.DirectoryExists(destinationAccountPath))
            _fileSystem.CreateDirectory(destinationAccountPath);

        foreach (
            var filePath in _fileSystem.GetFiles(
                sourceAccountPath,
                "*",
                SearchOption.TopDirectoryOnly
            )
        )
        {
            _fileSystem.CopyFile(
                filePath,
                Path.Combine(destinationAccountPath, Path.GetFileName(filePath))
            );
        }

        var sourceSavedVariablesPath = Path.Combine(sourceAccountPath, SavedVariablesFolderName);
        if (_fileSystem.DirectoryExists(sourceSavedVariablesPath))
        {
            var destinationSavedVariablesPath = Path.Combine(
                destinationAccountPath,
                SavedVariablesFolderName
            );
            CopyDirectory(sourceSavedVariablesPath, destinationSavedVariablesPath);
        }
    }

    private void ReplaceDirectoryWithRollback(string source, string destination, string operation)
    {
        var rollbackPath = string.Empty;
        var rollbackRequired = false;

        try
        {
            if (_fileSystem.DirectoryExists(destination))
            {
                rollbackPath = CreateRollbackPath(destination, Path.GetFileName(destination));
                CopyDirectory(destination, rollbackPath);
                rollbackRequired = true;

                ClearReadOnlyAttributes(destination);
                _fileSystem.DeleteDirectory(destination, recursive: true);
            }

            CopyDirectory(source, destination);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to {Operation}.", operation);

            if (
                !rollbackRequired
                || string.IsNullOrEmpty(rollbackPath)
                || !_fileSystem.DirectoryExists(rollbackPath)
            )
                throw;

            if (_fileSystem.DirectoryExists(destination))
            {
                ClearReadOnlyAttributes(destination);
                _fileSystem.DeleteDirectory(destination, recursive: true);
            }

            CopyDirectory(rollbackPath, destination);
            throw;
        }
        finally
        {
            CleanupTemporaryDirectory(rollbackPath);
        }
    }

    private void CopyDirectory(string source, string destination)
    {
        _fileSystem.CreateDirectory(destination);

        foreach (var filePath in _fileSystem.GetFiles(source, "*", SearchOption.TopDirectoryOnly))
        {
            _fileSystem.CopyFile(filePath, Path.Combine(destination, Path.GetFileName(filePath)));
        }

        foreach (var childDirectory in _fileSystem.GetDirectories(source))
        {
            CopyDirectory(
                childDirectory,
                Path.Combine(destination, Path.GetFileName(childDirectory))
            );
        }
    }

    private void ClearReadOnlyAttributes(string directory)
    {
        if (!_fileSystem.DirectoryExists(directory))
            return;

        foreach (var filePath in _fileSystem.GetFiles(directory, "*", SearchOption.AllDirectories))
            ClearReadOnlyIfNeeded(filePath);
    }

    private void ClearReadOnlyIfNeeded(string filePath)
    {
        var attributes = _fileSystem.GetAttributes(filePath);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            _fileSystem.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
    }

    private string CreateRollbackPath(string destination, string suffix)
    {
        var parentDirectory = Path.GetDirectoryName(destination);
        if (string.IsNullOrEmpty(parentDirectory))
            throw new InvalidOperationException(
                $"Could not create rollback path for '{destination}'."
            );

        return Path.Combine(parentDirectory, $"{RollbackFolderPrefix}{suffix}-{Guid.NewGuid():N}");
    }

    private void CleanupTemporaryDirectory(string path)
    {
        if (string.IsNullOrEmpty(path) || !_fileSystem.DirectoryExists(path))
            return;

        try
        {
            ClearReadOnlyAttributes(path);
            _fileSystem.DeleteDirectory(path, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up temporary directory {Path}.", path);
        }
    }
}
