using System.IO;
using HearthSwing.Models.Accounts;
using Microsoft.Extensions.Logging;

namespace HearthSwing.Services;

/// <summary>
/// Applies saved account snapshots into the live WTF account directory.
/// </summary>
public sealed class AccountSwitchService : IAccountSwitchService
{
    private const string RollbackFolderPrefix = ".rollback-";

    private readonly ISettingsService _settings;
    private readonly ISavedAccountCatalog _catalog;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<AccountSwitchService> _logger;

    public AccountSwitchService(
        ISettingsService settings,
        ISavedAccountCatalog catalog,
        IFileSystem fileSystem,
        ILogger<AccountSwitchService> logger
    )
    {
        _settings = settings;
        _catalog = catalog;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public string WtfPath => Path.Combine(_settings.Current.GamePath, "WTF");

    public void SwitchTo(SavedAccountSummary savedAccount)
    {
        ArgumentNullException.ThrowIfNull(savedAccount);

        if (!_fileSystem.DirectoryExists(savedAccount.SnapshotPath))
        {
            throw new InvalidOperationException(
                $"Saved account snapshot folder not found: {savedAccount.SnapshotPath}"
            );
        }

        var liveAccountPath = Path.Combine(WtfPath, "Account", savedAccount.AccountName);
        ReplaceDirectoryWithRollback(savedAccount.SnapshotPath, liveAccountPath, "switch account");

        _catalog.SetActiveAccount(
            new ActiveAccountState
            {
                SavedAccountId = savedAccount.Id,
                AccountName = savedAccount.AccountName,
            }
        );

        _logger.LogInformation(
            "Saved account '{AccountName}' ({SavedAccountId}) is now active.",
            savedAccount.AccountName,
            savedAccount.Id
        );
    }

    public void RestoreActiveAccount()
    {
        var activeAccount = _catalog.GetActiveAccount();
        if (activeAccount is null)
            throw new InvalidOperationException("No active saved account to restore.");

        var savedAccount =
            _catalog.GetById(activeAccount.SavedAccountId)
            ?? throw new InvalidOperationException(
                $"Active saved account '{activeAccount.SavedAccountId}' was not found in storage."
            );

        SwitchTo(savedAccount);
    }

    private void ReplaceDirectoryWithRollback(string source, string destination, string operation)
    {
        var rollbackPath = string.Empty;
        var rollbackRequired = false;

        try
        {
            if (_fileSystem.DirectoryExists(destination))
            {
                rollbackPath = CreateRollbackPath(destination);
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

    private string CreateRollbackPath(string destination)
    {
        var parentDirectory = Path.GetDirectoryName(destination);
        if (string.IsNullOrEmpty(parentDirectory))
            throw new InvalidOperationException(
                $"Could not create rollback path for '{destination}'."
            );

        return Path.Combine(
            parentDirectory,
            $"{RollbackFolderPrefix}{Path.GetFileName(destination)}-{Guid.NewGuid():N}"
        );
    }

    private void CopyDirectory(string source, string destination)
    {
        _fileSystem.CreateDirectory(destination);

        foreach (var filePath in _fileSystem.GetFiles(source, "*", SearchOption.TopDirectoryOnly))
            _fileSystem.CopyFile(filePath, Path.Combine(destination, Path.GetFileName(filePath)));

        foreach (var childDirectory in _fileSystem.GetDirectories(source))
            CopyDirectory(
                childDirectory,
                Path.Combine(destination, Path.GetFileName(childDirectory))
            );
    }

    private void ClearReadOnlyAttributes(string directory)
    {
        if (!_fileSystem.DirectoryExists(directory))
            return;

        foreach (var filePath in _fileSystem.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            var attributes = _fileSystem.GetAttributes(filePath);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                _fileSystem.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
        }
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
