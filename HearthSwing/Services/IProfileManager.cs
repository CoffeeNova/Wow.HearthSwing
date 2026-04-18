using HearthSwing.Models;

namespace HearthSwing.Services;

public interface IProfileManager
{
    string GamePath { get; }
    string ProfilesPath { get; }
    List<ProfileInfo> DiscoverProfiles();
    ProfileInfo? DetectCurrentProfile();
    void SwitchTo(ProfileInfo target, Action<string> log);
    void SaveCurrentAsProfile(string profileId, Action<string> log);
    void RestoreActiveProfile(Action<string> log);
}
