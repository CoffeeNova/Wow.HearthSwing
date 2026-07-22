using HearthSwing.Models.Templates;
using HearthSwing.Models.WoW;

namespace HearthSwing.Services;

/// <summary>
/// Applies stored templates onto live targets: account templates overlay a target account's shared
/// settings; character templates re-personalize tokenized content with the target's character/realm.
/// </summary>
public interface ITemplateApplyService
{
    void ApplyAccountTemplate(TemplateSummary template, WowAccount target);

    void ApplyCharacterTemplate(TemplateSummary template, WowCharacter target);
}
