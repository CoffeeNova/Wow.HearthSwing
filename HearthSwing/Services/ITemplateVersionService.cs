using HearthSwing.Models;

namespace HearthSwing.Services;

/// <summary>
/// Versions character templates as <c>.tar.gz</c> archives under
/// <c>&lt;ProfilesPath&gt;/.template-versions/&lt;templateId&gt;</c>.
/// </summary>
public interface ITemplateVersionService
{
    Task CreateVersionAsync(string templateId);

    List<ProfileVersion> GetVersions(string templateId);

    Task RestoreVersionAsync(ProfileVersion version);

    void DeleteVersion(ProfileVersion version);

    void PruneVersions(string templateId, int maxVersions);
}
