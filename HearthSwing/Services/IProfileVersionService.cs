using HearthSwing.Models;
using HearthSwing.Models.Profiles;

namespace HearthSwing.Services;

public interface IProfileVersionService
{
    Task CreateVersionAsync(string profileId);
    Task CreateVersionAsync(ProfileDescriptor descriptor);
    List<ProfileVersion> GetVersions(string profileId);
    List<ProfileVersion> GetVersions(ProfileDescriptor descriptor);
    Task RestoreVersionAsync(ProfileVersion version);
    void DeleteVersion(ProfileVersion version);
    void PruneVersions(string profileId, int maxVersions);
}
