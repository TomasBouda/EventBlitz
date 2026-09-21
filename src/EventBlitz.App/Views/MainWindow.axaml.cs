using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EventBlitz.App.ViewModels;

namespace EventBlitz.App.Views;

public partial class MainWindow : Window
{
    private bool _gripDragging;
    private double _gripStartY;
    private double _gripStartHeight;

    public MainWindow()
    {
        InitializeComponent();

        // Tunnel so the palette gets Up/Down/Enter/Esc before the text box does.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        KeyDown += OnKeyDown;
        Loaded += OnLoaded;
        PaletteList.PointerReleased += OnPaletteItemPointerReleased;
    }

    private MainWindowViewModel? Vm => DataContext as MainWindowViewModel;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // The window is up: a freshly installed update may drop its rollback backup.
        TomLabs.AutoUpdate.Updater.Current?.MarkHealthy();

        if (Vm is { } vm)
        {
            vm.CopyToClipboard = text => Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
            vm.FocusSearch = () =>
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            };
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainWindowViewModel.IsMergedView))
                    UpdateChannelColumn(vm);
            };
            UpdateChannelColumn(vm);
        }
    }

    /// <summary>The channel column only earns its space when more than one channel is on screen.</summary>
    private void UpdateChannelColumn(MainWindowViewModel vm)
    {
        if (EventsGrid.Columns.Count > 2)
            EventsGrid.Columns[2].IsVisible = vm.IsMergedView;
    }

    // ----- Keyboard -----

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;

        if (vm.Palette.IsOpen)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    vm.Palette.Close();
                    e.Handled = true;
                    break;
                case Key.Down:
                    vm.Palette.MoveSelection(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    vm.Palette.MoveSelection(-1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    vm.Palette.RunSelected();
                    e.Handled = true;
                    break;
            }
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.K)
        {
            OpenPalette();
            e.Handled = true;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm || vm.Palette.IsOpen) return;

        if (e.Key == Key.F5)
        {
            vm.RunQueryCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && vm.IsLoading)
        {
            vm.CancelLoadingCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            switch (e.Key)
            {
                case Key.L:
                    vm.ToggleThemeCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.X:
                    vm.ClearFiltersCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        }
        else if (e.KeyModifiers == KeyModifiers.Control)
        {
            switch (e.Key)
            {
                case Key.F:
                    vm.FocusSearch?.Invoke();
                    e.Handled = true;
                    break;
                case Key.L:
                    vm.ToggleLiveCommand.Execute(null);
                    e.Handled = true;
                    break;
                case Key.C when e.Source is not TextBox && e.Source is not SelectableTextBlock:
                    vm.CopyEventCommand.Execute(null);
                    e.Handled = true;
                    break;
            }
        }
    }

    // ----- Command palette -----

    private void OpenPalette()
    {
        if (Vm is not { } vm) return;
        vm.Palette.Open();
        Dispatcher.UIThread.Post(() =>
        {
            PaletteBox.Focus();
            PaletteBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void OnPaletteButtonClick(object? sender, RoutedEventArgs e) => OpenPalette();

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e) => Vm?.Palette.Close();

    private void OnPaletteItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<ListBoxItem>(true) != null)
            Vm?.Palette.RunSelected();
    }

    // ----- Title bar -----

    /// <summary>The header doubles as the title bar: drag moves the window, double-click toggles maximize.</summary>
    private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(true) != null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2)
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
            BeginMoveDrag(e);
    }

    // ----- Detail pane resize -----

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is not { } vm) return;
        _gripDragging = true;
        _gripStartY = e.GetPosition(this).Y;
        _gripStartHeight = vm.DetailHeight;
        e.Pointer.Capture(DetailGrip);
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (!_gripDragging || Vm is not { } vm) return;
        var delta = _gripStartY - e.GetPosition(this).Y;
        vm.DetailHeight = Math.Clamp(_gripStartHeight + delta, 120, Math.Max(120, Bounds.Height - 300));
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        _gripDragging = false;
        e.Pointer.Capture(null);
    }
}
