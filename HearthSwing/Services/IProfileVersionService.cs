using HearthSwing.Models;

namespace HearthSwing.Services;

public interface IProfileVersionService
{
    Task CreateVersionAsync(string profileId);
    List<ProfileVersion> GetVersions(string profileId);
    Task RestoreVersionAsync(ProfileVersion version);
    void DeleteVersion(ProfileVersion version);
    void PruneVersions(string profileId, int maxVersions);
}
