using System.Globalization;
using System.IO;
using HearthSwing.Models;
using Microsoft.Extensions.Logging;

namespace HearthSwing.Services;

/// <summary>
/// Template counterpart of <see cref="ProfileVersionService"/>. Archives the template folder under
/// <c>.template-versions</c> and restores it back into <c>.templates</c>.
/// </summary>
public sealed class TemplateVersionService : ITemplateVersionService
{
    private const string TemplatesFolderName = ".templates";
    private const string VersionsFolderName = ".template-versions";
    private const string TimestampFormat = "yyyyMMdd_HHmmss";
    private const string ArchiveExtension = ".tar.gz";

    private readonly IFileSystem _fs;
    private readonly ISettingsService _settings;
    private readonly ILogger<TemplateVersionService> _logger;
    private readonly IArchiveService _archive;

    public TemplateVersionService(
        IFileSystem fileSystem,
        ISettingsService settings,
        ILogger<TemplateVersionService> logger,
        IArchiveService archive
    )
    {
        _fs = fileSystem;
        _settings = settings;
        _logger = logger;
        _archive = archive;
    }

    private string TemplatesRoot =>
        Path.Combine(_settings.Current.ProfilesPath, TemplatesFolderName);

    private string VersionsRoot => Path.Combine(_settings.Current.ProfilesPath, VersionsFolderName);

    public async Task CreateVersionAsync(string templateId)
    {
        var templatePath = Path.Combine(TemplatesRoot, templateId);
        if (!_fs.DirectoryExists(templatePath))
        {
            _logger.LogWarning(
                "Template folder '{TemplateId}' not found — skipping version.",
                templateId
            );
            return;
        }

        var versionId = DateTime.Now.ToString(TimestampFormat);
        var templateVersionsDir = Path.Combine(VersionsRoot, templateId);
        _fs.CreateDirectory(templateVersionsDir);

        var archivePath = Path.Combine(templateVersionsDir, versionId + ArchiveExtension);
        await _archive.CompressDirectoryAsync(templatePath, archivePath);
        _logger.LogInformation(
            "Version '{VersionId}' created for template '{TemplateId}'.",
            versionId,
            templateId
        );

        PruneVersions(templateId, _settings.Current.MaxVersionsPerProfile);
    }

    public List<ProfileVersion> GetVersions(string templateId)
    {
        var templateVersionsDir = Path.Combine(VersionsRoot, templateId);
        if (!_fs.DirectoryExists(templateVersionsDir))
            return [];

        var versions = new List<ProfileVersion>();
        foreach (
            var file in _fs.GetFiles(
                templateVersionsDir,
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
                        ProfileId = templateId,
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
        var templatePath = Path.Combine(TemplatesRoot, version.ProfileId);

        if (_fs.DirectoryExists(templatePath))
        {
            ClearReadOnlyAttributes(templatePath);
            _fs.DeleteDirectory(templatePath, recursive: true);
        }

        _fs.CreateDirectory(templatePath);
        await _archive.ExtractToDirectoryAsync(version.ArchivePath, templatePath);
        _logger.LogInformation(
            "Template '{TemplateId}' restored from version '{VersionId}'.",
            version.ProfileId,
            version.VersionId
        );
    }

    public void DeleteVersion(ProfileVersion version)
    {
        if (!_fs.FileExists(version.ArchivePath))
            return;

        _fs.DeleteFile(version.ArchivePath);
        _logger.LogInformation(
            "Version '{VersionId}' deleted for template '{TemplateId}'.",
            version.VersionId,
            version.ProfileId
        );
    }

    public void PruneVersions(string templateId, int maxVersions)
    {
        var versions = GetVersions(templateId);
        if (versions.Count <= maxVersions)
            return;

        var toDelete = versions.Skip(maxVersions).ToList();
        foreach (var version in toDelete)
            DeleteVersion(version);

        _logger.LogInformation(
            "Pruned {Count} old version(s) for template '{TemplateId}'.",
            toDelete.Count,
            templateId
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
