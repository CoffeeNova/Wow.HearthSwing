namespace HearthSwing.Models.Templates;

/// <summary>
/// Persisted description of a depersonalized character template (stored as template.json).
/// </summary>
public sealed class TemplateMetadata
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string SourceAccountName { get; init; }

    public required string SourceRealmName { get; init; }

    public required string SourceCharacterName { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? LastUpdatedUtc { get; init; }

    /// <summary>
    /// Token-format schema version, reserved for future migrations of the token layout.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;
}
