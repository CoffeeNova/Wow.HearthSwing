namespace HearthSwing.Models.Profiles;

public sealed record ActiveProfileState
{
    public required string Id { get; init; }
    public string? LocalProfileId { get; init; }
    public ProfileGranularity Granularity { get; init; } = ProfileGranularity.FullWtf;
    public string? AccountName { get; init; }
    public string? RealmName { get; init; }
    public string? CharacterName { get; init; }
    public string? SnapshotPath { get; init; }
    public string? VersionId { get; init; }

    public static ActiveProfileState CreateLegacyFullWtf(string id, string? snapshotPath = null)
    {
        return new ActiveProfileState
        {
            Id = id,
            LocalProfileId = id,
            Granularity = ProfileGranularity.FullWtf,
            SnapshotPath = snapshotPath,
        };
    }
}
