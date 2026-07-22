namespace HearthSwing.Models.Templates;

/// <summary>
/// In-memory projection of a stored template used for listing and applying. Not persisted.
/// </summary>
public sealed class TemplateSummary
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required TemplateKind Kind { get; init; }

    public required string RootPath { get; init; }

    public required string SourceAccountName { get; init; }

    public string? SourceRealmName { get; init; }

    public string? SourceCharacterName { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? LastUpdatedUtc { get; init; }

    public string DisplayName => Name;

    public string KindLabel => Kind == TemplateKind.Account ? "Account" : "Character";

    public string SourceDescription =>
        Kind == TemplateKind.Account
            ? SourceAccountName
            : $"{SourceCharacterName} - {SourceRealmName}";
}
