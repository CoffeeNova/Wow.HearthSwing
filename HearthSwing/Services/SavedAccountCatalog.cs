using System.IO;
using System.Text.Json;
using HearthSwing.Models.Accounts;
using Microsoft.Extensions.Logging;

namespace HearthSwing.Services;

/// <summary>
/// Persists and resolves saved-account metadata under the configured storage root.
/// </summary>
public sealed class SavedAccountCatalog : ISavedAccountCatalog
{
    private const string MetadataFileName = "account.json";
    private const string ActiveStateFileName = ".active-account.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ISettingsService _settings;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<SavedAccountCatalog> _logger;

    public SavedAccountCatalog(
        ISettingsService settings,
        IFileSystem fileSystem,
        ILogger<SavedAccountCatalog> logger
    )
    {
        _settings = settings;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    private string StorageRoot => _settings.Current.ProfilesPath;

    public List<SavedAccountSummary> DiscoverAccounts()
    {
        if (!_fileSystem.DirectoryExists(StorageRoot))
            return [];

        var activeAccountId = GetActiveAccount()?.SavedAccountId;
        var accounts = new List<SavedAccountSummary>();

        foreach (var directoryPath in GetStorageDirectories())
        {
            var metadata = ReadMetadata(directoryPath);
            accounts.Add(ToSummary(metadata, directoryPath, activeAccountId));
        }

        return accounts.OrderBy(account => account.AccountName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public SavedAccountSummary? GetById(string savedAccountId)
    {
        if (string.IsNullOrWhiteSpace(savedAccountId))
            throw new ArgumentException("Saved account id is required.", nameof(savedAccountId));

        return DiscoverAccounts().FirstOrDefault(account =>
            account.Id.Equals(savedAccountId, StringComparison.OrdinalIgnoreCase)
        );
    }

    public SavedAccountSummary? FindByAccountName(string accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName))
            throw new ArgumentException("Account name is required.", nameof(accountName));

        return DiscoverAccounts().FirstOrDefault(account =>
            account.AccountName.Equals(accountName.Trim(), StringComparison.OrdinalIgnoreCase)
        );
    }

    public SavedAccountSummary EnsureAccount(string accountName)
    {
        if (string.IsNullOrWhiteSpace(accountName))
            throw new ArgumentException("Account name is required.", nameof(accountName));

        var normalizedAccountName = accountName.Trim();
        var existing = FindByAccountName(normalizedAccountName);
        if (existing is not null)
            return existing;

        if (!_fileSystem.DirectoryExists(StorageRoot))
            _fileSystem.CreateDirectory(StorageRoot);

        var savedAccountId = BuildUniqueSavedAccountId(normalizedAccountName);
        var rootPath = Path.Combine(StorageRoot, savedAccountId);
        var snapshotPath = BuildSnapshotPath(rootPath, normalizedAccountName);

        _fileSystem.CreateDirectory(snapshotPath);

        var metadata = new SavedAccountMetadata
        {
            Id = savedAccountId,
            AccountName = normalizedAccountName,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        WriteMetadata(rootPath, metadata);

        _logger.LogInformation(
            "Created saved account '{AccountName}' with id '{SavedAccountId}'.",
            normalizedAccountName,
            savedAccountId
        );

        return ToSummary(metadata, rootPath, GetActiveAccount()?.SavedAccountId);
    }

    public void UpdateLastSaved(string savedAccountId, DateTimeOffset savedAtUtc)
    {
        var account = GetById(savedAccountId)
            ?? throw new InvalidOperationException(
                $"Saved account '{savedAccountId}' was not found in storage."
            );

        var metadata = new SavedAccountMetadata
        {
            Id = account.Id,
            AccountName = account.AccountName,
            CreatedAtUtc = account.CreatedAtUtc,
            LastSavedUtc = savedAtUtc,
        };

        WriteMetadata(account.RootPath, metadata);

        _logger.LogInformation(
            "Updated last-saved timestamp for account '{AccountName}' ({SavedAccountId}).",
            account.AccountName,
            account.Id
        );
    }

    public ActiveAccountState? GetActiveAccount()
    {
        var markerPath = GetActiveStatePath();
        if (!_fileSystem.FileExists(markerPath))
            return null;

        try
        {
            var json = _fileSystem.ReadAllText(markerPath);
            var activeAccount = JsonSerializer.Deserialize<ActiveAccountState>(json, JsonOptions);

            if (
                activeAccount is null
                || string.IsNullOrWhiteSpace(activeAccount.SavedAccountId)
                || string.IsNullOrWhiteSpace(activeAccount.AccountName)
            )
            {
                _logger.LogWarning("Active saved-account marker at {MarkerPath} is invalid.", markerPath);
                return null;
            }

            return activeAccount;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse active saved-account marker at {MarkerPath}.", markerPath);
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to read active saved-account marker at {MarkerPath}.", markerPath);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied reading active saved-account marker at {MarkerPath}.", markerPath);
            return null;
        }
    }

    public void SetActiveAccount(ActiveAccountState activeAccount)
    {
        ArgumentNullException.ThrowIfNull(activeAccount);

        if (!_fileSystem.DirectoryExists(StorageRoot))
            _fileSystem.CreateDirectory(StorageRoot);

        var json = JsonSerializer.Serialize(activeAccount, JsonOptions);
        _fileSystem.WriteAllText(GetActiveStatePath(), json);
    }

    public void ClearActiveAccount()
    {
        var markerPath = GetActiveStatePath();
        if (_fileSystem.FileExists(markerPath))
            _fileSystem.DeleteFile(markerPath);
    }

    private IEnumerable<string> GetStorageDirectories()
    {
        return _fileSystem
            .GetDirectories(StorageRoot)
            .Where(path =>
            {
                var directoryName = Path.GetFileName(path);
                return !string.IsNullOrWhiteSpace(directoryName) && !directoryName.StartsWith('.');
            })
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
    }

    private SavedAccountMetadata ReadMetadata(string rootPath)
    {
        var metadataPath = GetMetadataPath(rootPath);
        if (!_fileSystem.FileExists(metadataPath))
        {
            throw new InvalidOperationException(
                $"Unsupported saved-account storage at '{rootPath}'. Expected metadata file '{MetadataFileName}'."
            );
        }

        try
        {
            var json = _fileSystem.ReadAllText(metadataPath);
            var metadata = JsonSerializer.Deserialize<SavedAccountMetadata>(json, JsonOptions);

            if (
                metadata is null
                || string.IsNullOrWhiteSpace(metadata.Id)
                || string.IsNullOrWhiteSpace(metadata.AccountName)
            )
            {
                throw new InvalidOperationException(
                    $"Saved-account metadata at '{metadataPath}' is missing required fields."
                );
            }

            return metadata;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Saved-account metadata at '{metadataPath}' is invalid.",
                ex
            );
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"Saved-account metadata at '{metadataPath}' could not be read.",
                ex
            );
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException(
                $"Access denied reading saved-account metadata at '{metadataPath}'.",
                ex
            );
        }
    }

    private void WriteMetadata(string rootPath, SavedAccountMetadata metadata)
    {
        if (!_fileSystem.DirectoryExists(rootPath))
            _fileSystem.CreateDirectory(rootPath);

        var json = JsonSerializer.Serialize(metadata, JsonOptions);
        _fileSystem.WriteAllText(GetMetadataPath(rootPath), json);
    }

    private string BuildUniqueSavedAccountId(string accountName)
    {
        var baseId = SanitizeSavedAccountId(accountName);
        var candidate = baseId;
        var suffix = 2;

        while (_fileSystem.DirectoryExists(Path.Combine(StorageRoot, candidate)))
        {
            candidate = $"{baseId}-{suffix}";
            suffix++;
        }

        return candidate;
    }

    private SavedAccountSummary ToSummary(
        SavedAccountMetadata metadata,
        string rootPath,
        string? activeAccountId
    )
    {
        return new SavedAccountSummary
        {
            Id = metadata.Id,
            AccountName = metadata.AccountName,
            RootPath = rootPath,
            SnapshotPath = BuildSnapshotPath(rootPath, metadata.AccountName),
            CreatedAtUtc = metadata.CreatedAtUtc,
            LastSavedUtc = metadata.LastSavedUtc,
            IsActive = string.Equals(metadata.Id, activeAccountId, StringComparison.OrdinalIgnoreCase),
        };
    }

    private static string SanitizeSavedAccountId(string accountName)
    {
        var candidate = accountName.Trim();
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            candidate = candidate.Replace(invalidChar, '_');

        candidate = candidate.Replace(' ', '-').Trim('.', '-', '_');
        return string.IsNullOrWhiteSpace(candidate) ? "account" : candidate;
    }

    private static string BuildSnapshotPath(string rootPath, string accountName) =>
        Path.Combine(rootPath, "Account", accountName);

    private static string GetMetadataPath(string rootPath) => Path.Combine(rootPath, MetadataFileName);

    private string GetActiveStatePath() => Path.Combine(StorageRoot, ActiveStateFileName);
}