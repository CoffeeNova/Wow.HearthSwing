using HearthSwing.Models.Templates;
using HearthSwing.Models.WoW;

namespace HearthSwing.Services;

/// <summary>
/// Captures templates from a live donor: an account's shared settings, or a depersonalized character.
/// </summary>
public interface ITemplateCaptureService
{
    TemplateSummary CreateAccountTemplate(WowAccount source, string templateName);

    TemplateSummary CreateCharacterTemplate(WowCharacter source, string templateName);
}
