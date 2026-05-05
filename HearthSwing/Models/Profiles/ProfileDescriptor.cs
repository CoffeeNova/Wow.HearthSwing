namespace HearthSwing.Models.Profiles;

public sealed record ProfileDescriptor
{
    public required string Id { get; init; }
    public string? LocalProfileId { get; init; }
    public required ProfileGranularity Granularity { get; init; }
    public string? AccountName { get; init; }
    public string? RealmName { get; init; }
    public string? CharacterName { get; init; }
    public required string SnapshotPath { get; init; }
    public string? DisplayName { get; init; }
    public string? VersionId { get; init; }
    public DateTimeOffset? LastSavedUtc { get; init; }

    public string EffectiveDisplayName =>
        string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new InvalidOperationException("Profile descriptor id is required.");

        if (string.IsNullOrWhiteSpace(SnapshotPath))
            throw new InvalidOperationException("Profile descriptor snapshot path is required.");

        switch (Granularity)
        {
            case ProfileGranularity.FullWtf:
                return;

            case ProfileGranularity.PerAccount when string.IsNullOrWhiteSpace(AccountName):
                throw new InvalidOperationException(
                    "Account name is required for a per-account profile."
                );

            case ProfileGranularity.PerCharacter
                when string.IsNullOrWhiteSpace(AccountName)
                    || string.IsNullOrWhiteSpace(RealmName)
                    || string.IsNullOrWhiteSpace(CharacterName):
                throw new InvalidOperationException(
                    "Account, realm, and character names are required for a per-character profile."
                );
        }
    }

    public ActiveProfileState ToActiveProfileState()
    {
        Validate();

        return new ActiveProfileState
        {
            Id = Id,
            LocalProfileId = LocalProfileId,
            Granularity = Granularity,
            AccountName = AccountName,
            RealmName = RealmName,
            CharacterName = CharacterName,
            SnapshotPath = SnapshotPath,
            VersionId = VersionId,
        };
    }
}
