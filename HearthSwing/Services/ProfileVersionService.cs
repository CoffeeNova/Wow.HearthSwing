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

    public async Task CreateVersionAsync(string profileId)
    {
        var profilePath = Path.Combine(_settings.Current.ProfilesPath, profileId);
        if (!_fs.DirectoryExists(profilePath))
        {
            _logger.LogWarning("Profile folder '{ProfileId}' not found — skipping version.", profileId);
            return;
        }

        var versionId = DateTime.Now.ToString(TimestampFormat);
        var profileVersionsDir = Path.Combine(VersionsRoot, profileId);
        _fs.CreateDirectory(profileVersionsDir);

        var archivePath = Path.Combine(profileVersionsDir, versionId + ArchiveExtension);
        await _archive.CompressDirectoryAsync(profilePath, archivePath);
        _logger.LogInformation("Version '{VersionId}' created for profile '{ProfileId}'.", versionId, profileId);

        PruneVersions(profileId, _settings.Current.MaxVersionsPerProfile);
    }

    public List<ProfileVersion> GetVersions(string profileId)
    {
        var profileVersionsDir = Path.Combine(VersionsRoot, profileId);
        if (!_fs.DirectoryExists(profileVersionsDir))
            return [];

        var versions = new List<ProfileVersion>();
        foreach (
            var file in _fs.GetFiles(
                profileVersionsDir,
                "*" + ArchiveExtension,
                SearchOption.TopDirectoryOnly
            )
        )
        {
            var name = Path.GetFileName(file);
            var versionId = name[..^ArchiveExtension.Length];
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
                        ProfileId = profileId,
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
        var profilePath = Path.Combine(_settings.Current.ProfilesPath, version.ProfileId);

        if (_fs.DirectoryExists(profilePath))
        {
            ClearReadOnlyAttributes(profilePath);
            _fs.DeleteDirectory(profilePath, recursive: true);
        }

        _fs.CreateDirectory(profilePath);
        await _archive.ExtractToDirectoryAsync(version.ArchivePath, profilePath);
        _logger.LogInformation("Profile '{ProfileId}' restored from version '{VersionId}'.", version.ProfileId, version.VersionId);
    }

    public void DeleteVersion(ProfileVersion version)
    {
        if (!_fs.FileExists(version.ArchivePath))
            return;

        _fs.DeleteFile(version.ArchivePath);
        _logger.LogInformation("Version '{VersionId}' deleted for profile '{ProfileId}'.", version.VersionId, version.ProfileId);
    }

    public void PruneVersions(string profileId, int maxVersions)
    {
        var versions = GetVersions(profileId);
        if (versions.Count <= maxVersions)
            return;

        var toDelete = versions.Skip(maxVersions).ToList();
        foreach (var version in toDelete)
        {
            if (_fs.FileExists(version.ArchivePath))
                _fs.DeleteFile(version.ArchivePath);
        }

        _logger.LogInformation("Pruned {Count} old version(s) for profile '{ProfileId}'.", toDelete.Count, profileId);
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
