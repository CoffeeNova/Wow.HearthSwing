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

    /// <summary>Re-captures an existing account template's content from a donor account, in place.</summary>
    TemplateSummary UpdateAccountTemplate(TemplateSummary template, WowAccount source);

    /// <summary>Re-captures an existing character template's content from a donor character, in place.</summary>
    TemplateSummary UpdateCharacterTemplate(TemplateSummary template, WowCharacter source);
}
