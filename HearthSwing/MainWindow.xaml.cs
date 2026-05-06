using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HearthSwing.Models.Accounts;
using HearthSwing.Models;
using HearthSwing.ViewModels;

namespace HearthSwing;

public partial class MainWindow : Window
{
    private static readonly SolidColorBrush[] AccentBrushes =
    [
        new(Color.FromRgb(0x4a, 0x9e, 0xff)), // blue
        new(Color.FromRgb(0xe8, 0x43, 0x93)), // pink
        new(Color.FromRgb(0x00, 0xb8, 0x94)), // green
        new(Color.FromRgb(0xfd, 0xcb, 0x6e)), // yellow
        new(Color.FromRgb(0x6c, 0x5c, 0xe7)), // purple
        new(Color.FromRgb(0xe1, 0x7a, 0x55)), // orange
    ];

    private readonly MainViewModel _vm;
    private bool _closePending;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();

        _vm = vm;
        DataContext = _vm;

        _vm.PropertyChanged += OnViewModelPropertyChanged;

        Loaded += (_, _) => UpdateProfileButtons();
        Closing += OnWindowClosing;
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_closePending)
            return;

        if (!_vm.IsArchiving)
            return;

        e.Cancel = true;
        _closePending = true;
        _vm.IsCloseBlockedByArchiving = true;

        await _vm.WaitForArchivingAsync();

        Close();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (
            e.PropertyName
            is nameof(MainViewModel.CurrentSavedAccountId)
                or nameof(MainViewModel.SavedAccounts)
        )
            UpdateProfileButtons();

        if (e.PropertyName is nameof(MainViewModel.LogText))
            LogScroller.ScrollToEnd();
    }

    private void UpdateProfileButtons()
    {
        var activeId = _vm.CurrentSavedAccountId;
        var cardBg = (SolidColorBrush)FindResource("CardBg");

        ProfileIndicator.Foreground = GetAccentBrush(activeId);

        for (var i = 0; i < ProfileButtons.Items.Count; i++)
        {
            var container = ProfileButtons.ItemContainerGenerator.ContainerFromIndex(i);
            if (container is null)
                continue;

            var btn = FindChild<Button>(container);
            if (btn is null)
                continue;

            var account = (SavedAccountSummary)ProfileButtons.Items[i];
            var accent = AccentBrushes[i % AccentBrushes.Length];
            var isActive = account.Id == activeId;

            btn.Background = isActive ? accent : cardBg;
            btn.BorderBrush = accent;
        }
    }

    private SolidColorBrush GetAccentBrush(string savedAccountId)
    {
        for (var i = 0; i < _vm.SavedAccounts.Count; i++)
        {
            if (_vm.SavedAccounts[i].Id == savedAccountId)
                return AccentBrushes[i % AccentBrushes.Length];
        }
        return (SolidColorBrush)FindResource("TextPrimary");
    }

    private static T? FindChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t)
                return t;
            var found = FindChild<T>(child);
            if (found is not null)
                return found;
        }
        return null;
    }
}
