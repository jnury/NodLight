using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using static NodLight.App.Native;

namespace NodLight.App;

/// <summary>The always-on-top strip docked to the right edge of the primary monitor's work area.</summary>
public partial class StripWindow : Window
{
    readonly DeckViewModel deck;

    public StripWindow(DeckViewModel deck)
    {
        InitializeComponent();
        this.deck = deck;
        DataContext = deck;
        Dock();
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
    }

    public long Handle => new WindowInteropHelper(this).Handle;

    // SystemParameters.WorkArea is the primary monitor's work area in device-independent units (the process is
    // system-DPI aware, WPF's default), which is the unit Left/Top/Width/Height use, so no manual scaling is needed.
    void Dock()
    {
        var area = SystemParameters.WorkArea;
        Height = area.Height;
        Left = area.Right - Width;
        Top = area.Top;
    }

    void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.WorkArea)) Dock();
    }

    // WS_EX_TOOLWINDOW keeps the strip out of Alt-Tab. WS_EX_NOACTIVATE means clicking a card does not make the
    // deck the foreground window first: the terminal the user was in stays active until Focus() switches to the
    // target, so no spurious foreground event for the deck is raised and focus never lands on the strip itself.
    // Mouse input (clicks, wheel, context menu) still works for non-activating windows.
    void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        SetExStyle(hwnd, GetExStyle(hwnd) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CardViewModel card }) deck.Activate(card);
    }

    void InstallHooks_Click(object sender, RoutedEventArgs e) => Setup.InstallFromMenu();

    void RemoveHooks_Click(object sender, RoutedEventArgs e) => Setup.RemoveFromMenu();

    void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
