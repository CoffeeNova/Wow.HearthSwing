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
        {
            log($"Removing current WTF...");
            ClearReadOnlyAttributes(wtfActive);
            _fs.DeleteDirectory(wtfActive, recursive: true);
        }

        log($"Activating '{target.DisplayName}': {target.Id}/ → WTF");
        CopyDirectory(targetParked, wtfActive);

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
            ClearReadOnlyAttributes(dest);
            _fs.DeleteDirectory(dest, recursive: true);
        }

        log($"Copying WTF → {profileId}/...");
        CopyDirectory(wtfActive, dest);
        WriteActiveMarker(profileId);
        log($"Profile '{profileId}' saved.");
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
