using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using HearthSwing.Models.Templates;
using Microsoft.Extensions.Logging;

namespace HearthSwing.Services;

/// <summary>
/// Stores character templates under <c>&lt;ProfilesPath&gt;/.templates/&lt;templateId&gt;</c> and
/// exposes discovery, creation, rename and delete. The <c>.templates</c> folder is dot-prefixed so
/// it is ignored by normal profile folder enumeration.
/// </summary>
public sealed class TemplateCatalog : ITemplateCatalog
{
    public const string TemplatesFolderName = ".templates";
    private const string HistoryFolderName = ".history";
    private const string TemplateHistoryFolderName = "template";
    private const string LegacyTemplateVersionsFolderName = ".template-versions";
    private const string MetadataFileName = "template.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ISettingsService _settings;
    private readonly IFileSystem _fs;
    private readonly ILogger<TemplateCatalog> _logger;

    public TemplateCatalog(
        ISettingsService settings,
        IFileSystem fileSystem,
        ILogger<TemplateCatalog> logger
    )
    {
        _settings = settings;
        _fs = fileSystem;
        _logger = logger;
    }

    private string StorageRoot => Path.Combine(_settings.Current.ProfilesPath, TemplatesFolderName);

    private string TemplateHistoryRoot =>
        Path.Combine(_settings.Current.ProfilesPath, HistoryFolderName, TemplateHistoryFolderName);

    private string LegacyTemplateVersionsRoot =>
        Path.Combine(_settings.Current.ProfilesPath, LegacyTemplateVersionsFolderName);

    public List<TemplateSummary> DiscoverTemplates()
    {
        if (!_fs.DirectoryExists(StorageRoot))
            return [];

        var templates = new List<TemplateSummary>();
        foreach (var directoryPath in _fs.GetDirectories(StorageRoot))
        {
            var metadata = ReadMetadata(directoryPath);
            if (metadata is not null)
                templates.Add(ToSummary(metadata, directoryPath));
        }

        return templates
            .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public TemplateSummary? GetById(string templateId)
    {
        EnsureSafeTemplateId(templateId, nameof(templateId));

        var rootPath = Path.Combine(StorageRoot, templateId);
        if (!_fs.DirectoryExists(rootPath))
            return null;

        var metadata = ReadMetadata(rootPath);
        return metadata is null ? null : ToSummary(metadata, rootPath);
    }

    public TemplateSummary Create(
        string name,
        TemplateKind kind,
        string sourceAccountName,
        string? sourceRealmName,
        string? sourceCharacterName
    )
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Template name is required.", nameof(name));

        var normalizedName = name.Trim();

        if (!_fs.DirectoryExists(StorageRoot))
            _fs.CreateDirectory(StorageRoot);

        var templateId = BuildUniqueTemplateId(normalizedName);
        var rootPath = Path.Combine(StorageRoot, templateId);
        _fs.CreateDirectory(rootPath);

        var metadata = new TemplateMetadata
        {
            Id = templateId,
            Name = normalizedName,
            Kind = kind,
            SourceAccountName = sourceAccountName,
            SourceRealmName = sourceRealmName,
            SourceCharacterName = sourceCharacterName,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        WriteMetadata(rootPath, metadata);

        _logger.LogInformation(
            "Created template '{Name}' with id '{TemplateId}'.",
            normalizedName,
            templateId
        );

        return ToSummary(metadata, rootPath);
    }

    public void UpdateLastUpdated(string templateId, DateTimeOffset updatedAtUtc)
    {
        var (rootPath, metadata) = RequireTemplate(templateId);

        WriteMetadata(rootPath, CloneMetadata(metadata, metadata.Name, updatedAtUtc));
    }

    public void Rename(string templateId, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("Template name is required.", nameof(newName));

        var (rootPath, metadata) = RequireTemplate(templateId);

        WriteMetadata(rootPath, CloneMetadata(metadata, newName.Trim(), DateTimeOffset.UtcNow));

        _logger.LogInformation("Renamed template '{TemplateId}' to '{Name}'.", templateId, newName);
    }

    private static TemplateMetadata CloneMetadata(
        TemplateMetadata source,
        string name,
        DateTimeOffset lastUpdatedUtc
    ) =>
        new()
        {
            Id = source.Id,
            Name = name,
            Kind = source.Kind,
            SourceAccountName = source.SourceAccountName,
            SourceRealmName = source.SourceRealmName,
            SourceCharacterName = source.SourceCharacterName,
            CreatedAtUtc = source.CreatedAtUtc,
            LastUpdatedUtc = lastUpdatedUtc,
            SchemaVersion = source.SchemaVersion,
        };

    public void Delete(string templateId)
    {
        EnsureSafeTemplateId(templateId, nameof(templateId));

        var rootPath = Path.Combine(StorageRoot, templateId);
        if (_fs.DirectoryExists(rootPath))
        {
            ClearReadOnlyAttributes(rootPath);
            _fs.DeleteDirectory(rootPath, recursive: true);
        }

        var templateHistoryPath = Path.Combine(TemplateHistoryRoot, templateId);
        if (_fs.DirectoryExists(templateHistoryPath))
            _fs.DeleteDirectory(templateHistoryPath, recursive: true);

        var legacyVersionsPath = Path.Combine(LegacyTemplateVersionsRoot, templateId);
        if (_fs.DirectoryExists(legacyVersionsPath))
            _fs.DeleteDirectory(legacyVersionsPath, recursive: true);

        _logger.LogInformation("Deleted template '{TemplateId}'.", templateId);
    }

    private (string RootPath, TemplateMetadata Metadata) RequireTemplate(string templateId)
    {
        EnsureSafeTemplateId(templateId, nameof(templateId));

        var rootPath = Path.Combine(StorageRoot, templateId);
        var metadata =
            ReadMetadata(rootPath)
            ?? throw new InvalidOperationException(
                $"Template '{templateId}' was not found in storage."
            );

        return (rootPath, metadata);
    }

    private TemplateMetadata? ReadMetadata(string rootPath)
    {
        var metadataPath = Path.Combine(rootPath, MetadataFileName);
        if (!_fs.FileExists(metadataPath))
            return null;

        try
        {
            var json = _fs.ReadAllText(metadataPath);
            var metadata = JsonSerializer.Deserialize<TemplateMetadata>(json, JsonOptions);

            if (
                metadata is null
                || string.IsNullOrWhiteSpace(metadata.Id)
                || string.IsNullOrWhiteSpace(metadata.Name)
            )
            {
                _logger.LogWarning(
                    "Template metadata at {MetadataPath} is missing required fields.",
                    metadataPath
                );
                return null;
            }

            return metadata;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Template metadata at {MetadataPath} is invalid.", metadataPath);
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(
                ex,
                "Template metadata at {MetadataPath} could not be read.",
                metadataPath
            );
            return null;
        }
    }

    private void WriteMetadata(string rootPath, TemplateMetadata metadata)
    {
        if (!_fs.DirectoryExists(rootPath))
            _fs.CreateDirectory(rootPath);

        var json = JsonSerializer.Serialize(metadata, JsonOptions);
        _fs.WriteAllText(Path.Combine(rootPath, MetadataFileName), json);
    }

    private string BuildUniqueTemplateId(string name)
    {
        var baseId = SanitizeTemplateId(name);
        var candidate = baseId;
        var suffix = 2;

        while (_fs.DirectoryExists(Path.Combine(StorageRoot, candidate)))
        {
            candidate = $"{baseId}-{suffix}";
            suffix++;
        }

        return candidate;
    }

    private static TemplateSummary ToSummary(TemplateMetadata metadata, string rootPath) =>
        new()
        {
            Id = metadata.Id,
            Name = metadata.Name,
            Kind = metadata.Kind,
            RootPath = rootPath,
            SourceAccountName = metadata.SourceAccountName,
            SourceRealmName = metadata.SourceRealmName,
            SourceCharacterName = metadata.SourceCharacterName,
            CreatedAtUtc = metadata.CreatedAtUtc,
            LastUpdatedUtc = metadata.LastUpdatedUtc,
        };

    private static string SanitizeTemplateId(string name)
    {
        var candidate = name.Trim();
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            candidate = candidate.Replace(invalidChar, '_');

        candidate = candidate.Replace(' ', '-').Trim('.', '-', '_');
        return string.IsNullOrWhiteSpace(candidate) ? "template" : candidate;
    }

    private static void EnsureSafeTemplateId(string templateId, string paramName)
    {
        if (string.IsNullOrWhiteSpace(templateId))
            throw new ArgumentException("Template id is required.", paramName);

        var normalized = templateId.Trim();
        if (normalized is "." or "..")
            throw new InvalidOperationException("Template id is invalid.");

        if (
            normalized.Contains(Path.DirectorySeparatorChar)
            || normalized.Contains(Path.AltDirectorySeparatorChar)
        )
            throw new InvalidOperationException("Template id is invalid.");

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            if (normalized.Contains(invalidChar))
                throw new InvalidOperationException("Template id is invalid.");
        }
    }

    private void ClearReadOnlyAttributes(string directory)
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
}
