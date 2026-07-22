namespace HearthSwing.Models.Templates;

/// <summary>
/// Options controlling how a template is applied onto a target character.
/// </summary>
public sealed class TemplateApplyOptions
{
    /// <summary>
    /// Whether to also apply account-level settings. Off by default because account-level
    /// SavedVariables are shared by every character on the target account and would be overwritten.
    /// </summary>
    public bool IncludeAccountSettings { get; init; }

    /// <summary>
    /// Whether to snapshot the target account into a version before applying the template.
    /// </summary>
    public bool CreateVersionBeforeApply { get; init; } = true;
}
