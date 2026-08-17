using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using HearthSwing.Models;
using HearthSwing.Models.WoW;

namespace HearthSwing.Services;

public sealed class ChangeHistoryService : IChangeHistoryService
{
    private const string HistoryFolderName = ".history";
    private const string TemplatesFolderName = ".templates";
    private const string SavedVariablesFolderName = "SavedVariables";
    private const string IndexFileName = "index.json";
    private const string ArchiveExtension = ".tar.gz";
    private const int DefaultMaxEntriesPerTarget = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IArchiveService _archiveService;
    private readonly IFileSystem _fileSystem;
    private readonly IDirectoryReplacer _directoryReplacer;
    private readonly IWtfInspector _wtfInspector;
    private readonly ISettingsService _settingsService;

    public ChangeHistoryService(
        IArchiveService archiveService,
        IFileSystem fileSystem,
        IDirectoryReplacer directoryReplacer,
        IWtfInspector wtfInspector,
        ISettingsService settingsService
    )
    {
        _archiveService = archiveService;
        _fileSystem = fileSystem;
        _directoryReplacer = directoryReplacer;
        _wtfInspector = wtfInspector;
        _settingsService = settingsService;
    }

    public event Action<string>? Log;

    public async Task<HistoryEntry> SnapshotAsync(
        string targetKey,
        HistoryTargetKind kind,
        string sourceFolder,
        string description,
        CancellationToken ct = default
    )
    {
        var normalizedTargetKey = ValidateAndNormalizeTargetKey(targetKey, kind);
        if (string.IsNullOrWhiteSpace(sourceFolder))
            throw new ArgumentException("Source folder is required.", nameof(sourceFolder));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required.", nameof(description));
        if (!_fileSystem.DirectoryExists(sourceFolder))
        {
            throw new InvalidOperationException(
                $"Could not create history snapshot because source folder '{sourceFolder}' does not exist."
            );
        }

        ct.ThrowIfCancellationRequested();

        var targetHistoryFolder = GetTargetHistoryFolder(normalizedTargetKey);
        _fileSystem.CreateDirectory(targetHistoryFolder);

        var createdUtc = DateTimeOffset.UtcNow;
        var archiveFileName =
            $"{createdUtc:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}{ArchiveExtension}";
        var archivePath = Path.Combine(targetHistoryFolder, archiveFileName);

        var archiveSourceFolder = sourceFolder;
        var temporarySourceFolder = string.Empty;

        if (kind == HistoryTargetKind.WtfAccount)
        {
            temporarySourceFolder = BuildTemporaryFolderPath(
                targetHistoryFolder,
                ".account-snapshot-"
            );
            BuildAccountScopedSnapshotContent(sourceFolder, temporarySourceFolder);
            archiveSourceFolder = temporarySourceFolder;
        }

        try
        {
            await _archiveService.CompressDirectoryAsync(archiveSourceFolder, archivePath, ct);
        }
        finally
        {
            CleanupTemporaryFolder(temporarySourceFolder);
        }

        var descriptor = BuildDescriptor(normalizedTargetKey, kind);
        var entry = new HistoryEntry
        {
            TargetKey = normalizedTargetKey,
            Kind = kind,
            CreatedUtc = createdUtc,
            Description = description,
            ArchivePath = archivePath,
            SizeBytes = _fileSystem.FileExists(archivePath)
                ? _fileSystem.GetFileLength(archivePath)
                : 0,
            AccountName = descriptor.accountName,
            RealmName = descriptor.realmName,
            CharacterName = descriptor.characterName,
        };

        var index = ReadIndex(targetHistoryFolder);
        index.Add(entry);
        TrimIndex(index, GetMaxEntriesPerTarget());
        WriteIndex(targetHistoryFolder, index);

        RaiseLog($"History snapshot created for '{normalizedTargetKey}'.");
        return entry;
    }

    public IReadOnlyList<HistoryEntry> List(string targetKey)
    {
        var normalizedTargetKey = ValidateAndNormalizeTargetKey(targetKey);
        var targetHistoryFolder = GetTargetHistoryFolder(normalizedTargetKey);

        return ReadIndex(targetHistoryFolder).OrderByDescending(entry => entry.CreatedUtc).ToList();
    }

    public IReadOnlyList<HistoryEntry> ListAll()
    {
        var historyRoot = GetHistoryRoot();
        if (!_fileSystem.DirectoryExists(historyRoot))
            return [];

        var allEntries = new List<HistoryEntry>();
        foreach (var folder in EnumerateHistoryFolders(historyRoot))
            allEntries.AddRange(ReadIndex(folder));

        return allEntries.OrderByDescending(entry => entry.CreatedUtc).ToList();
    }

    public async Task RestoreAsync(HistoryEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ct.ThrowIfCancellationRequested();

        var normalizedTargetKey = ValidateAndNormalizeTargetKey(entry.TargetKey, entry.Kind);

        if (!_fileSystem.FileExists(entry.ArchivePath))
            throw new FileNotFoundException("History archive not found.", entry.ArchivePath);

        var targetFolder = ResolveTargetFolder(entry);
        var tempExtractFolder = BuildRestoreTempPath(entry.ArchivePath);
        _fileSystem.CreateDirectory(tempExtractFolder);

        try
        {
            await _archiveService.ExtractToDirectoryAsync(entry.ArchivePath, tempExtractFolder, ct);
            ct.ThrowIfCancellationRequested();

            if (_fileSystem.DirectoryExists(targetFolder))
            {
                await SnapshotAsync(
                    normalizedTargetKey,
                    entry.Kind,
                    targetFolder,
                    $"Before restoring history from {entry.CreatedUtc:O}",
                    ct
                );
            }

            if (entry.Kind == HistoryTargetKind.WtfAccount)
                RestoreAccountScopedTarget(tempExtractFolder, targetFolder);
            else
                _directoryReplacer.ReplaceDirectory(tempExtractFolder, targetFolder);

            RaiseLog($"History restored for '{normalizedTargetKey}'.");
        }
        finally
        {
            CleanupTemporaryFolder(tempExtractFolder);
        }
    }

    public Task DeleteAsync(HistoryEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ct.ThrowIfCancellationRequested();

        var normalizedTargetKey = ValidateAndNormalizeTargetKey(entry.TargetKey, entry.Kind);

        if (_fileSystem.FileExists(entry.ArchivePath))
            _fileSystem.DeleteFile(entry.ArchivePath);

        var targetHistoryFolder = GetTargetHistoryFolder(normalizedTargetKey);
        var index = ReadIndex(targetHistoryFolder);
        index.RemoveAll(candidate =>
            string.Equals(
                candidate.ArchivePath,
                entry.ArchivePath,
                StringComparison.OrdinalIgnoreCase
            )
        );
        WriteIndex(targetHistoryFolder, index);

        RaiseLog($"History entry deleted for '{normalizedTargetKey}'.");
        return Task.CompletedTask;
    }

    private string ResolveTargetFolder(HistoryEntry entry)
    {
        if (entry.Kind == HistoryTargetKind.Template)
        {
            var templateId = GetTemplateId(entry.TargetKey);
            var templatesRoot = Path.Combine(
                _settingsService.Current.ProfilesPath,
                TemplatesFolderName
            );
            return EnsurePathUnderRoot(
                templatesRoot,
                Path.Combine(templatesRoot, templateId),
                "template"
            );
        }

        var installation = _wtfInspector.Inspect(_settingsService.Current.GamePath);
        var accountName = entry.AccountName;

        if (string.IsNullOrWhiteSpace(accountName))
        {
            throw new InvalidOperationException(
                $"History entry '{entry.TargetKey}' does not contain an account descriptor."
            );
        }

        var account = installation.Accounts.FirstOrDefault(candidate =>
            string.Equals(candidate.AccountName, accountName, StringComparison.OrdinalIgnoreCase)
        );
        if (account is null)
            throw new InvalidOperationException($"WoW account '{accountName}' was not found.");

        if (entry.Kind == HistoryTargetKind.WtfAccount)
            return account.FolderPath;

        var realmName = entry.RealmName;
        var characterName = entry.CharacterName;

        if (string.IsNullOrWhiteSpace(realmName) || string.IsNullOrWhiteSpace(characterName))
        {
            throw new InvalidOperationException(
                $"History entry '{entry.TargetKey}' does not contain a character descriptor."
            );
        }

        var realm = account.Realms.FirstOrDefault(candidate =>
            string.Equals(candidate.RealmName, realmName, StringComparison.OrdinalIgnoreCase)
        );
        if (realm is null)
            throw new InvalidOperationException($"WoW realm '{realmName}' was not found.");

        var character = realm.Characters.FirstOrDefault(candidate =>
            string.Equals(
                candidate.CharacterName,
                characterName,
                StringComparison.OrdinalIgnoreCase
            )
        );
        if (character is null)
        {
            throw new InvalidOperationException(
                $"WoW character '{characterName}' was not found on realm '{realmName}'."
            );
        }

        return character.FolderPath;
    }

    private void RestoreAccountScopedTarget(string extractedRoot, string accountRoot)
    {
        if (!_fileSystem.DirectoryExists(accountRoot))
            _fileSystem.CreateDirectory(accountRoot);

        var sourceSavedVariables = Path.Combine(extractedRoot, SavedVariablesFolderName);
        var targetSavedVariables = Path.Combine(accountRoot, SavedVariablesFolderName);

        if (_fileSystem.DirectoryExists(sourceSavedVariables))
            _directoryReplacer.ReplaceDirectory(sourceSavedVariables, targetSavedVariables);

        foreach (
            var sourceFilePath in _fileSystem.GetFiles(
                extractedRoot,
                "*",
                SearchOption.TopDirectoryOnly
            )
        )
        {
            var destinationPath = Path.Combine(accountRoot, Path.GetFileName(sourceFilePath));
            ClearReadOnlyIfNeeded(destinationPath);
            _fileSystem.CopyFile(sourceFilePath, destinationPath);
        }
    }

    private void BuildAccountScopedSnapshotContent(string accountRoot, string destinationRoot)
    {
        _fileSystem.CreateDirectory(destinationRoot);

        var sourceSavedVariables = Path.Combine(accountRoot, SavedVariablesFolderName);
        var destinationSavedVariables = Path.Combine(destinationRoot, SavedVariablesFolderName);
        CopyDirectoryContent(sourceSavedVariables, destinationSavedVariables);

        foreach (
            var sourceFilePath in _fileSystem.GetFiles(
                accountRoot,
                "*",
                SearchOption.TopDirectoryOnly
            )
        )
        {
            var destinationPath = Path.Combine(destinationRoot, Path.GetFileName(sourceFilePath));
            _fileSystem.WriteAllBytes(destinationPath, _fileSystem.ReadAllBytes(sourceFilePath));
        }
    }

    private void CopyDirectoryContent(string sourceRoot, string destinationRoot)
    {
        if (!_fileSystem.DirectoryExists(sourceRoot))
            return;

        _fileSystem.CreateDirectory(destinationRoot);
        foreach (
            var sourceFilePath in _fileSystem.GetFiles(sourceRoot, "*", SearchOption.AllDirectories)
        )
        {
            var relativePath = Path.GetRelativePath(sourceRoot, sourceFilePath);
            var destinationPath = Path.Combine(destinationRoot, relativePath);
            EnsureParentDirectory(destinationPath);
            _fileSystem.WriteAllBytes(destinationPath, _fileSystem.ReadAllBytes(sourceFilePath));
        }
    }

    private void EnsureParentDirectory(string filePath)
    {
        var parent = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(parent) && !_fileSystem.DirectoryExists(parent))
            _fileSystem.CreateDirectory(parent);
    }

    private void ClearReadOnlyIfNeeded(string filePath)
    {
        if (!_fileSystem.FileExists(filePath))
            return;

        var attributes = _fileSystem.GetAttributes(filePath);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            _fileSystem.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
    }

    private void CleanupTemporaryFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !_fileSystem.DirectoryExists(path))
            return;

        try
        {
            _directoryReplacer.ClearReadOnlyAttributes(path);
            _fileSystem.DeleteDirectory(path, recursive: true);
        }
        catch
        {
            RaiseLog($"Warning: could not remove temporary history folder '{path}'.");
        }
    }

    private List<HistoryEntry> ReadIndex(string targetHistoryFolder)
    {
        var indexPath = Path.Combine(targetHistoryFolder, IndexFileName);
        if (!_fileSystem.FileExists(indexPath))
            return [];

        try
        {
            var json = _fileSystem.ReadAllText(indexPath);
            return JsonSerializer.Deserialize<List<HistoryEntry>>(json, JsonOptions) ?? [];
        }
        catch
        {
            RaiseLog($"Warning: could not parse history index '{indexPath}'.");
            return [];
        }
    }

    private void WriteIndex(string targetHistoryFolder, List<HistoryEntry> entries)
    {
        var ordered = entries.OrderByDescending(entry => entry.CreatedUtc).ToList();
        var indexPath = Path.Combine(targetHistoryFolder, IndexFileName);
        var json = JsonSerializer.Serialize(ordered, JsonOptions);
        _fileSystem.WriteAllText(indexPath, json);
    }

    private void TrimIndex(List<HistoryEntry> entries, int maxEntries)
    {
        if (entries.Count <= maxEntries)
            return;

        var ordered = entries.OrderByDescending(entry => entry.CreatedUtc).ToList();
        var toDelete = ordered.Skip(maxEntries).ToList();

        foreach (var entry in toDelete)
        {
            if (_fileSystem.FileExists(entry.ArchivePath))
                _fileSystem.DeleteFile(entry.ArchivePath);
        }

        entries.RemoveAll(entry =>
            toDelete.Any(candidate =>
                string.Equals(
                    candidate.ArchivePath,
                    entry.ArchivePath,
                    StringComparison.OrdinalIgnoreCase
                )
            )
        );
    }

    private int GetMaxEntriesPerTarget()
    {
        var configured = _settingsService.Current.MaxHistoryEntriesPerTarget;
        return configured > 0 ? configured : DefaultMaxEntriesPerTarget;
    }

    private string GetHistoryRoot()
    {
        return Path.Combine(_settingsService.Current.ProfilesPath, HistoryFolderName);
    }

    private string GetTargetHistoryFolder(string targetKey)
    {
        var sanitizedKeyPath = SanitizeTargetKey(targetKey)
            .Replace('/', Path.DirectorySeparatorChar);
        var historyRoot = GetHistoryRoot();
        var candidate = Path.Combine(historyRoot, sanitizedKeyPath);
        return EnsurePathUnderRoot(historyRoot, candidate, "history");
    }

    private static string SanitizeTargetKey(string targetKey)
    {
        var segments = targetKey
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeSegment)
            .ToArray();

        return string.Join('/', segments);
    }

    private static string SanitizeSegment(string segment)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitizedChars = segment.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray();

        var sanitized = new string(sanitizedChars).Trim();
        return string.IsNullOrEmpty(sanitized) ? "_" : sanitized;
    }

    private static string ValidateAndNormalizeTargetKey(
        string targetKey,
        HistoryTargetKind? expectedKind = null
    )
    {
        if (string.IsNullOrWhiteSpace(targetKey))
            throw new ArgumentException("Target key is required.", nameof(targetKey));

        var segments = targetKey
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Trim())
            .ToArray();

        if (segments.Length == 0)
            throw new InvalidOperationException("History target key is invalid.");

        if (segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidOperationException(
                $"History target key '{targetKey}' contains invalid segments."
            );
        }

        if (expectedKind is not null)
            EnsureExpectedShape(segments, expectedKind.Value, targetKey);

        return string.Join('/', segments);
    }

    private static void EnsureExpectedShape(
        string[] segments,
        HistoryTargetKind expectedKind,
        string targetKey
    )
    {
        var valid = expectedKind switch
        {
            HistoryTargetKind.WtfCharacter => segments.Length == 5
                && string.Equals(segments[0], "wtf", StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[1], "char", StringComparison.OrdinalIgnoreCase),
            HistoryTargetKind.WtfAccount => segments.Length == 3
                && string.Equals(segments[0], "wtf", StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[1], "account", StringComparison.OrdinalIgnoreCase),
            HistoryTargetKind.Template => segments.Length == 2
                && string.Equals(segments[0], "template", StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

        if (!valid)
        {
            throw new InvalidOperationException(
                $"History target key '{targetKey}' is invalid for '{expectedKind}'."
            );
        }
    }

    private static string EnsurePathUnderRoot(
        string rootPath,
        string candidatePath,
        string pathRole
    )
    {
        var normalizedRoot = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCandidate = Path.GetFullPath(candidatePath);
        var expectedPrefix = normalizedRoot + Path.DirectorySeparatorChar;

        if (
            !normalizedCandidate.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(
                normalizedCandidate,
                normalizedRoot,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            throw new InvalidOperationException($"Resolved {pathRole} path escaped its root.");
        }

        return normalizedCandidate;
    }

    private static string BuildRestoreTempPath(string archivePath)
    {
        var parent = Path.GetDirectoryName(archivePath);
        if (string.IsNullOrWhiteSpace(parent))
        {
            throw new InvalidOperationException(
                $"Could not resolve parent folder for archive '{archivePath}'."
            );
        }

        return Path.Combine(parent, $".restore-{Guid.NewGuid():N}");
    }

    private static (string? accountName, string? realmName, string? characterName) BuildDescriptor(
        string targetKey,
        HistoryTargetKind kind
    )
    {
        var parts = targetKey.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return kind switch
        {
            HistoryTargetKind.WtfCharacter when parts.Length >= 5 => (parts[2], parts[3], parts[4]),
            HistoryTargetKind.WtfAccount when parts.Length >= 3 => (parts[2], null, null),
            _ => (null, null, null),
        };
    }

    private static string GetTemplateId(string targetKey)
    {
        var parts = targetKey.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (
            parts.Length != 2
            || !string.Equals(parts[0], "template", StringComparison.OrdinalIgnoreCase)
        )
            throw new InvalidOperationException($"Template history key '{targetKey}' is invalid.");

        var templateId = parts[1].Trim();
        if (string.IsNullOrWhiteSpace(templateId) || templateId is "." or "..")
            throw new InvalidOperationException($"Template history key '{targetKey}' is invalid.");

        if (
            templateId.Contains(Path.DirectorySeparatorChar)
            || templateId.Contains(Path.AltDirectorySeparatorChar)
        )
            throw new InvalidOperationException($"Template history key '{targetKey}' is invalid.");

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            if (templateId.Contains(invalidChar))
                throw new InvalidOperationException(
                    $"Template history key '{targetKey}' is invalid."
                );
        }

        return templateId;
    }

    private static string BuildTemporaryFolderPath(string anchorPath, string prefix)
    {
        var parent = Path.GetDirectoryName(anchorPath);
        var basePath = !string.IsNullOrWhiteSpace(parent) ? parent : anchorPath;
        return Path.Combine(basePath, $"{prefix}{Guid.NewGuid():N}");
    }

    private IEnumerable<string> EnumerateHistoryFolders(string root)
    {
        var children = _fileSystem.GetDirectories(root);
        foreach (var child in children)
        {
            if (_fileSystem.FileExists(Path.Combine(child, IndexFileName)))
                yield return child;

            foreach (var nested in EnumerateHistoryFolders(child))
                yield return nested;
        }
    }

    private void RaiseLog(string message)
    {
        Log?.Invoke(message);
    }
}
