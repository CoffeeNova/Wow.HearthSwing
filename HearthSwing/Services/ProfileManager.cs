using System.IO;
using System.Text.Json;
using HearthSwing.Models;
using HearthSwing.Models.Profiles;
using Microsoft.Extensions.Logging;

namespace HearthSwing.Services;

public sealed class ProfileManager : IProfileManager
{
    private const string WtfFolderName = "WTF";
    private const string ActiveMarker = ".active";
    private const string RollbackFolderPrefix = ".rollback-";
    private static readonly JsonSerializerOptions ActiveProfileJsonOptions = new()
    {
        WriteIndented = true,
    };
    private readonly ISettingsService _settings;
    private readonly IFileSystem _fs;
    private readonly ILogger<ProfileManager> _logger;

    public ProfileManager(
        ISettingsService settings,
        IFileSystem fileSystem,
        ILogger<ProfileManager> logger
    )
    {
        _settings = settings;
        _fs = fileSystem;
        _logger = logger;
    }

    public string GamePath => _settings.Current.GamePath;
    public string ProfilesPath => _settings.Current.ProfilesPath;
    public string WtfPath => Path.Combine(GamePath, WtfFolderName);

    public List<ProfileInfo> DiscoverProfiles()
    {
        var profiles = new List<ProfileInfo>();
        var activeId = ReadActiveState()?.Id ?? string.Empty;

        if (!_fs.DirectoryExists(ProfilesPath))
            return profiles;

        foreach (var dir in _fs.GetDirectories(ProfilesPath))
        {
            var name = Path.GetFileName(dir);
            // Skip hidden/internal folders like .versions
            if (name.StartsWith('.'))
                continue;

            profiles.Add(
                new ProfileInfo
                {
                    Id = name,
                    FolderPath = dir,
                    IsActive = false,
                }
            );
        }

        MarkActiveProfile(profiles, activeId);

        return profiles.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public ProfileInfo? DetectCurrentProfile()
    {
        var activeState = ReadActiveState();
        if (activeState is null)
            return null;

        return new ProfileInfo
        {
            Id = activeState.Id,
            FolderPath = activeState.SnapshotPath ?? Path.Combine(ProfilesPath, activeState.Id),
            IsActive = true,
        };
    }

    public void SwitchTo(ProfileInfo target)
    {
        var currentProfile = DetectCurrentProfile();

        if (
            currentProfile is not null
            && currentProfile.Id.Equals(target.Id, StringComparison.OrdinalIgnoreCase)
        )
        {
            _logger.LogInformation("'{DisplayName}' is already active.", target.DisplayName);
            return;
        }

        var targetParked = target.FolderPath;
        if (!_fs.DirectoryExists(targetParked))
            throw new InvalidOperationException($"Target profile folder not found: {targetParked}");

        _logger.LogInformation(
            "Activating '{DisplayName}': {ProfileId}/ → WTF",
            target.DisplayName,
            target.Id
        );
        ReplaceDirectoryWithRollback(targetParked, WtfPath, "switch profiles");

        WriteActiveMarker(target.Id);
        _logger.LogInformation("Profile switched to '{DisplayName}'.", target.DisplayName);
    }

    public void SwitchTo(ProfileDescriptor descriptor)
    {
        descriptor.Validate();

        switch (descriptor.Granularity)
        {
            case ProfileGranularity.FullWtf:
                ApplyFullWtfSnapshot(descriptor);
                break;
            case ProfileGranularity.PerAccount:
                ApplyAccountSnapshot(descriptor);
                break;
            case ProfileGranularity.PerCharacter:
                ApplyCharacterSnapshot(descriptor);
                break;
        }

        WriteActiveMarker(descriptor.ToActiveProfileState());
        _logger.LogInformation(
            "Profile '{DisplayName}' ({Granularity}) applied.",
            descriptor.EffectiveDisplayName,
            descriptor.Granularity
        );
    }

    public void SaveCurrentAsProfile(string profileId)
    {
        if (!_fs.DirectoryExists(WtfPath))
            throw new InvalidOperationException("WTF folder not found.");

        if (!_fs.DirectoryExists(ProfilesPath))
            _fs.CreateDirectory(ProfilesPath);

        var dest = Path.Combine(ProfilesPath, profileId);
        if (_fs.DirectoryExists(dest))
            _logger.LogInformation("Overwriting existing profile '{ProfileId}'...", profileId);

        _logger.LogInformation("Copying WTF → {ProfileId}/...", profileId);
        ReplaceDirectoryWithRollback(WtfPath, dest, "save profile");
        WriteActiveMarker(profileId);
        _logger.LogInformation("Profile '{ProfileId}' saved.", profileId);
    }

    public void SaveCurrentAsProfile(ProfileDescriptor descriptor)
    {
        descriptor.Validate();

        if (!_fs.DirectoryExists(WtfPath))
            throw new InvalidOperationException("WTF folder not found.");

        switch (descriptor.Granularity)
        {
            case ProfileGranularity.FullWtf:
                CaptureFullWtfSnapshot(descriptor);
                break;
            case ProfileGranularity.PerAccount:
                CaptureAccountSnapshot(descriptor);
                break;
            case ProfileGranularity.PerCharacter:
                CaptureCharacterSnapshot(descriptor);
                break;
        }

        WriteActiveMarker(descriptor.ToActiveProfileState());
        _logger.LogInformation(
            "Profile '{DisplayName}' ({Granularity}) saved.",
            descriptor.EffectiveDisplayName,
            descriptor.Granularity
        );
    }

    public void RestoreActiveProfile()
    {
        var activeState = ReadActiveState();
        if (activeState is null)
            throw new InvalidOperationException("No active profile to restore.");

        var activeId = activeState.Id;
        var profilePath = Path.Combine(ProfilesPath, activeId);
        if (!_fs.DirectoryExists(profilePath))
            throw new InvalidOperationException(
                $"Saved profile '{activeId}' not found. Save the profile first."
            );

        _logger.LogInformation("Restoring profile '{ProfileId}' from saved snapshot...", activeId);
        ReplaceDirectoryWithRollback(profilePath, WtfPath, "restore active profile");
        _logger.LogInformation("Profile '{ProfileId}' restored from saved snapshot.", activeId);
    }

    private void ApplyFullWtfSnapshot(ProfileDescriptor descriptor)
    {
        if (!_fs.DirectoryExists(descriptor.SnapshotPath))
            throw new InvalidOperationException(
                $"Profile snapshot folder not found: {descriptor.SnapshotPath}"
            );

        _logger.LogInformation(
            "Applying full WTF snapshot from '{SnapshotPath}'.",
            descriptor.SnapshotPath
        );
        ReplaceDirectoryWithRollback(descriptor.SnapshotPath, WtfPath, "apply full WTF snapshot");
    }

    private void ApplyAccountSnapshot(ProfileDescriptor descriptor)
    {
        var snapshotAccountPath = Path.Combine(
            descriptor.SnapshotPath,
            "Account",
            descriptor.AccountName!
        );

        if (!_fs.DirectoryExists(snapshotAccountPath))
            throw new InvalidOperationException(
                $"Account snapshot folder not found: {snapshotAccountPath}"
            );

        var liveAccountPath = Path.Combine(WtfPath, "Account", descriptor.AccountName!);

        _logger.LogInformation(
            "Applying account '{AccountName}' snapshot from '{SnapshotPath}'.",
            descriptor.AccountName,
            descriptor.SnapshotPath
        );
        ReplaceDirectoryWithRollback(snapshotAccountPath, liveAccountPath, "apply account snapshot");
    }

    private void ApplyCharacterSnapshot(ProfileDescriptor descriptor)
    {
        var snapshotCharPath = Path.Combine(
            descriptor.SnapshotPath,
            "Account",
            descriptor.AccountName!,
            descriptor.RealmName!,
            descriptor.CharacterName!
        );

        if (!_fs.DirectoryExists(snapshotCharPath))
            throw new InvalidOperationException(
                $"Character snapshot folder not found: {snapshotCharPath}"
            );

        var liveCharPath = Path.Combine(
            WtfPath,
            "Account",
            descriptor.AccountName!,
            descriptor.RealmName!,
            descriptor.CharacterName!
        );

        _logger.LogInformation(
            "Applying character '{CharacterName}' snapshot from '{SnapshotPath}'.",
            descriptor.CharacterName,
            descriptor.SnapshotPath
        );
        ReplaceDirectoryWithRollback(snapshotCharPath, liveCharPath, "apply character snapshot");
        RestoreAccountLevelFiles(descriptor);
    }

    private void RestoreAccountLevelFiles(ProfileDescriptor descriptor)
    {
        var snapshotAccountPath = Path.Combine(
            descriptor.SnapshotPath,
            "Account",
            descriptor.AccountName!
        );

        if (!_fs.DirectoryExists(snapshotAccountPath))
            return;

        var liveAccountPath = Path.Combine(WtfPath, "Account", descriptor.AccountName!);
        if (!_fs.DirectoryExists(liveAccountPath))
            _fs.CreateDirectory(liveAccountPath);

        foreach (var srcFile in _fs.GetFiles(snapshotAccountPath, "*", SearchOption.TopDirectoryOnly))
        {
            var destFile = Path.Combine(liveAccountPath, Path.GetFileName(srcFile));
            _fs.CopyFile(srcFile, destFile);
        }
    }

    private void CaptureFullWtfSnapshot(ProfileDescriptor descriptor)
    {
        _logger.LogInformation(
            "Capturing full WTF snapshot to '{SnapshotPath}'.",
            descriptor.SnapshotPath
        );
        ReplaceDirectoryWithRollback(WtfPath, descriptor.SnapshotPath, "capture full WTF snapshot");
    }

    private void CaptureAccountSnapshot(ProfileDescriptor descriptor)
    {
        var liveAccountPath = Path.Combine(WtfPath, "Account", descriptor.AccountName!);
        if (!_fs.DirectoryExists(liveAccountPath))
            throw new InvalidOperationException(
                $"Account folder not found in WTF: {liveAccountPath}"
            );

        var snapshotAccountPath = Path.Combine(
            descriptor.SnapshotPath,
            "Account",
            descriptor.AccountName!
        );

        _logger.LogInformation(
            "Capturing account '{AccountName}' to '{SnapshotPath}'.",
            descriptor.AccountName,
            descriptor.SnapshotPath
        );
        ReplaceDirectoryWithRollback(
            liveAccountPath,
            snapshotAccountPath,
            "capture account snapshot"
        );
    }

    private void CaptureCharacterSnapshot(ProfileDescriptor descriptor)
    {
        var liveCharPath = Path.Combine(
            WtfPath,
            "Account",
            descriptor.AccountName!,
            descriptor.RealmName!,
            descriptor.CharacterName!
        );

        if (!_fs.DirectoryExists(liveCharPath))
            throw new InvalidOperationException(
                $"Character folder not found in WTF: {liveCharPath}"
            );

        var snapshotCharPath = Path.Combine(
            descriptor.SnapshotPath,
            "Account",
            descriptor.AccountName!,
            descriptor.RealmName!,
            descriptor.CharacterName!
        );

        _logger.LogInformation(
            "Capturing character '{CharacterName}' to '{SnapshotPath}'.",
            descriptor.CharacterName,
            descriptor.SnapshotPath
        );
        ReplaceDirectoryWithRollback(liveCharPath, snapshotCharPath, "capture character snapshot");
        CaptureAccountLevelFiles(descriptor);
    }

    private void CaptureAccountLevelFiles(ProfileDescriptor descriptor)
    {
        var liveAccountPath = Path.Combine(WtfPath, "Account", descriptor.AccountName!);
        if (!_fs.DirectoryExists(liveAccountPath))
            return;

        var snapshotAccountPath = Path.Combine(
            descriptor.SnapshotPath,
            "Account",
            descriptor.AccountName!
        );

        if (!_fs.DirectoryExists(snapshotAccountPath))
            _fs.CreateDirectory(snapshotAccountPath);

        foreach (var srcFile in _fs.GetFiles(liveAccountPath, "*", SearchOption.TopDirectoryOnly))
        {
            var destFile = Path.Combine(snapshotAccountPath, Path.GetFileName(srcFile));
            _fs.CopyFile(srcFile, destFile);
        }
    }

    private void MarkActiveProfile(List<ProfileInfo> profiles, string activeId)    {
        if (string.IsNullOrEmpty(activeId))
            return;

        var existing = profiles.FirstOrDefault(p =>
            p.Id.Equals(activeId, StringComparison.OrdinalIgnoreCase)
        );

        if (existing is not null)
            existing.IsActive = true;
    }

    private ActiveProfileState? ReadActiveState()
    {
        var markerPath = Path.Combine(ProfilesPath, ActiveMarker);
        if (!_fs.FileExists(markerPath))
            return null;

        try
        {
            var content = _fs.ReadAllText(markerPath).Trim();
            if (string.IsNullOrWhiteSpace(content))
                return null;

            if (!content.StartsWith('{'))
                return ActiveProfileState.CreateLegacyFullWtf(
                    content,
                    Path.Combine(ProfilesPath, content)
                );

            var activeState = JsonSerializer.Deserialize<ActiveProfileState>(
                content,
                ActiveProfileJsonOptions
            );

            if (activeState is null || string.IsNullOrWhiteSpace(activeState.Id))
                return null;

            return activeState;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Could not parse active profile marker at {MarkerPath}.", markerPath);
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not read active profile marker at {MarkerPath}.", markerPath);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied reading active profile marker at {MarkerPath}.", markerPath);
            return null;
        }
    }

    private void WriteActiveMarker(string profileId)
    {
        WriteActiveMarker(
            ActiveProfileState.CreateLegacyFullWtf(profileId, Path.Combine(ProfilesPath, profileId))
        );
    }

    private void WriteActiveMarker(ActiveProfileState activeState)
    {
        if (!_fs.DirectoryExists(ProfilesPath))
            _fs.CreateDirectory(ProfilesPath);

        var markerPath = Path.Combine(ProfilesPath, ActiveMarker);
        var json = JsonSerializer.Serialize(activeState, ActiveProfileJsonOptions);
        _fs.WriteAllText(markerPath, json);
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

    private void ReplaceDirectoryWithRollback(string source, string destination, string operation)
    {
        var rollbackPath = string.Empty;
        var rollbackRequired = false;

        try
        {
            if (_fs.DirectoryExists(destination))
            {
                rollbackPath = CreateRollbackPath(destination);
                _logger.LogInformation(
                    "Creating rollback snapshot for {Destination} at {RollbackPath}.",
                    destination,
                    rollbackPath
                );
                CopyDirectory(destination, rollbackPath);

                rollbackRequired = true;
                _logger.LogInformation("Removing current {DirectoryName}...", Path.GetFileName(destination));
                ClearReadOnlyAttributes(destination);
                _fs.DeleteDirectory(destination, recursive: true);
            }

            CopyDirectory(source, destination);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to {Operation}.", operation);

            if (!rollbackRequired || string.IsNullOrEmpty(rollbackPath) || !_fs.DirectoryExists(rollbackPath))
                throw;

            try
            {
                RestoreRollback(destination, rollbackPath, operation);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(rollbackEx, "Rollback failed after '{Operation}'.", operation);
                throw new InvalidOperationException(
                    $"Failed to {operation} and rollback failed.",
                    new AggregateException(ex, rollbackEx)
                );
            }

            throw;
        }
        finally
        {
            CleanupTemporaryDirectory(rollbackPath);
        }
    }

    private string CreateRollbackPath(string destination)
    {
        var parentDirectory = Path.GetDirectoryName(destination);
        if (string.IsNullOrEmpty(parentDirectory))
            throw new InvalidOperationException($"Could not create rollback path for '{destination}'.");

        var directoryName = Path.GetFileName(destination);
        return Path.Combine(
            parentDirectory,
            $"{RollbackFolderPrefix}{directoryName}-{Guid.NewGuid():N}"
        );
    }

    private void RestoreRollback(string destination, string rollbackPath, string operation)
    {
        _logger.LogWarning(
            "Restoring {Destination} from rollback snapshot after '{Operation}' failed.",
            destination,
            operation
        );

        if (_fs.DirectoryExists(destination))
        {
            ClearReadOnlyAttributes(destination);
            _fs.DeleteDirectory(destination, recursive: true);
        }

        CopyDirectory(rollbackPath, destination);
        _logger.LogWarning("Rollback completed for {Destination}.", destination);
    }

    private void CleanupTemporaryDirectory(string path)
    {
        if (string.IsNullOrEmpty(path) || !_fs.DirectoryExists(path))
            return;

        try
        {
            ClearReadOnlyAttributes(path);
            _fs.DeleteDirectory(path, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not delete temporary rollback directory {RollbackPath}.",
                path
            );
        }
    }

    private void CopyDirectory(string source, string dest)
    {
        _fs.CreateDirectory(dest);
        foreach (var file in _fs.GetFiles(source, "*", SearchOption.TopDirectoryOnly))
            _fs.CopyFile(file, Path.Combine(dest, Path.GetFileName(file)));
        foreach (var dir in _fs.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }
}
