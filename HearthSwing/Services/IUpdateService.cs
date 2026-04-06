using HearthSwing.Models;

namespace HearthSwing.Services;

public interface IUpdateService
{
    event Action<string>? Log;
    Task<UpdateCheckResult?> CheckForUpdateAsync(string currentVersion, CancellationToken ct);
    Task ApplyUpdateAsync(UpdateCheckResult update, CancellationToken ct);
}
