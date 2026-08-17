namespace HearthSwing.Models.Templates;

public sealed record TemplateRestoreOptions
{
    public TemplateApplyScope Scope { get; init; } = TemplateApplyScope.Full;

    public bool IncludeAccountScoped { get; init; } = true;
}
