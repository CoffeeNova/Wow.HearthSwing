namespace HearthSwing.Models.WoW;

public sealed record WowInstallation
{
    public required string GamePath { get; init; }
    public required string WtfPath { get; init; }
    public IReadOnlyList<WowAccount> Accounts { get; init; } = [];
}
