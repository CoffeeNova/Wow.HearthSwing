using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HearthSwing.Models;

namespace HearthSwing.Services;

public sealed class ProfileManager
{
    private const string WtfFolderName = "WTF";
    private const string ActiveMarker = ".active";
    private readonly SettingsService _settings;

    public ProfileManager(SettingsService settings)
    {
        _settings = settings;
    }

    public string GamePath => _settings.Current.GamePath;
    public string ProfilesPath => _settings.Current.ProfilesPath;

    /// <summary>
    /// Scans the ProfilesPath directory for subfolders. Each subfolder is a profile.
    /// The currently active profile is the one whose folder is absent (moved to WTF).
    /// </summary>
    public List<ProfileInfo> DiscoverProfiles()
    {
        var profiles = new List<ProfileInfo>();
        var activeId = ReadActiveMarker();

        if (!Directory.Exists(ProfilesPath))
            return profiles;

        foreach (var dir in Directory.GetDirectories(ProfilesPath))
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

        // If we have an active marker and that profile folder is absent, add it
        if (!string.IsNullOrEmpty(activeId))
        {
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

        return profiles.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Returns the currently active profile (the one whose data is in WTF right now).
    /// </summary>
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

    /// <summary>
    /// Switches the active profile:
    /// 1. Move WTF → ProfilesPath/{current}  (park the current profile)
    /// 2. Move ProfilesPath/{target} → WTF   (activate the target)
    /// 3. Update active marker
    /// </summary>
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
        if (!Directory.Exists(targetParked))
            throw new InvalidOperationException($"Target profile folder not found: {targetParked}");

        // Park current WTF if it exists
        if (Directory.Exists(wtfActive))
        {
            if (currentProfile is not null)
            {
                var currentParked = currentProfile.FolderPath;

                if (Directory.Exists(currentParked))
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
            else
            {
                // No active marker — WTF exists but we don't know whose it is.
                // Auto-save it as a backup before overwriting.
                var backupName = $"_backup_{DateTime.Now:yyyyMMdd_HHmmss}";
                var backupPath = Path.Combine(ProfilesPath, backupName);
                log($"No active profile known. Backing up current WTF → {backupName}/");
                try
                {
                    if (!Directory.Exists(ProfilesPath))
                        Directory.CreateDirectory(ProfilesPath);
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
        }

        // Activate target
        log($"Activating '{target.DisplayName}': {target.Id}/ → WTF");
        try
        {
            MoveDirectory(targetParked, wtfActive);
        }
        catch (Exception ex)
        {
            // Rollback: try to restore parked profile
            if (currentProfile is not null)
            {
                log("ERROR: Rolling back...");
                try
                {
                    MoveDirectory(currentProfile.FolderPath, wtfActive);
                }
                catch
                { /* best effort */
                }
            }
            throw new InvalidOperationException(
                $"Failed to activate target profile: {ex.Message}. Attempted rollback.",
                ex
            );
        }

        WriteActiveMarker(target.Id);
        log($"Profile switched to '{target.DisplayName}'.");
    }

    /// <summary>
    /// Saves the current WTF folder as a new profile (or overwrites an existing one).
    /// </summary>
    public void SaveCurrentAsProfile(string profileId, Action<string> log)
    {
        var wtfActive = Path.Combine(GamePath, WtfFolderName);
        if (!Directory.Exists(wtfActive))
            throw new InvalidOperationException("WTF folder not found.");

        if (!Directory.Exists(ProfilesPath))
            Directory.CreateDirectory(ProfilesPath);

        var dest = Path.Combine(ProfilesPath, profileId);
        if (Directory.Exists(dest))
        {
            log($"Overwriting existing profile '{profileId}'...");
            Directory.Delete(dest, recursive: true);
        }

        log($"Copying WTF → {profileId}/...");
        CopyDirectory(wtfActive, dest);
        WriteActiveMarker(profileId);
        log($"Profile '{profileId}' saved.");
    }

    private string ReadActiveMarker()
    {
        var markerPath = Path.Combine(ProfilesPath, ActiveMarker);
        if (!File.Exists(markerPath))
            return string.Empty;
        try
        {
            return File.ReadAllText(markerPath).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private void WriteActiveMarker(string profileId)
    {
        if (!Directory.Exists(ProfilesPath))
            Directory.CreateDirectory(ProfilesPath);

        var markerPath = Path.Combine(ProfilesPath, ActiveMarker);
        File.WriteAllText(markerPath, profileId);
    }

    /// <summary>
    /// Moves a directory. Clears read-only attributes first, then uses rename if same
    /// volume, otherwise falls back to copy + delete.
    /// </summary>
    private static void MoveDirectory(string source, string dest)
    {
        ClearReadOnlyAttributes(source);

        if (
            Path.GetPathRoot(Path.GetFullPath(source))!
                .Equals(
                    Path.GetPathRoot(Path.GetFullPath(dest)),
                    StringComparison.OrdinalIgnoreCase
                )
        )
        {
            Directory.Move(source, dest);
        }
        else
        {
            CopyDirectory(source, dest);
            Directory.Delete(source, recursive: true);
        }
    }

    private static void ClearReadOnlyAttributes(string directory)
    {
        if (!Directory.Exists(directory))
            return;
        foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            var attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
    }
}
