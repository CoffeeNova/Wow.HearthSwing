using HearthSwing.Models;
using HearthSwing.Models.Profiles;

namespace HearthSwing.Services;

public interface IProfileManager
{
    string GamePath { get; }
    string ProfilesPath { get; }
    string WtfPath { get; }
    List<ProfileInfo> DiscoverProfiles();
    ProfileInfo? DetectCurrentProfile();
    void SwitchTo(ProfileInfo target);
    void SwitchTo(ProfileDescriptor descriptor);
    void SaveCurrentAsProfile(string profileId);
    void SaveCurrentAsProfile(ProfileDescriptor descriptor);
    void RestoreActiveProfile();
}
