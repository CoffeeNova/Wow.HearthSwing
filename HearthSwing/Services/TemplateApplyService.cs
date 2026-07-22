using System.IO;
using HearthSwing.Models.Templates;
using HearthSwing.Models.WoW;
using Microsoft.Extensions.Logging;

namespace HearthSwing.Services;

/// <summary>
/// Re-personalizes a template into a staging folder (expanding tokens with the target's names) and
/// swaps it into the target's live character folder with rollback. Account-level settings, when
/// requested, are overlaid so that the target account's other realm/character folders are preserved.
/// </summary>
public sealed class TemplateApplyService : ITemplateApplyService
{
    private const string SavedVariablesFolderName = "SavedVariables";
    private const string StagingFolderPrefix = ".template-staging-";

    private readonly IDirectoryReplacer _replacer;
    private readonly ITemplateTokenizer _tokenizer;
    private readonly ITemplateFileClassifier _classifier;
    private readonly IFileSystem _fs;
    private readonly ILogger<TemplateApplyService> _logger;

    public TemplateApplyService(
        IDirectoryReplacer replacer,
        ITemplateTokenizer tokenizer,
        ITemplateFileClassifier classifier,
        IFileSystem fileSystem,
        ILogger<TemplateApplyService> logger
    )
    {
        _replacer = replacer;
        _tokenizer = tokenizer;
        _classifier = classifier;
        _fs = fileSystem;
        _logger = logger;
    }

    public void ApplyTemplate(
        TemplateSummary template,
        WowCharacter target,
        TemplateApplyOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);

        var templateCharRoot = Path.Combine(
            template.RootPath,
            TemplateLayout.CharacterFolderName,
            TemplateLayout.RealmTokenFolderName,
            TemplateLayout.CharTokenFolderName
        );

        if (!_fs.DirectoryExists(templateCharRoot))
            throw new InvalidOperationException(
                $"Template '{template.Id}' has no character data to apply."
            );

        ApplyCharacter(templateCharRoot, target);

        if (options.IncludeAccountSettings)
        {
            var templateAccountRoot = Path.Combine(
                template.RootPath,
                TemplateLayout.AccountFolderName
            );
            if (_fs.DirectoryExists(templateAccountRoot))
                ApplyAccountSettings(templateAccountRoot, target);
        }

        _logger.LogInformation(
            "Applied template '{Name}' to character '{Character}' on realm '{Realm}'.",
            template.Name,
            target.CharacterName,
            target.RealmName
        );
    }

    private void ApplyCharacter(string templateCharRoot, WowCharacter target)
    {
        var staging = CreateStagingPath(target.FolderPath);
        try
        {
            ExpandTreeToStaging(templateCharRoot, staging, target);
            _replacer.ReplaceDirectory(staging, target.FolderPath);
        }
        finally
        {
            CleanupStaging(staging);
        }
    }

    private void ApplyAccountSettings(string templateAccountRoot, WowCharacter target)
    {
        var targetAccountPath = GetAccountPath(target.FolderPath);
        if (targetAccountPath is null)
            return;

        var staging = CreateStagingPath(targetAccountPath);
        try
        {
            ExpandTreeToStaging(templateAccountRoot, staging, target);

            var stagingSavedVariables = Path.Combine(staging, SavedVariablesFolderName);
            if (_fs.DirectoryExists(stagingSavedVariables))
            {
                _replacer.ReplaceDirectory(
                    stagingSavedVariables,
                    Path.Combine(targetAccountPath, SavedVariablesFolderName)
                );
            }

            OverlayTopLevelFiles(staging, targetAccountPath);
        }
        finally
        {
            CleanupStaging(staging);
        }
    }

    private void OverlayTopLevelFiles(string stagingAccountPath, string targetAccountPath)
    {
        if (!_fs.DirectoryExists(targetAccountPath))
            _fs.CreateDirectory(targetAccountPath);

        foreach (
            var filePath in _fs.GetFiles(stagingAccountPath, "*", SearchOption.TopDirectoryOnly)
        )
        {
            var destination = Path.Combine(targetAccountPath, Path.GetFileName(filePath));
            ClearReadOnlyIfNeeded(destination);
            _fs.CopyFile(filePath, destination);
        }
    }

    private void ExpandTreeToStaging(string sourceRoot, string stagingRoot, WowCharacter target)
    {
        foreach (var filePath in _fs.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, filePath);
            var destination = Path.Combine(stagingRoot, relativePath);
            EnsureParentDirectory(destination);

            if (_classifier.ShouldTokenize(relativePath))
            {
                var content = _fs.ReadAllText(filePath);
                var expanded = _tokenizer.Expand(content, target.CharacterName, target.RealmName);
                _fs.WriteAllText(destination, expanded);
            }
            else
            {
                _fs.CopyFile(filePath, destination);
            }
        }
    }

    private void EnsureParentDirectory(string filePath)
    {
        var parent = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(parent) && !_fs.DirectoryExists(parent))
            _fs.CreateDirectory(parent);
    }

    private void ClearReadOnlyIfNeeded(string filePath)
    {
        if (!_fs.FileExists(filePath))
            return;

        var attributes = _fs.GetAttributes(filePath);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            _fs.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
    }

    private static string CreateStagingPath(string anchorPath)
    {
        var parentDirectory = Path.GetDirectoryName(anchorPath);
        if (string.IsNullOrEmpty(parentDirectory))
            throw new InvalidOperationException(
                $"Could not create a staging path for '{anchorPath}'."
            );

        return Path.Combine(parentDirectory, $"{StagingFolderPrefix}{Guid.NewGuid():N}");
    }

    private void CleanupStaging(string stagingPath)
    {
        if (!_fs.DirectoryExists(stagingPath))
            return;

        try
        {
            _replacer.ClearReadOnlyAttributes(stagingPath);
            _fs.DeleteDirectory(stagingPath, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up staging directory {Path}.", stagingPath);
        }
    }

    private static string? GetAccountPath(string characterFolderPath)
    {
        var realmFolder = Path.GetDirectoryName(characterFolderPath);
        return realmFolder is null ? null : Path.GetDirectoryName(realmFolder);
    }
}
