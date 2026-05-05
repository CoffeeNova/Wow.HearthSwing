using HearthSwing.Models;

namespace HearthSwing.Services;

public interface IProfileManager
{
    string GamePath { get; }
    string ProfilesPath { get; }
    List<ProfileInfo> DiscoverProfiles();
    ProfileInfo? DetectCurrentProfile();
    void SwitchTo(ProfileInfo target);
    void SaveCurrentAsProfile(string profileId);
    void RestoreActiveProfile();
}
