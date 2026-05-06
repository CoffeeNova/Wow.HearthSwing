using System.Globalization;
using System.IO;
using HearthSwing.Models;
using Microsoft.Extensions.Logging;

namespace HearthSwing.Services;

public sealed class ProfileVersionService : IProfileVersionService
{
    private const string VersionsFolderName = ".versions";
    private const string TimestampFormat = "yyyyMMdd_HHmmss";
    private const string ArchiveExtension = ".tar.gz";
    private const string MetaExtension = ".meta.json";

    private readonly IFileSystem _fs;
    private readonly ISettingsService _settings;
    private readonly ILogger<ProfileVersionService> _logger;
    private readonly IArchiveService _archive;

    public ProfileVersionService(
        IFileSystem fileSystem,
        ISettingsService settings,
        ILogger<ProfileVersionService> logger,
        IArchiveService archive
    )
    {
        _fs = fileSystem;
        _settings = settings;
        _logger = logger;
        _archive = archive;
    }

    private string VersionsRoot => Path.Combine(_settings.Current.ProfilesPath, VersionsFolderName);

    public async Task CreateVersionAsync(string savedAccountId)
    {
        var savedAccountPath = Path.Combine(_settings.Current.ProfilesPath, savedAccountId);
        if (!_fs.DirectoryExists(savedAccountPath))
        {
            _logger.LogWarning(
                "Saved account folder '{SavedAccountId}' not found — skipping version.",
                savedAccountId
            );
            return;
        }

        var versionId = DateTime.Now.ToString(TimestampFormat);
        var savedAccountVersionsDir = Path.Combine(VersionsRoot, savedAccountId);
        _fs.CreateDirectory(savedAccountVersionsDir);

        var archivePath = Path.Combine(savedAccountVersionsDir, versionId + ArchiveExtension);
        await _archive.CompressDirectoryAsync(savedAccountPath, archivePath);
        _logger.LogInformation(
            "Version '{VersionId}' created for saved account '{SavedAccountId}'.",
            versionId,
            savedAccountId
        );

        PruneVersions(savedAccountId, _settings.Current.MaxVersionsPerProfile);
    }

    public List<ProfileVersion> GetVersions(string savedAccountId)
    {
        var savedAccountVersionsDir = Path.Combine(VersionsRoot, savedAccountId);
        if (!_fs.DirectoryExists(savedAccountVersionsDir))
            return [];

        var versions = new List<ProfileVersion>();
        foreach (
            var file in _fs.GetFiles(
                savedAccountVersionsDir,
                "*" + ArchiveExtension,
                SearchOption.TopDirectoryOnly
            )
        )
        {
            var versionId = Path.GetFileName(file)[..^ArchiveExtension.Length];
            if (
                DateTime.TryParseExact(
                    versionId,
                    TimestampFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var createdAt
                )
            )
            {
                versions.Add(
                    new ProfileVersion
                    {
                        VersionId = versionId,
                        ProfileId = savedAccountId,
                        CreatedAt = createdAt,
                        ArchivePath = file,
                    }
                );
            }
        }

        return versions.OrderByDescending(v => v.CreatedAt).ToList();
    }

    public async Task RestoreVersionAsync(ProfileVersion version)
    {
        var savedAccountPath = Path.Combine(_settings.Current.ProfilesPath, version.ProfileId);

        if (_fs.DirectoryExists(savedAccountPath))
        {
            ClearReadOnlyAttributes(savedAccountPath);
            _fs.DeleteDirectory(savedAccountPath, recursive: true);
        }

        _fs.CreateDirectory(savedAccountPath);
        await _archive.ExtractToDirectoryAsync(version.ArchivePath, savedAccountPath);
        _logger.LogInformation(
            "Saved account '{SavedAccountId}' restored from version '{VersionId}'.",
            version.ProfileId,
            version.VersionId
        );
    }

    public void DeleteVersion(ProfileVersion version)
    {
        if (!_fs.FileExists(version.ArchivePath))
            return;

        _fs.DeleteFile(version.ArchivePath);
        var metaPath = Path.ChangeExtension(version.ArchivePath, null) + MetaExtension;
        if (_fs.FileExists(metaPath))
            _fs.DeleteFile(metaPath);

        _logger.LogInformation(
            "Version '{VersionId}' deleted for saved account '{SavedAccountId}'.",
            version.VersionId,
            version.ProfileId
        );
    }

    public void PruneVersions(string savedAccountId, int maxVersions)
    {
        var versions = GetVersions(savedAccountId);
        if (versions.Count <= maxVersions)
            return;

        var toDelete = versions.Skip(maxVersions).ToList();
        foreach (var version in toDelete)
            DeleteVersion(version);

        _logger.LogInformation(
            "Pruned {Count} old version(s) for saved account '{SavedAccountId}'.",
            toDelete.Count,
            savedAccountId
        );
    }

    private void ClearReadOnlyAttributes(string directory)
    {
        if (!_fs.DirectoryExists(directory))
            return;

        foreach (var file in _fs.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            var attrs = _fs.GetAttributes(file);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                _fs.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
        }
    }
}
