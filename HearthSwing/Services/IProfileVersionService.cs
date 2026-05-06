using HearthSwing.Models;

namespace HearthSwing.Services;

public interface IProfileVersionService
{
    Task CreateVersionAsync(string savedAccountId);
    List<ProfileVersion> GetVersions(string savedAccountId);
    Task RestoreVersionAsync(ProfileVersion version);
    void DeleteVersion(ProfileVersion version);
    void PruneVersions(string savedAccountId, int maxVersions);
}
