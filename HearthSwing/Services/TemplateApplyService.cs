using System.IO;
using HearthSwing.Models.Templates;
using HearthSwing.Models.WoW;
using Microsoft.Extensions.Logging;

namespace HearthSwing.Services;

/// <summary>
/// Applies templates onto live targets. Character templates are expanded into a staging folder
/// (re-personalizing tokens with the target's names) and swapped into the target character folder
/// with rollback. Character templates also restore the account-level Shared folder so a full
/// character restore replays the donor's shared settings. Account templates overlay the target
/// account's shared settings, preserving the account's other realm/character folders.
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

    public void ApplyAccountTemplate(
        TemplateSummary template,
        WowAccount target,
        TemplateApplyScope scope = TemplateApplyScope.Full,
        bool useDirectorySwap = true
    )
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(target);

        var templateAccountRoot = Path.Combine(template.RootPath, TemplateLayout.AccountFolderName);

        if (!_fs.DirectoryExists(templateAccountRoot))
            throw new InvalidOperationException(
                $"Template '{template.Id}' has no account data to apply."
            );

        if (scope == TemplateApplyScope.Full && useDirectorySwap)
        {
            var sourceSavedVariables = Path.Combine(templateAccountRoot, SavedVariablesFolderName);
            if (_fs.DirectoryExists(sourceSavedVariables))
            {
                _replacer.ReplaceDirectory(
                    sourceSavedVariables,
                    Path.Combine(target.FolderPath, SavedVariablesFolderName)
                );
            }

            OverlayTopLevelFiles(templateAccountRoot, target.FolderPath);
        }
        else
        {
            ApplyScopedFiles(
                templateAccountRoot,
                target.FolderPath,
                targetCharacter: null,
                scope
            );
        }

        _logger.LogInformation(
            "Applied account template '{Name}' to account '{Account}'.",
            template.Name,
            target.AccountName
        );
    }

    public void ApplyCharacterTemplate(
        TemplateSummary template,
        WowCharacter target,
        TemplateApplyScope scope = TemplateApplyScope.Full,
        bool includeAccountScoped = true,
        bool useDirectorySwap = true
    )
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(target);

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

        if (scope == TemplateApplyScope.Full && useDirectorySwap)
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

            if (includeAccountScoped)
                ApplySharedAccountTemplate(template, target, TemplateApplyScope.Full, true);
        }
        else
        {
            ApplyScopedFiles(templateCharRoot, target.FolderPath, target, scope);
            if (includeAccountScoped)
                ApplySharedAccountTemplate(template, target, scope, useDirectorySwap);
        }

        _logger.LogInformation(
            "Applied character template '{Name}' to character '{Character}' on realm '{Realm}'.",
            template.Name,
            target.CharacterName,
            target.RealmName
        );
    }

    private void OverlayTopLevelFiles(string sourceAccountPath, string targetAccountPath)
    {
        if (!_fs.DirectoryExists(targetAccountPath))
            _fs.CreateDirectory(targetAccountPath);

        foreach (
            var filePath in _fs.GetFiles(sourceAccountPath, "*", SearchOption.TopDirectoryOnly)
        )
        {
            var destination = Path.Combine(targetAccountPath, Path.GetFileName(filePath));
            ClearReadOnlyIfNeeded(destination);
            _fs.CopyFile(filePath, destination);
        }
    }

    private void ApplySharedAccountTemplate(
        TemplateSummary template,
        WowCharacter target,
        TemplateApplyScope scope,
        bool useDirectorySwap
    )
    {
        var templateSharedRoot = Path.Combine(template.RootPath, TemplateLayout.SharedFolderName);
        if (!_fs.DirectoryExists(templateSharedRoot))
            return;

        var targetAccountRoot = GetTargetAccountRoot(target.FolderPath);

        if (scope == TemplateApplyScope.CacheOnly || !useDirectorySwap)
        {
            ApplyScopedFiles(templateSharedRoot, targetAccountRoot, targetCharacter: null, scope);
            return;
        }

        var sourceSavedVariables = Path.Combine(templateSharedRoot, SavedVariablesFolderName);

        if (_fs.DirectoryExists(sourceSavedVariables))
        {
            _replacer.ReplaceDirectory(
                sourceSavedVariables,
                Path.Combine(targetAccountRoot, SavedVariablesFolderName)
            );
        }

        OverlayTopLevelFiles(templateSharedRoot, targetAccountRoot);
    }

    private void ApplyScopedFiles(
        string sourceRoot,
        string targetRoot,
        WowCharacter? targetCharacter,
        TemplateApplyScope scope
    )
    {
        foreach (var filePath in _fs.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, filePath);
            if (scope == TemplateApplyScope.CacheOnly && !IsTokenizableCacheFile(relativePath))
                continue;

            var destination = Path.Combine(targetRoot, relativePath);
            EnsureParentDirectory(destination);
            ClearReadOnlyIfNeeded(destination);

            if (targetCharacter is not null && _classifier.ShouldTokenize(relativePath))
            {
                var content = _fs.ReadAllText(filePath);
                var expanded = _tokenizer.Expand(
                    content,
                    targetCharacter.CharacterName,
                    targetCharacter.RealmName
                );
                _fs.WriteAllText(destination, expanded);
            }
            else
            {
                _fs.WriteAllBytes(destination, _fs.ReadAllBytes(filePath));
            }
        }
    }

    private static bool IsTokenizableCacheFile(string relativePath)
    {
        return CacheFilePatterns.IsTokenizableCacheFileName(Path.GetFileName(relativePath));
    }

    private static string GetTargetAccountRoot(string targetCharacterFolder)
    {
        var realmFolder = Path.GetDirectoryName(targetCharacterFolder);
        var accountRoot = realmFolder is null ? null : Path.GetDirectoryName(realmFolder);

        return !string.IsNullOrEmpty(accountRoot)
            ? accountRoot
            : throw new InvalidOperationException(
                $"Could not resolve account root from character folder '{targetCharacterFolder}'."
            );
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
}
