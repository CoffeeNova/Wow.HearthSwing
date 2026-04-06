namespace HearthSwing.Models;

public sealed class UpdateCheckResult
{
    public required string Version { get; init; }
    public required string DownloadUrl { get; init; }
}
