namespace HearthSwing.Models;

public sealed class ProfileVersion
{
    public required string VersionId { get; init; }

    public required string ProfileId { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required string ArchivePath { get; init; }

    public string DisplayName => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
}
