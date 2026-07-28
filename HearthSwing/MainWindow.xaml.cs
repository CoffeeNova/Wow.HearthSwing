using System.ComponentModel;
using System.Windows;
using HearthSwing.Models;
using HearthSwing.Services;
using HearthSwing.ViewModels;

namespace HearthSwing;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ISettingsService _settings;
    private bool _closePending;

    public MainWindow(MainViewModel vm, ISettingsService settings)
    {
        InitializeComponent();

        _vm = vm;
        _settings = settings;
        DataContext = _vm;

        RestoreWindowPlacement();

        _vm.PropertyChanged += OnViewModelPropertyChanged;
        Closing += OnWindowClosing;
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_closePending)
        {
            SaveWindowPlacement();
            return;
        }

        if (!_vm.IsArchiving)
        {
            SaveWindowPlacement();
            return;
        }

        e.Cancel = true;
        _closePending = true;
        _vm.IsCloseBlockedByArchiving = true;

        await _vm.WaitForArchivingAsync();

        Close();
    }

    private void RestoreWindowPlacement()
    {
        var settings = _settings.Current;

        if (settings.WindowWidth is > 0)
            Width = settings.WindowWidth.Value;

        if (settings.WindowHeight is > 0)
            Height = settings.WindowHeight.Value;

        if (settings.WindowLeft.HasValue && settings.WindowTop.HasValue)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = settings.WindowLeft.Value;
            Top = settings.WindowTop.Value;
        }

        if (settings.StartMaximized)
            WindowState = WindowState.Maximized;
    }

    private void SaveWindowPlacement()
    {
        var bounds =
            WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;

        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        _settings.Current.WindowLeft = bounds.Left;
        _settings.Current.WindowTop = bounds.Top;
        _settings.Current.WindowWidth = bounds.Width;
        _settings.Current.WindowHeight = bounds.Height;
        _settings.Current.StartMaximized = WindowState == WindowState.Maximized;
        _settings.Save();
    }

    private void OnDonorTreeSelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e
    )
    {
        _vm.SelectedDonorCharacter = (e.NewValue as WtfTreeNodeViewModel)?.Character;
    }

    private void OnTargetTreeSelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e
    )
    {
        _vm.SelectedTargetCharacter = (e.NewValue as WtfTreeNodeViewModel)?.Character;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.LogText))
            LogScroller.ScrollToEnd();
    }
}
