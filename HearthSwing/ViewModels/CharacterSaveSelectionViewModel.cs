using CommunityToolkit.Mvvm.ComponentModel;
using HearthSwing.Models.Accounts;

namespace HearthSwing.ViewModels;

public partial class CharacterSaveSelectionViewModel : ObservableObject
{
    private readonly Action _selectionChanged;

    public CharacterSaveSelectionViewModel(
        string realmName,
        string characterName,
        AccountSnapshotDiffStatus status,
        bool isSelected,
        Action selectionChanged
    )
    {
        RealmName = realmName;
        CharacterName = characterName;
        Status = status;
        _isSelected = isSelected;
        _selectionChanged = selectionChanged;
    }

    public string RealmName { get; }

    public string CharacterName { get; }

    public AccountSnapshotDiffStatus Status { get; }

    public string StatusText => Status switch
    {
        AccountSnapshotDiffStatus.New => "new",
        AccountSnapshotDiffStatus.Modified => "modified",
        _ => "unchanged",
    };

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _selectionChanged();
}