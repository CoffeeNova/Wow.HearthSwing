using System.IO;
using HearthSwing.Models.Templates;
using HearthSwing.Models.WoW;
using Microsoft.Extensions.Logging;

namespace HearthSwing.Services;

/// <summary>
/// Builds a depersonalized template from a donor character: account-level settings plus the donor's
/// character folder, with the donor's character and realm names tokenized inside text files and
/// replaced with token folder names on disk.
/// </summary>
public sealed class TemplateCaptureService : ITemplateCaptureService
{
    private readonly ITemplateCatalog _catalog;
    private readonly IAccountSnapshotLayout _layout;
    private readonly ITemplateTokenizer _tokenizer;
    private readonly ITemplateFileClassifier _classifier;
    private readonly IFileSystem _fs;
    private readonly ILogger<TemplateCaptureService> _logger;

    public TemplateCaptureService(
        ITemplateCatalog catalog,
        IAccountSnapshotLayout layout,
        ITemplateTokenizer tokenizer,
        ITemplateFileClassifier classifier,
        IFileSystem fileSystem,
        ILogger<TemplateCaptureService> logger
    )
    {
        _catalog = catalog;
        _layout = layout;
        _tokenizer = tokenizer;
        _classifier = classifier;
        _fs = fileSystem;
        _logger = logger;
    }

    public TemplateSummary CreateTemplate(WowCharacter source, string templateName)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(templateName))
            throw new ArgumentException("Template name is required.", nameof(templateName));

        var template = _catalog.Create(
            templateName,
            source.AccountName,
            source.RealmName,
            source.CharacterName
        );

        CaptureAccountSettings(source, template);
        CaptureCharacter(source, template);

        _catalog.UpdateLastUpdated(template.Id, DateTimeOffset.UtcNow);

        _logger.LogInformation(
            "Captured template '{Name}' from character '{Character}' on realm '{Realm}'.",
            template.Name,
            source.CharacterName,
            source.RealmName
        );

        return _catalog.GetById(template.Id) ?? template;
    }

    private void CaptureAccountSettings(WowCharacter source, TemplateSummary template)
    {
        var accountPath = GetAccountPath(source.FolderPath);
        if (accountPath is null || !_fs.DirectoryExists(accountPath))
            return;

        var destinationRoot = Path.Combine(template.RootPath, TemplateLayout.AccountFolderName);

        foreach (var relativePath in _layout.CollectAccountSettingsRelativePaths(accountPath))
        {
            CaptureFile(
                Path.Combine(accountPath, relativePath),
                Path.Combine(destinationRoot, relativePath),
                relativePath,
                source
            );
        }
    }

    private void CaptureCharacter(WowCharacter source, TemplateSummary template)
    {
        if (!_fs.DirectoryExists(source.FolderPath))
            return;

        var destinationRoot = Path.Combine(
            template.RootPath,
            TemplateLayout.CharacterFolderName,
            TemplateLayout.RealmTokenFolderName,
            TemplateLayout.CharTokenFolderName
        );

        foreach (var relativePath in _layout.CollectCharacterRelativePaths(source.FolderPath))
        {
            CaptureFile(
                Path.Combine(source.FolderPath, relativePath),
                Path.Combine(destinationRoot, relativePath),
                relativePath,
                source
            );
        }
    }

    private void CaptureFile(
        string sourceFile,
        string destinationFile,
        string relativePath,
        WowCharacter source
    )
    {
        if (!_fs.FileExists(sourceFile))
            return;

        EnsureParentDirectory(destinationFile);

        if (_classifier.ShouldTokenize(relativePath))
        {
            var content = _fs.ReadAllText(sourceFile);
            var tokenized = _tokenizer.Tokenize(content, source.CharacterName, source.RealmName);
            _fs.WriteAllText(destinationFile, tokenized);
        }
        else
        {
            _fs.CopyFile(sourceFile, destinationFile);
        }
    }

    private void EnsureParentDirectory(string filePath)
    {
        var parent = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(parent) && !_fs.DirectoryExists(parent))
            _fs.CreateDirectory(parent);
    }

    private static string? GetAccountPath(string characterFolderPath)
    {
        var realmFolder = Path.GetDirectoryName(characterFolderPath);
        return realmFolder is null ? null : Path.GetDirectoryName(realmFolder);
    }
}
