using System.IO;
using Microsoft.Extensions.Logging;

namespace HearthSwing.Services;

public sealed class LegacyDataCleanupService : ILegacyDataCleanupService
{
    private const string HistoryFolderName = ".history";
    private const string LegacyVersionsFolderName = ".versions";
    private const string LegacyTemplateVersionsFolderName = ".template-versions";
    private const string LegacyActiveAccountFileName = ".active-account.json";
    private const string LegacyAccountMetadataFileName = "account.json";
    private const string LegacyAccountRootFolderName = "Account";
    private const string SavedVariablesFolderName = "SavedVariables";

    private static readonly HashSet<string> ReservedFolderNames =
    [
        TemplateCatalog.TemplatesFolderName,
        HistoryFolderName,
        LegacyVersionsFolderName,
        LegacyTemplateVersionsFolderName,
    ];

    private readonly ISettingsService _settingsService;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<LegacyDataCleanupService> _logger;

    public LegacyDataCleanupService(
        ISettingsService settingsService,
        IFileSystem fileSystem,
        ILogger<LegacyDataCleanupService> logger
    )
    {
        _settingsService = settingsService;
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public LegacyDataCleanupSummary Discover()
    {
        var profilesPath = _settingsService.Current.ProfilesPath;
        if (string.IsNullOrWhiteSpace(profilesPath) || !_fileSystem.DirectoryExists(profilesPath))
            return new LegacyDataCleanupSummary();

        var directories = new List<string>();
        foreach (var directoryPath in _fileSystem.GetDirectories(profilesPath))
        {
            var name = Path.GetFileName(
                directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            );
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (ReservedFolderNames.Contains(name))
            {
                if (
                    string.Equals(
                        name,
                        LegacyVersionsFolderName,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || string.Equals(
                        name,
                        LegacyTemplateVersionsFolderName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    directories.Add(directoryPath);
                }

                continue;
            }

            if (
                !name.StartsWith(".", StringComparison.Ordinal)
                && IsLegacyAccountFolder(directoryPath)
            )
                directories.Add(directoryPath);
        }

        var files = new List<string>();
        var legacyActiveAccountPath = Path.Combine(profilesPath, LegacyActiveAccountFileName);
        if (_fileSystem.FileExists(legacyActiveAccountPath))
            files.Add(legacyActiveAccountPath);

        return new LegacyDataCleanupSummary
        {
            Directories = directories
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Files = files,
        };
    }

    public LegacyDataCleanupSummary Cleanup()
    {
        var summary = Discover();
        if (!summary.HasItems)
            return summary;

        foreach (var directory in summary.Directories)
        {
            ClearReadOnlyAttributes(directory);
            _fileSystem.DeleteDirectory(directory, recursive: true);
        }

        foreach (var file in summary.Files)
        {
            ClearReadOnlyAttribute(file);
            _fileSystem.DeleteFile(file);
        }

        _logger.LogInformation("Removed {Count} legacy storage item(s).", summary.TotalCount);
        return summary;
    }

    private bool IsLegacyAccountFolder(string directoryPath)
    {
        var metadataPath = Path.Combine(directoryPath, LegacyAccountMetadataFileName);
        if (!_fileSystem.FileExists(metadataPath))
            return false;

        var accountRootPath = Path.Combine(directoryPath, LegacyAccountRootFolderName);
        if (!_fileSystem.DirectoryExists(accountRootPath))
            return false;

        var accountDirectories = _fileSystem.GetDirectories(accountRootPath);
        foreach (var accountDirectory in accountDirectories)
        {
            var accountName = Path.GetFileName(
                accountDirectory.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                )
            );
            if (
                string.IsNullOrWhiteSpace(accountName)
                || accountName.StartsWith(".", StringComparison.Ordinal)
            )
                continue;

            var savedVariablesPath = Path.Combine(accountDirectory, SavedVariablesFolderName);
            if (_fileSystem.DirectoryExists(savedVariablesPath))
                return true;

            var realmDirectories = _fileSystem.GetDirectories(accountDirectory);
            foreach (var realmDirectory in realmDirectories)
            {
                var realmName = Path.GetFileName(
                    realmDirectory.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar
                    )
                );
                if (
                    string.IsNullOrWhiteSpace(realmName)
                    || realmName.StartsWith(".", StringComparison.Ordinal)
                )
                    continue;

                var characterDirectories = _fileSystem.GetDirectories(realmDirectory);
                if (characterDirectories.Length > 0)
                    return true;
            }
        }

        return false;
    }

    private void ClearReadOnlyAttributes(string directory)
    {
        if (!_fileSystem.DirectoryExists(directory))
            return;

        foreach (var filePath in _fileSystem.GetFiles(directory, "*", SearchOption.AllDirectories))
            ClearReadOnlyAttribute(filePath);
    }

    private void ClearReadOnlyAttribute(string path)
    {
        var attributes = _fileSystem.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            _fileSystem.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }
}
