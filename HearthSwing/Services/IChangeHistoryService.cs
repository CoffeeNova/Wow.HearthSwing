using HearthSwing.Models;

namespace HearthSwing.Services;

public interface IChangeHistoryService
{
    event Action<string>? Log;

    Task<HistoryEntry> SnapshotAsync(
        string targetKey,
        HistoryTargetKind kind,
        string sourceFolder,
        string description,
        CancellationToken ct = default
    );

    IReadOnlyList<HistoryEntry> List(string targetKey);

    IReadOnlyList<HistoryEntry> ListAll();

    Task RestoreAsync(HistoryEntry entry, CancellationToken ct = default);

    Task DeleteAsync(HistoryEntry entry, CancellationToken ct = default);
}
