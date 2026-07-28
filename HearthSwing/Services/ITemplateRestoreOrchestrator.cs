using HearthSwing.Models.Templates;
using HearthSwing.Models.WoW;

namespace HearthSwing.Services;

public interface ITemplateRestoreOrchestrator
{
    event Action<string>? Log;

    Task RestoreCharacterTemplateAsync(
        TemplateSummary template,
        WowCharacter target,
        TemplateRestoreOptions options,
        CancellationToken ct = default
    );

    Task RestoreAccountTemplateAsync(
        TemplateSummary template,
        WowAccount target,
        TemplateRestoreOptions options,
        CancellationToken ct = default
    );
}
