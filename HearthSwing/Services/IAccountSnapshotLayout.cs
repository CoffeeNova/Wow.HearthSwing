namespace HearthSwing.Services;

/// <summary>
/// Defines how account-level and character-level files are partitioned inside a WoW account snapshot.
/// </summary>
public interface IAccountSnapshotLayout
{
    /// <summary>
    /// Collects relative file paths that belong to account-level settings for the given account root.
    /// </summary>
    List<string> CollectAccountSettingsRelativePaths(string accountPath);

    /// <summary>
    /// Collects relative file paths that belong to a specific character folder.
    /// </summary>
    List<string> CollectCharacterRelativePaths(string characterPath);
}
