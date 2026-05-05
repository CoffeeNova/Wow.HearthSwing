using System.Globalization;
using System.IO;
using System.Text.Json;
using HearthSwing.Models;
using HearthSwing.Models.Profiles;
using Microsoft.Extensions.Logging;

namespace HearthSwing.Services;

public sealed class ProfileVersionService : IProfileVersionService
{
    private const string VersionsFolderName = ".versions";
    private const string TimestampFormat = "yyyyMMdd_HHmmss";
    private const string ArchiveExtension = ".tar.gz";
    private const string MetaExtension = ".meta.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

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

    public async Task CreateVersionAsync(ProfileDescriptor descriptor)
    {
        if (!_fs.DirectoryExists(descriptor.SnapshotPath))
        {
            _logger.LogWarning(
                "Snapshot folder '{SnapshotPath}' not found — skipping version.",
                descriptor.SnapshotPath
            );
            return;
        }

        var versionId = DateTime.Now.ToString(TimestampFormat);
        var versionDir = Path.Combine(VersionsRoot, descriptor.Id);
        _fs.CreateDirectory(versionDir);

        var archivePath = Path.Combine(versionDir, versionId + ArchiveExtension);
        await _archive.CompressDirectoryAsync(descriptor.SnapshotPath, archivePath);

        var meta = new VersionMeta
        {
            LocalProfileId = descriptor.LocalProfileId,
            Granularity = descriptor.Granularity,
            AccountName = descriptor.AccountName,
            RealmName = descriptor.RealmName,
            CharacterName = descriptor.CharacterName,
        };
        var metaPath = Path.Combine(versionDir, versionId + MetaExtension);
        var json = JsonSerializer.Serialize(meta, JsonOptions);
        _fs.WriteAllText(metaPath, json);

        _logger.LogInformation(
            "Version '{VersionId}' created for scope '{DescriptorId}'.",
            versionId,
            descriptor.Id
        );

        PruneVersionsByDescriptor(descriptor.Id, _settings.Current.MaxVersionsPerProfile);
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
                        ProfileId = profileId,
                        CreatedAt = createdAt,
                        ArchivePath = file,
                    }
                );
            }
        }

        return versions.OrderByDescending(v => v.CreatedAt).ToList();
    }

    public List<ProfileVersion> GetVersions(ProfileDescriptor descriptor)
    {
        var versionDir = Path.Combine(VersionsRoot, descriptor.Id);
        if (!_fs.DirectoryExists(versionDir))
            return [];

        var versions = new List<ProfileVersion>();
        foreach (
            var file in _fs.GetFiles(versionDir, "*" + ArchiveExtension, SearchOption.TopDirectoryOnly)
        )
        {
            var versionId = Path.GetFileName(file)[..^ArchiveExtension.Length];
            if (
                !DateTime.TryParseExact(
                    versionId,
                    TimestampFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var createdAt
                )
            )
                continue;

            var metaPath = Path.Combine(versionDir, versionId + MetaExtension);
            VersionMeta? meta = null;
            if (_fs.FileExists(metaPath))
            {
                try
                {
                    var json = _fs.ReadAllText(metaPath);
                    meta = JsonSerializer.Deserialize<VersionMeta>(json, JsonOptions);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to read meta for version '{VersionId}' of '{DescriptorId}'.",
                        versionId,
                        descriptor.Id
                    );
                }
            }

            versions.Add(
                new ProfileVersion
                {
                    VersionId = versionId,
                    ProfileId = descriptor.Id,
                    CreatedAt = createdAt,
                    ArchivePath = file,
                    LocalProfileId = meta?.LocalProfileId,
                    Granularity = meta?.Granularity ?? ProfileGranularity.FullWtf,
                    AccountName = meta?.AccountName,
                    RealmName = meta?.RealmName,
                    CharacterName = meta?.CharacterName,
                }
            );
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
        _logger.LogInformation(
            "Profile '{ProfileId}' restored from version '{VersionId}'.",
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
            "Version '{VersionId}' deleted for profile '{ProfileId}'.",
            version.VersionId,
            version.ProfileId
        );
    }

    public void PruneVersions(string profileId, int maxVersions)
    {
        var versions = GetVersions(profileId);
        if (versions.Count <= maxVersions)
            return;

        var toDelete = versions.Skip(maxVersions).ToList();
        foreach (var version in toDelete)
            DeleteVersion(version);

        _logger.LogInformation(
            "Pruned {Count} old version(s) for profile '{ProfileId}'.",
            toDelete.Count,
            profileId
        );
    }

    private void PruneVersionsByDescriptor(string descriptorId, int maxVersions)
    {
        var versionDir = Path.Combine(VersionsRoot, descriptorId);
        if (!_fs.DirectoryExists(versionDir))
            return;

        var files = _fs
            .GetFiles(versionDir, "*" + ArchiveExtension, SearchOption.TopDirectoryOnly)
            .OrderByDescending(f => f)
            .Skip(maxVersions)
            .ToList();

        foreach (var file in files)
        {
            if (_fs.FileExists(file))
                _fs.DeleteFile(file);
            var metaPath = Path.ChangeExtension(file, null) + MetaExtension;
            if (_fs.FileExists(metaPath))
                _fs.DeleteFile(metaPath);
        }

        if (files.Count > 0)
            _logger.LogInformation(
                "Pruned {Count} old version(s) for scope '{DescriptorId}'.",
                files.Count,
                descriptorId
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

    private sealed class VersionMeta
    {
        public string? LocalProfileId { get; set; }
        public ProfileGranularity Granularity { get; set; }
        public string? AccountName { get; set; }
        public string? RealmName { get; set; }
        public string? CharacterName { get; set; }
    }
}
