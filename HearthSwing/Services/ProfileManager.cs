using System.IO;
using HearthSwing.Models;

namespace HearthSwing.Services;

public sealed class ProfileManager : IProfileManager
{
    private const string WtfFolderName = "WTF";
    private const string ActiveMarker = ".active";
    private readonly ISettingsService _settings;
    private readonly IFileSystem _fs;

    public ProfileManager(ISettingsService settings, IFileSystem fileSystem)
    {
        _settings = settings;
        _fs = fileSystem;
    }

    public string GamePath => _settings.Current.GamePath;
    public string ProfilesPath => _settings.Current.ProfilesPath;

    public List<ProfileInfo> DiscoverProfiles()
    {
        var profiles = new List<ProfileInfo>();
        var activeId = ReadActiveMarker();

        if (!_fs.DirectoryExists(ProfilesPath))
            return profiles;

        foreach (var dir in _fs.GetDirectories(ProfilesPath))
        {
            var name = Path.GetFileName(dir);
            profiles.Add(
                new ProfileInfo
                {
                    Id = name,
                    FolderPath = dir,
                    IsActive = false,
                }
            );
        }

        MarkOrAddActiveProfile(profiles, activeId);

        return profiles.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public ProfileInfo? DetectCurrentProfile()
    {
        var activeId = ReadActiveMarker();
        if (string.IsNullOrEmpty(activeId))
            return null;

        return new ProfileInfo
        {
            Id = activeId,
            FolderPath = Path.Combine(ProfilesPath, activeId),
            IsActive = true,
        };
    }

    public void SwitchTo(ProfileInfo target, Action<string> log)
    {
        var currentProfile = DetectCurrentProfile();
        var wtfActive = Path.Combine(GamePath, WtfFolderName);

        if (
            currentProfile is not null
            && currentProfile.Id.Equals(target.Id, StringComparison.OrdinalIgnoreCase)
        )
        {
            log($"'{target.DisplayName}' is already active.");
            return;
        }

        var targetParked = target.FolderPath;
        if (!_fs.DirectoryExists(targetParked))
            throw new InvalidOperationException($"Target profile folder not found: {targetParked}");

        if (_fs.DirectoryExists(wtfActive))
            ParkOrBackupCurrentWtf(currentProfile, wtfActive, log);

        ActivateProfile(target, targetParked, wtfActive, currentProfile, log);

        WriteActiveMarker(target.Id);
        log($"Profile switched to '{target.DisplayName}'.");
    }

    public void SaveCurrentAsProfile(string profileId, Action<string> log)
    {
        var wtfActive = Path.Combine(GamePath, WtfFolderName);
        if (!_fs.DirectoryExists(wtfActive))
            throw new InvalidOperationException("WTF folder not found.");

        if (!_fs.DirectoryExists(ProfilesPath))
            _fs.CreateDirectory(ProfilesPath);

        var dest = Path.Combine(ProfilesPath, profileId);
        if (_fs.DirectoryExists(dest))
        {
            log($"Overwriting existing profile '{profileId}'...");
            _fs.DeleteDirectory(dest, recursive: true);
        }

        log($"Copying WTF → {profileId}/...");
        CopyDirectory(wtfActive, dest);
        WriteActiveMarker(profileId);
        log($"Profile '{profileId}' saved.");
    }

    private void MarkOrAddActiveProfile(List<ProfileInfo> profiles, string activeId)
    {
        if (string.IsNullOrEmpty(activeId))
            return;

        var existing = profiles.FirstOrDefault(p =>
            p.Id.Equals(activeId, StringComparison.OrdinalIgnoreCase)
        );

        if (existing is not null)
        {
            existing.IsActive = true;
        }
        else
        {
            // Folder is absent because it's currently the active WTF
            profiles.Add(
                new ProfileInfo
                {
                    Id = activeId,
                    FolderPath = Path.Combine(ProfilesPath, activeId),
                    IsActive = true,
                }
            );
        }
    }

    private void ParkOrBackupCurrentWtf(
        ProfileInfo? currentProfile,
        string wtfActive,
        Action<string> log
    )
    {
        if (currentProfile is not null)
            ParkCurrentProfile(currentProfile, wtfActive, log);
        else
            BackupUnknownWtf(wtfActive, log);
    }

    private void ParkCurrentProfile(
        ProfileInfo currentProfile,
        string wtfActive,
        Action<string> log
    )
    {
        var currentParked = currentProfile.FolderPath;

        if (_fs.DirectoryExists(currentParked))
            throw new InvalidOperationException(
                $"Cannot park current profile: folder already exists: {currentParked}. "
                    + "This may indicate a broken state — check your profiles folder."
            );

        log($"Parking '{currentProfile.DisplayName}': WTF → {currentProfile.Id}/");
        try
        {
            MoveDirectory(wtfActive, currentParked);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to park current profile: {ex.Message}. No changes were made.",
                ex
            );
        }
    }

    /// <summary>
    /// No active marker exists — WTF is present but we don't know which profile owns it.
    /// Back it up with a timestamped name before overwriting.
    /// </summary>
    private void BackupUnknownWtf(string wtfActive, Action<string> log)
    {
        var backupName = $"_backup_{DateTime.Now:yyyyMMdd_HHmmss}";
        var backupPath = Path.Combine(ProfilesPath, backupName);
        log($"No active profile known. Backing up current WTF → {backupName}/");
        try
        {
            if (!_fs.DirectoryExists(ProfilesPath))
                _fs.CreateDirectory(ProfilesPath);
            MoveDirectory(wtfActive, backupPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to back up current WTF: {ex.Message}. No changes were made.",
                ex
            );
        }
    }

    private void ActivateProfile(
        ProfileInfo target,
        string targetParked,
        string wtfActive,
        ProfileInfo? currentProfile,
        Action<string> log
    )
    {
        log($"Activating '{target.DisplayName}': {target.Id}/ → WTF");
        try
        {
            MoveDirectory(targetParked, wtfActive);
        }
        catch (Exception ex)
        {
            RollbackParkedProfile(currentProfile, wtfActive, log);
            throw new InvalidOperationException(
                $"Failed to activate target profile: {ex.Message}. Attempted rollback.",
                ex
            );
        }
    }

    private void RollbackParkedProfile(
        ProfileInfo? currentProfile,
        string wtfActive,
        Action<string> log
    )
    {
        if (currentProfile is null)
            return;

        log("ERROR: Rolling back...");
        try
        {
            MoveDirectory(currentProfile.FolderPath, wtfActive);
        }
        catch
        { /* best effort */
        }
    }

    private string ReadActiveMarker()
    {
        var markerPath = Path.Combine(ProfilesPath, ActiveMarker);
        if (!_fs.FileExists(markerPath))
            return string.Empty;
        try
        {
            return _fs.ReadAllText(markerPath).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private void WriteActiveMarker(string profileId)
    {
        if (!_fs.DirectoryExists(ProfilesPath))
            _fs.CreateDirectory(ProfilesPath);

        var markerPath = Path.Combine(ProfilesPath, ActiveMarker);
        _fs.WriteAllText(markerPath, profileId);
    }

    /// <summary>
    /// Same-volume uses rename for speed; cross-volume falls back to copy + delete.
    /// </summary>
    private void MoveDirectory(string source, string dest)
    {
        ClearReadOnlyAttributes(source);

        if (IsSameVolume(source, dest))
        {
            _fs.MoveDirectory(source, dest);
        }
        else
        {
            CopyDirectory(source, dest);
            _fs.DeleteDirectory(source, recursive: true);
        }
    }

    private static bool IsSameVolume(string path1, string path2)
    {
        return Path.GetPathRoot(Path.GetFullPath(path1))!
            .Equals(Path.GetPathRoot(Path.GetFullPath(path2)), StringComparison.OrdinalIgnoreCase);
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

    private void CopyDirectory(string source, string dest)
    {
        _fs.CreateDirectory(dest);
        foreach (var file in _fs.GetFiles(source, "*", SearchOption.TopDirectoryOnly))
            _fs.CopyFile(file, Path.Combine(dest, Path.GetFileName(file)));
        foreach (var dir in _fs.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }
}
