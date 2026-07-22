namespace HearthSwing.Models.Templates;

/// <summary>
/// Persisted description of a template (stored as template.json). A template is either an account
/// template (shared account settings) or a character template (a single depersonalized character).
/// </summary>
public sealed class TemplateMetadata
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required TemplateKind Kind { get; init; }

    public required string SourceAccountName { get; init; }

    /// <summary>Realm the donor character belonged to. Null for account templates.</summary>
    public string? SourceRealmName { get; init; }

    /// <summary>Donor character name. Null for account templates.</summary>
    public string? SourceCharacterName { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? LastUpdatedUtc { get; init; }

    /// <summary>
    /// Token-format schema version, reserved for future migrations of the token layout.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;
}
