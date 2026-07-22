using System.IO;
using Microsoft.Extensions.Logging;

namespace HearthSwing.Services;

/// <summary>
/// Rollback-aware directory copier: backs up the destination, swaps in the source, and restores
/// the backup if any step fails. Mirrors the proven pattern used by the account snapshot services.
/// </summary>
public sealed class DirectoryReplacer : IDirectoryReplacer
{
    private const string RollbackFolderPrefix = ".rollback-";

    private readonly IFileSystem _fs;
    private readonly ILogger<DirectoryReplacer> _logger;

    public DirectoryReplacer(IFileSystem fileSystem, ILogger<DirectoryReplacer> logger)
    {
        _fs = fileSystem;
        _logger = logger;
    }

    public void ReplaceDirectory(string sourcePath, string destinationPath)
    {
        var rollbackPath = string.Empty;
        var rollbackRequired = false;

        try
        {
            if (_fs.DirectoryExists(destinationPath))
            {
                rollbackPath = CreateRollbackPath(destinationPath);
                CopyDirectory(destinationPath, rollbackPath);
                rollbackRequired = true;

                ClearReadOnlyAttributes(destinationPath);
                _fs.DeleteDirectory(destinationPath, recursive: true);
            }

            CopyDirectory(sourcePath, destinationPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to replace directory '{Destination}'.", destinationPath);

            if (
                !rollbackRequired
                || string.IsNullOrEmpty(rollbackPath)
                || !_fs.DirectoryExists(rollbackPath)
            )
                throw;

            if (_fs.DirectoryExists(destinationPath))
            {
                ClearReadOnlyAttributes(destinationPath);
                _fs.DeleteDirectory(destinationPath, recursive: true);
            }

            CopyDirectory(rollbackPath, destinationPath);
            throw;
        }
        finally
        {
            CleanupTemporaryDirectory(rollbackPath);
        }
    }

    public void CopyDirectory(string source, string destination)
    {
        _fs.CreateDirectory(destination);

        foreach (var filePath in _fs.GetFiles(source, "*", SearchOption.TopDirectoryOnly))
            _fs.CopyFile(filePath, Path.Combine(destination, Path.GetFileName(filePath)));

        foreach (var childDirectory in _fs.GetDirectories(source))
            CopyDirectory(
                childDirectory,
                Path.Combine(destination, Path.GetFileName(childDirectory))
            );
    }

    public void ClearReadOnlyAttributes(string directory)
    {
        if (!_fs.DirectoryExists(directory))
            return;

        foreach (var filePath in _fs.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            var attributes = _fs.GetAttributes(filePath);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                _fs.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
        }
    }

    private static string CreateRollbackPath(string destination)
    {
        var parentDirectory = Path.GetDirectoryName(destination);
        if (string.IsNullOrEmpty(parentDirectory))
            throw new InvalidOperationException(
                $"Could not create rollback path for '{destination}'."
            );

        var suffix = Path.GetFileName(destination);
        return Path.Combine(parentDirectory, $"{RollbackFolderPrefix}{suffix}-{Guid.NewGuid():N}");
    }

    private void CleanupTemporaryDirectory(string path)
    {
        if (string.IsNullOrEmpty(path) || !_fs.DirectoryExists(path))
            return;

        try
        {
            ClearReadOnlyAttributes(path);
            _fs.DeleteDirectory(path, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up temporary directory {Path}.", path);
        }
    }
}
