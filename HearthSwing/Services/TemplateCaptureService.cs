using System.IO;
using HearthSwing.Models.Templates;
using HearthSwing.Models.WoW;
using Microsoft.Extensions.Logging;

namespace HearthSwing.Services;

/// <summary>
/// Builds templates from a live donor. Account templates copy the account's shared settings as-is.
/// Character templates capture a single character folder, tokenizing the donor's character and realm
/// names inside text files and replacing them with token folder names on disk.
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

    public TemplateSummary CreateAccountTemplate(WowAccount source, string templateName)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(templateName))
            throw new ArgumentException("Template name is required.", nameof(templateName));

        var template = _catalog.Create(
            templateName,
            TemplateKind.Account,
            source.AccountName,
            sourceRealmName: null,
            sourceCharacterName: null
        );

        CaptureAccountSettings(source.FolderPath, template);

        _catalog.UpdateLastUpdated(template.Id, DateTimeOffset.UtcNow);

        _logger.LogInformation(
            "Captured account template '{Name}' from account '{Account}'.",
            template.Name,
            source.AccountName
        );

        return _catalog.GetById(template.Id) ?? template;
    }

    public TemplateSummary CreateCharacterTemplate(WowCharacter source, string templateName)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(templateName))
            throw new ArgumentException("Template name is required.", nameof(templateName));

        var template = _catalog.Create(
            templateName,
            TemplateKind.Character,
            source.AccountName,
            source.RealmName,
            source.CharacterName
        );

        CaptureCharacter(source, template);

        _catalog.UpdateLastUpdated(template.Id, DateTimeOffset.UtcNow);

        _logger.LogInformation(
            "Captured character template '{Name}' from character '{Character}' on realm '{Realm}'.",
            template.Name,
            source.CharacterName,
            source.RealmName
        );

        return _catalog.GetById(template.Id) ?? template;
    }

    public TemplateSummary UpdateAccountTemplate(TemplateSummary template, WowAccount source)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(source);

        ClearContentFolder(Path.Combine(template.RootPath, TemplateLayout.AccountFolderName));
        CaptureAccountSettings(source.FolderPath, template);

        _catalog.UpdateLastUpdated(template.Id, DateTimeOffset.UtcNow);

        _logger.LogInformation(
            "Updated account template '{Name}' from account '{Account}'.",
            template.Name,
            source.AccountName
        );

        return _catalog.GetById(template.Id) ?? template;
    }

    public TemplateSummary UpdateCharacterTemplate(TemplateSummary template, WowCharacter source)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(source);

        ClearContentFolder(Path.Combine(template.RootPath, TemplateLayout.CharacterFolderName));
        CaptureCharacter(source, template);

        _catalog.UpdateLastUpdated(template.Id, DateTimeOffset.UtcNow);

        _logger.LogInformation(
            "Updated character template '{Name}' from character '{Character}' on realm '{Realm}'.",
            template.Name,
            source.CharacterName,
            source.RealmName
        );

        return _catalog.GetById(template.Id) ?? template;
    }

    private void CaptureAccountSettings(string accountPath, TemplateSummary template)
    {
        if (!_fs.DirectoryExists(accountPath))
            return;

        var destinationRoot = Path.Combine(template.RootPath, TemplateLayout.AccountFolderName);

        foreach (var relativePath in _layout.CollectAccountSettingsRelativePaths(accountPath))
        {
            var sourceFile = Path.Combine(accountPath, relativePath);
            if (!_fs.FileExists(sourceFile))
                continue;

            var destinationFile = Path.Combine(destinationRoot, relativePath);
            EnsureParentDirectory(destinationFile);
            _fs.CopyFile(sourceFile, destinationFile);
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
            CaptureCharacterFile(
                Path.Combine(source.FolderPath, relativePath),
                Path.Combine(destinationRoot, relativePath),
                relativePath,
                source
            );
        }
    }

    private void CaptureCharacterFile(
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

    private void ClearContentFolder(string folder)
    {
        if (!_fs.DirectoryExists(folder))
            return;

        foreach (var file in _fs.GetFiles(folder, "*", SearchOption.AllDirectories))
        {
            var attributes = _fs.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                _fs.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
        }

        _fs.DeleteDirectory(folder, recursive: true);
    }
}
