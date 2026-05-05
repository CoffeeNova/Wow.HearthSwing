using System.IO;
using HearthSwing.Models;
using Microsoft.Extensions.Logging;

namespace HearthSwing.Services;

public sealed class ProfileManager : IProfileManager
{
    private const string WtfFolderName = "WTF";
    private const string ActiveMarker = ".active";
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

    public List<ProfileInfo> DiscoverProfiles()
    {
        var profiles = new List<ProfileInfo>();
        var activeId = ReadActiveMarker();

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

    public void SwitchTo(ProfileInfo target)
    {
        var currentProfile = DetectCurrentProfile();
        var wtfActive = Path.Combine(GamePath, WtfFolderName);

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

        if (_fs.DirectoryExists(wtfActive))
        {
            _logger.LogInformation("Removing current WTF...");
            ClearReadOnlyAttributes(wtfActive);
            _fs.DeleteDirectory(wtfActive, recursive: true);
        }

        _logger.LogInformation(
            "Activating '{DisplayName}': {ProfileId}/ → WTF",
            target.DisplayName,
            target.Id
        );
        CopyDirectory(targetParked, wtfActive);

        WriteActiveMarker(target.Id);
        _logger.LogInformation("Profile switched to '{DisplayName}'.", target.DisplayName);
    }

    public void SaveCurrentAsProfile(string profileId)
    {
        var wtfActive = Path.Combine(GamePath, WtfFolderName);
        if (!_fs.DirectoryExists(wtfActive))
            throw new InvalidOperationException("WTF folder not found.");

        if (!_fs.DirectoryExists(ProfilesPath))
            _fs.CreateDirectory(ProfilesPath);

        var dest = Path.Combine(ProfilesPath, profileId);
        if (_fs.DirectoryExists(dest))
        {
            _logger.LogInformation("Overwriting existing profile '{ProfileId}'...", profileId);
            ClearReadOnlyAttributes(dest);
            _fs.DeleteDirectory(dest, recursive: true);
        }

        _logger.LogInformation("Copying WTF → {ProfileId}/...", profileId);
        CopyDirectory(wtfActive, dest);
        WriteActiveMarker(profileId);
        _logger.LogInformation("Profile '{ProfileId}' saved.", profileId);
    }

    public void RestoreActiveProfile()
    {
        var activeId = ReadActiveMarker();
        if (string.IsNullOrEmpty(activeId))
            throw new InvalidOperationException("No active profile to restore.");

        var profilePath = Path.Combine(ProfilesPath, activeId);
        if (!_fs.DirectoryExists(profilePath))
            throw new InvalidOperationException(
                $"Saved profile '{activeId}' not found. Save the profile first."
            );

        var wtfPath = Path.Combine(GamePath, WtfFolderName);

        _logger.LogInformation("Restoring profile '{ProfileId}' from saved snapshot...", activeId);
        if (_fs.DirectoryExists(wtfPath))
        {
            ClearReadOnlyAttributes(wtfPath);
            _fs.DeleteDirectory(wtfPath, recursive: true);
        }

        CopyDirectory(profilePath, wtfPath);
        _logger.LogInformation("Profile '{ProfileId}' restored from saved snapshot.", activeId);
    }

    private void MarkActiveProfile(List<ProfileInfo> profiles, string activeId)
    {
        if (string.IsNullOrEmpty(activeId))
            return;

        var existing = profiles.FirstOrDefault(p =>
            p.Id.Equals(activeId, StringComparison.OrdinalIgnoreCase)
        );

        if (existing is not null)
            existing.IsActive = true;
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
