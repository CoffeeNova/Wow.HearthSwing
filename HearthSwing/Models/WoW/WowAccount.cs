namespace HearthSwing.Models.WoW;

public sealed record WowAccount
{
    public required string AccountName { get; init; }
    public required string FolderPath { get; init; }
    public IReadOnlyList<WowRealm> Realms { get; init; } = [];
}
