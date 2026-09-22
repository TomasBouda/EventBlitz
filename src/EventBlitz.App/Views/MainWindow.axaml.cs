using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit.TextMate;
using EventBlitz.App.Controls;
using EventBlitz.App.ViewModels;
using TextMateSharp.Grammars;

namespace EventBlitz.App.Views;

public partial class MainWindow : Window
{
    private bool _gripDragging;
    private double _gripStartY;
    private double _gripStartHeight;
    private TextMate.Installation? _xmlTextMate;

    public MainWindow()
    {
        InitializeComponent();

        // Tunnel so the palette gets Up/Down/Enter/Esc before the text box does.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        KeyDown += OnKeyDown;
        Loaded += OnLoaded;
        PaletteList.PointerReleased += OnPaletteItemPointerReleased;

        SetupEditors();
        ActualThemeVariantChanged += (_, _) => ApplyEditorTheme();
    }

    /// <summary>XML gets a TextMate grammar; the message a colorizer of its own since it is free text with structure.</summary>
    private void SetupEditors()
    {
        var registry = new RegistryOptions(ThemeName.DarkPlus);
        _xmlTextMate = XmlEditor.InstallTextMate(registry);
        _xmlTextMate.SetGrammar(registry.GetScopeByLanguageId("xml"));
        MessageEditor.TextArea.TextView.LineTransformers.Add(new MessageColorizer());
        MessageEditor.Options.EnableHyperlinks = false;
        XmlEditor.Options.EnableHyperlinks = false;
    }

    /// <summary>Dark+ / Light+ follow the app theme; the colorizer reads the theme brushes live, so it only needs a redraw.</summary>
    private void ApplyEditorTheme()
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var registry = new RegistryOptions(dark ? ThemeName.DarkPlus : ThemeName.LightPlus);
        try
        {
            _xmlTextMate?.SetTheme(registry.LoadTheme(dark ? ThemeName.DarkPlus : ThemeName.LightPlus));
        }
        catch (Exception ex)
        {
            Services.AppLog.For("ui").Warning(ex, "Editor theme switch failed");
        }
        MessageEditor.TextArea.TextView.Redraw();
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
                switch (args.PropertyName)
                {
                    case nameof(MainWindowViewModel.IsMergedView):
                        UpdateChannelColumn(vm);
                        break;
                    case nameof(MainWindowViewModel.SelectedEvent):
                        MessageEditor.Document.Text = vm.SelectedEvent?.Message ?? string.Empty;
                        MessageEditor.ScrollToHome();
                        if (vm.SelectedEvent is { } selected) EventsGrid.ScrollIntoView(selected, null);
                        break;
                    case nameof(MainWindowViewModel.DetailXml):
                        XmlEditor.Document.Text = vm.DetailXml;
                        XmlEditor.ScrollToHome();
                        break;
                }
            };
            UpdateChannelColumn(vm);
            ApplyEditorTheme();
            vm.AlertRaised += _ => FlashTaskbar();
            HitsList.SelectionChanged += (_, _) =>
            {
                if (HitsList.SelectedItem is Services.AlertHit hit)
                {
                    HitsList.SelectedItem = null;
                    vm.RevealAlertCommand.Execute(hit);
                }
            };
        }
    }

    /// <summary>The rule editor's "Selected" button copies the sidebar selection into the channel list.</summary>
    private void OnUseSelectedChannels(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && (sender as Control)?.DataContext is AlertRuleViewModel rule)
            rule.SetChannels(vm.SelectedChannelNames);
    }

    /// <summary>Flashes the taskbar button until the window is activated, so an alert is noticed while it sits in the background.</summary>
    private void FlashTaskbar()
    {
        if (!OperatingSystem.IsWindows() || IsActive) return;
        if (TryGetPlatformHandle()?.Handle is not { } handle || handle == IntPtr.Zero) return;
        var info = new FlashWInfo
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<FlashWInfo>(),
            hwnd = handle,
            dwFlags = FlashTray | FlashTimerNoFg,
            uCount = uint.MaxValue,
            dwTimeout = 0,
        };
        FlashWindowEx(ref info);
    }

    private const uint FlashTray = 0x2;
    private const uint FlashTimerNoFg = 0xC;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct FlashWInfo
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashWInfo info);

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
                case Key.A:
                    vm.ToggleAlertsCommand.Execute(null);
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
