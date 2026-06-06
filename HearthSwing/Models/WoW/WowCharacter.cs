namespace HearthSwing.Models.WoW;

public sealed record WowCharacter
{
    public required string AccountName { get; init; }
    public required string RealmName { get; init; }
    public required string CharacterName { get; init; }
    public required string FolderPath { get; init; }
}
