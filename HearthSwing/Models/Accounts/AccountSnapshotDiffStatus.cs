namespace HearthSwing.Models.Accounts;

/// <summary>
/// Describes how a live account slice differs from the last saved snapshot.
/// </summary>
public enum AccountSnapshotDiffStatus
{
    New,
    Modified,
    Unchanged,
}