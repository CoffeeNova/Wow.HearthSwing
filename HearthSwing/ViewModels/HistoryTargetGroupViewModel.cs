using System.Collections.ObjectModel;
using HearthSwing.Models;

namespace HearthSwing.ViewModels;

public sealed class HistoryTargetGroupViewModel
{
    public required string KindLabel { get; init; }

    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    public ObservableCollection<HistoryEntry> Entries { get; } = [];
}