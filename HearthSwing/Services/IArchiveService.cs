namespace HearthSwing.Services;

public interface IArchiveService
{
    Task CompressDirectoryAsync(
        string sourceDir,
        string destinationArchivePath,
        CancellationToken ct = default
    );
    Task ExtractToDirectoryAsync(
        string archivePath,
        string destinationDir,
        CancellationToken ct = default
    );
}
