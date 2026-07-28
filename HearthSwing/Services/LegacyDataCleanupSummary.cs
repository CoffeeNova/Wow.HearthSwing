namespace HearthSwing.Services;

public sealed record LegacyDataCleanupSummary
{
    public IReadOnlyList<string> Directories { get; init; } = [];

    public IReadOnlyList<string> Files { get; init; } = [];

    public bool HasItems => Directories.Count > 0 || Files.Count > 0;

    public int TotalCount => Directories.Count + Files.Count;
}