namespace HearthSwing.Models.WoW;

public sealed record WowRealm
{
    public required string AccountName { get; init; }
    public required string RealmName { get; init; }
    public required string FolderPath { get; init; }
    public IReadOnlyList<WowCharacter> Characters { get; init; } = [];
}
