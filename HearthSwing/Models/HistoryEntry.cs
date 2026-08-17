namespace HearthSwing.Models;

public sealed record HistoryEntry
{
    public required string TargetKey { get; init; }
    public required HistoryTargetKind Kind { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required string Description { get; init; }
    public required string ArchivePath { get; init; }
    public long SizeBytes { get; init; }
    public string? AccountName { get; init; }
    public string? RealmName { get; init; }
    public string? CharacterName { get; init; }

    public string DisplayName => CreatedUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
}