using System.Collections.ObjectModel;
using HearthSwing.Models.Accounts;

namespace HearthSwing.ViewModels;

public sealed class RealmSaveSelectionViewModel
{
    public RealmSaveSelectionViewModel(string realmName, AccountSnapshotDiffStatus status)
    {
        RealmName = realmName;
        Status = status;
    }

    public string RealmName { get; }

    public AccountSnapshotDiffStatus Status { get; }

    public string StatusText => Status switch
    {
        AccountSnapshotDiffStatus.New => "all new",
        AccountSnapshotDiffStatus.Modified => "has changes",
        _ => "unchanged",
    };

    public ObservableCollection<CharacterSaveSelectionViewModel> Characters { get; } = [];
}