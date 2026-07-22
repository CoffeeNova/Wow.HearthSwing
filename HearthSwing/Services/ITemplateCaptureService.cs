using HearthSwing.Models.Templates;
using HearthSwing.Models.WoW;

namespace HearthSwing.Services;

/// <summary>
/// Captures a depersonalized template from a live donor character.
/// </summary>
public interface ITemplateCaptureService
{
    TemplateSummary CreateTemplate(WowCharacter source, string templateName);
}
