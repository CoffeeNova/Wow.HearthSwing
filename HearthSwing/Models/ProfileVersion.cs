using HearthSwing.Models.Profiles;

namespace HearthSwing.Models;

public sealed class ProfileVersion
{
    public required string VersionId { get; init; }

    public required string ProfileId { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required string ArchivePath { get; init; }

    // Scope metadata — null for legacy FullWtf versions
    public string? LocalProfileId { get; init; }
    public ProfileGranularity Granularity { get; init; } = ProfileGranularity.FullWtf;
    public string? AccountName { get; init; }
    public string? RealmName { get; init; }
    public string? CharacterName { get; init; }

    public string DisplayName => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
}
