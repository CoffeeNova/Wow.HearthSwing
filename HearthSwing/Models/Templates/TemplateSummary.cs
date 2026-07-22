namespace HearthSwing.Models.Templates;

/// <summary>
/// In-memory projection of a stored template used for listing and applying. Not persisted.
/// </summary>
public sealed class TemplateSummary
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string RootPath { get; init; }

    public required string SourceAccountName { get; init; }

    public required string SourceRealmName { get; init; }

    public required string SourceCharacterName { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? LastUpdatedUtc { get; init; }

    public string DisplayName => Name;
}
