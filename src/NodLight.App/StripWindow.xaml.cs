using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using static NodLight.App.Native;

namespace NodLight.App;

/// <summary>
/// The always-on-top strip anchored to the bottom-right corner of the primary monitor's work area. Its height
/// follows the card count (capped at the work area; the cards scroll beyond that).
/// </summary>
public partial class StripWindow : Window
{
    const double HeaderHeight = 34; // keep in sync with the header row in StripWindow.xaml

    readonly DeckViewModel deck;

    public StripWindow(DeckViewModel deck)
    {
        InitializeComponent();
        this.deck = deck;
        DataContext = deck;
        Dock();
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => Dock();
        Closed += (_, _) => SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
    }

    public long Handle => new WindowInteropHelper(this).Handle;

    // SystemParameters.WorkArea is the primary monitor's work area in device-independent units (the process is
    // system-DPI aware, WPF's default), which is the unit Left/Top/Width/Height use, so no manual scaling is needed.
    void Dock()
    {
        var area = SystemParameters.WorkArea;
        MaxHeight = area.Height;
        Scroller.MaxHeight = Math.Max(0, area.Height - HeaderHeight - 1);
        Left = area.Right - Width;
        Top = area.Bottom - (ActualHeight > 0 ? ActualHeight : HeaderHeight);
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

    void Hide_Click(object sender, RoutedEventArgs e) => Hide();

    void NewSession_Click(object sender, RoutedEventArgs e) => AlacrittyLauncher.Start();
}
