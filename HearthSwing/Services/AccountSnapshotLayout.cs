using System.IO;

namespace HearthSwing.Services;

/// <summary>
/// Classifies account-level vs character-level files inside a WoW account snapshot.
/// </summary>
public sealed class AccountSnapshotLayout : IAccountSnapshotLayout
{
    private const string SavedVariablesFolderName = "SavedVariables";

    private readonly IFileSystem _fileSystem;

    public AccountSnapshotLayout(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public List<string> CollectAccountSettingsRelativePaths(string accountPath)
    {
        if (!_fileSystem.DirectoryExists(accountPath))
            return [];

        var relativePaths = new List<string>();

        foreach (
            var filePath in _fileSystem.GetFiles(accountPath, "*", SearchOption.TopDirectoryOnly)
        )
            relativePaths.Add(Path.GetRelativePath(accountPath, filePath));

        var savedVariablesPath = Path.Combine(accountPath, SavedVariablesFolderName);
        if (_fileSystem.DirectoryExists(savedVariablesPath))
        {
            foreach (
                var filePath in _fileSystem.GetFiles(
                    savedVariablesPath,
                    "*",
                    SearchOption.AllDirectories
                )
            )
            {
                relativePaths.Add(Path.GetRelativePath(accountPath, filePath));
            }
        }

        return relativePaths.Distinct(StringComparer.OrdinalIgnoreCase).Order().ToList();
    }

    public List<string> CollectCharacterRelativePaths(string characterPath)
    {
        if (!_fileSystem.DirectoryExists(characterPath))
            return [];

        return _fileSystem
            .GetFiles(characterPath, "*", SearchOption.AllDirectories)
            .Select(filePath => Path.GetRelativePath(characterPath, filePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order()
            .ToList();
    }
}
