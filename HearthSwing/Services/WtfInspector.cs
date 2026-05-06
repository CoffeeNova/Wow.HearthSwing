using System.IO;
using HearthSwing.Models.WoW;
using Microsoft.Extensions.Logging;

namespace HearthSwing.Services;

public sealed class WtfInspector : IWtfInspector
{
    private const string WtfFolderName = "WTF";
    private const string AccountFolderName = "Account";
    private const string SavedVariablesFolderName = "SavedVariables";

    private readonly IFileSystem _fileSystem;
    private readonly ILogger<WtfInspector> _logger;

    public WtfInspector(IFileSystem fileSystem, ILogger<WtfInspector> logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public WowInstallation Inspect(string gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath))
            throw new ArgumentException("Game path is required.", nameof(gamePath));

        var wtfPath = Path.Combine(gamePath, WtfFolderName);
        if (!_fileSystem.DirectoryExists(wtfPath))
            throw new InvalidOperationException($"WTF folder not found at '{wtfPath}'.");

        return new WowInstallation
        {
            GamePath = gamePath,
            WtfPath = wtfPath,
            Accounts = DiscoverAccounts(wtfPath),
        };
    }

    private IReadOnlyList<WowAccount> DiscoverAccounts(string wtfPath)
    {
        var accountRoot = Path.Combine(wtfPath, AccountFolderName);
        if (!_fileSystem.DirectoryExists(accountRoot))
        {
            _logger.LogInformation("No Account directory found under {WtfPath}.", wtfPath);
            return [];
        }

        return GetChildDirectories(accountRoot, SavedVariablesFolderName)
            .Select(accountPath => BuildAccount(accountPath))
            .ToList();
    }

    private WowAccount BuildAccount(string accountPath)
    {
        var accountName = Path.GetFileName(accountPath);

        return new WowAccount
        {
            AccountName = accountName,
            FolderPath = accountPath,
            Realms = DiscoverRealms(accountPath, accountName),
        };
    }

    private IReadOnlyList<WowRealm> DiscoverRealms(string accountPath, string accountName)
    {
        return GetChildDirectories(accountPath, SavedVariablesFolderName)
            .Select(realmPath => BuildRealm(realmPath, accountName))
            .ToList();
    }

    private WowRealm BuildRealm(string realmPath, string accountName)
    {
        var realmName = Path.GetFileName(realmPath);

        return new WowRealm
        {
            AccountName = accountName,
            RealmName = realmName,
            FolderPath = realmPath,
            Characters = DiscoverCharacters(realmPath, accountName, realmName),
        };
    }

    private IReadOnlyList<WowCharacter> DiscoverCharacters(
        string realmPath,
        string accountName,
        string realmName
    )
    {
        return GetChildDirectories(realmPath)
            .Select(characterPath => new WowCharacter
            {
                AccountName = accountName,
                RealmName = realmName,
                CharacterName = Path.GetFileName(characterPath),
                FolderPath = characterPath,
            })
            .ToList();
    }

    private IEnumerable<string> GetChildDirectories(
        string path,
        params string[] ignoredDirectoryNames
    )
    {
        var ignoredNames = ignoredDirectoryNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return _fileSystem
            .GetDirectories(path)
            .Where(directoryPath => ShouldIncludeDirectory(directoryPath, ignoredNames))
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
    }

    private static bool ShouldIncludeDirectory(string path, HashSet<string> ignoredDirectoryNames)
    {
        var directoryName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(directoryName))
            return false;

        if (directoryName.StartsWith('.'))
            return false;

        return !ignoredDirectoryNames.Contains(directoryName);
    }
}
