using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HearthSwing.Models.WoW;

namespace HearthSwing.ViewModels;

/// <summary>
/// A node in the account → realm → character selection tree used when picking a donor or target
/// character for a template. Account and realm nodes are containers; character nodes are leaves.
/// </summary>
public partial class WtfTreeNodeViewModel : ObservableObject
{
    public WtfTreeNodeViewModel(string label, WowCharacter? character)
    {
        Label = label;
        Character = character;
    }

    public string Label { get; }

    public WowCharacter? Character { get; }

    public bool IsCharacter => Character is not null;

    public ObservableCollection<WtfTreeNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>
    /// Applies a case-insensitive filter. A character node matches on its own labels; a container
    /// node stays visible when any descendant matches. Returns true when this node stays visible.
    /// </summary>
    public bool ApplyFilter(string? search)
    {
        if (Character is not null)
        {
            IsVisible =
                string.IsNullOrWhiteSpace(search)
                || Contains(Character.CharacterName, search)
                || Contains(Character.RealmName, search)
                || Contains(Character.AccountName, search);
            return IsVisible;
        }

        var anyChildVisible = false;
        foreach (var child in Children)
            anyChildVisible |= child.ApplyFilter(search);

        IsVisible = anyChildVisible;
        if (!string.IsNullOrWhiteSpace(search))
            IsExpanded = anyChildVisible;

        return IsVisible;
    }

    private static bool Contains(string value, string search) =>
        value.Contains(search, StringComparison.OrdinalIgnoreCase);
}
