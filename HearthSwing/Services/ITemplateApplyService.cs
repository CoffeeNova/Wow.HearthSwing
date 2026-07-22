using HearthSwing.Models.Templates;
using HearthSwing.Models.WoW;

namespace HearthSwing.Services;

/// <summary>
/// Applies a stored template onto a live target character, re-personalizing tokenized content with
/// the target character and realm names.
/// </summary>
public interface ITemplateApplyService
{
    void ApplyTemplate(TemplateSummary template, WowCharacter target, TemplateApplyOptions options);
}
