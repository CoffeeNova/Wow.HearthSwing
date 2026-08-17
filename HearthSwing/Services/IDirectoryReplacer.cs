namespace HearthSwing.Services;

/// <summary>
/// Replaces and copies directory trees with rollback semantics. New helper shared by the template
/// services; the existing account services keep their own private copy of this pattern.
/// </summary>
public interface IDirectoryReplacer
{
    /// <summary>
    /// Replaces <paramref name="destinationPath"/> with the contents of <paramref name="sourcePath"/>,
    /// restoring the previous destination if the copy fails.
    /// </summary>
    void ReplaceDirectory(string sourcePath, string destinationPath);

    /// <summary>
    /// Recursively copies a directory tree from <paramref name="source"/> to <paramref name="destination"/>.
    /// </summary>
    void CopyDirectory(string source, string destination);

    /// <summary>
    /// Clears the read-only attribute from every file under <paramref name="directory"/>.
    /// </summary>
    void ClearReadOnlyAttributes(string directory);
}
