using System.Formats.Tar;
using System.IO;
using System.IO.Compression;

namespace HearthSwing.Services;

public sealed class TarGzArchiveService : IArchiveService
{
    public async Task CompressDirectoryAsync(
        string sourceDir,
        string destinationArchivePath,
        CancellationToken ct = default
    )
    {
        await using var fileStream = File.Create(destinationArchivePath);
        await using var gzipStream = new GZipStream(fileStream, CompressionLevel.SmallestSize);
        await TarFile.CreateFromDirectoryAsync(
            sourceDir,
            gzipStream,
            includeBaseDirectory: false,
            ct
        );
    }

    public async Task ExtractToDirectoryAsync(
        string archivePath,
        string destinationDir,
        CancellationToken ct = default
    )
    {
        await using var fileStream = File.OpenRead(archivePath);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gzipStream, destinationDir, overwriteFiles: true, ct);
    }
}
