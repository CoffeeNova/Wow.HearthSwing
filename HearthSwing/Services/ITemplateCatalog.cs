using HearthSwing.Models.Templates;

namespace HearthSwing.Services;

/// <summary>
/// Persists and resolves character-template metadata under <c>&lt;ProfilesPath&gt;/.templates</c>.
/// </summary>
public interface ITemplateCatalog
{
    List<TemplateSummary> DiscoverTemplates();

    TemplateSummary? GetById(string templateId);

    TemplateSummary Create(
        string name,
        string sourceAccountName,
        string sourceRealmName,
        string sourceCharacterName
    );

    void UpdateLastUpdated(string templateId, DateTimeOffset updatedAtUtc);

    void Rename(string templateId, string newName);

    void Delete(string templateId);
}
