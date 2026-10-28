using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NodLight.Core;
using static NodLight.App.Native;

namespace NodLight.App;

/// <summary>
/// The deck's taskbar presence. The strip itself is a tool window (no taskbar button, no Alt-Tab), so this tiny
/// minimised window carries the button: clicking it shows the strip when hidden and hides it when shown.
/// Closing it from the taskbar exits the app.
/// Hovering the button shows a dashboard (sessions, CPU, memory, I/O) instead of a blank thumbnail: the window opts in
/// to DWM's "iconic" thumbnail and answers its requests with a bitmap rendered from <see cref="DashboardView"/>.
/// </summary>
public sealed class TaskbarWindow : Window
{
    readonly DeckViewModel deck;

    public TaskbarWindow(Window strip, DeckViewModel deck)
    {
        this.deck = deck;
        Title = "NodLight";
        Width = 200;
        Height = 80;
        ShowInTaskbar = true;
        ShowActivated = false;
        WindowState = WindowState.Minimized;
        Icon = AppIcon.Create();

        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) return;
            // The taskbar click restored this window: put it straight back and toggle the strip instead.
            Dispatcher.BeginInvoke(() =>
            {
                WindowState = WindowState.Minimized;
                if (strip.IsVisible) strip.Hide();
                else strip.Show();
            });
        };
        Closed += (_, _) => Application.Current.Shutdown();
    }

    /// <summary>Asks Windows to request fresh thumbnail bitmaps (it only does so while a thumbnail is on screen).</summary>
    public void RefreshThumbnail()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != 0) DwmInvalidateIconicBitmaps(hwnd);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var on = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_FORCE_ICONIC_REPRESENTATION, ref on, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_HAS_ICONIC_BITMAP, ref on, sizeof(int));
        ((HwndSource)PresentationSource.FromVisual(this)).AddHook(WndProc);
    }

    nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        // Called from the window's message loop: an escaping exception would take the app down.
        try
        {
            switch (msg)
            {
                case WM_DWMSENDICONICTHUMBNAIL:
                    // lParam: maximum thumbnail width in the high word, height in the low word.
                    SendBitmap(hwnd, (int)((lParam >> 16) & 0xFFFF), (int)(lParam & 0xFFFF), live: false);
                    handled = true;
                    break;
                case WM_DWMSENDICONICLIVEPREVIEWBITMAP:
                    // The larger preview shown while hovering the thumbnail.
                    SendBitmap(hwnd, 960, 600, live: true);
                    handled = true;
                    break;
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"NodLight: thumbnail failed: {ex}");
        }
        return 0;
    }

    void SendBitmap(nint hwnd, int maxWidth, int maxHeight, bool live)
    {
        if (maxWidth <= 0 || maxHeight <= 0) return;
        var hbitmap = ToHBitmap(Render(deck.Summarize(), maxWidth, maxHeight));
        try
        {
            if (live) DwmSetIconicLivePreviewBitmap(hwnd, hbitmap, 0, 0);
            else DwmSetIconicThumbnail(hwnd, hbitmap, 0);
        }
        finally
        {
            DeleteObject(hbitmap); // DWM keeps its own copy
        }
    }

    /// <summary>The dashboard scaled to fit inside the given size, keeping its aspect ratio.</summary>
    public static BitmapSource Render(DashboardSummary summary, int maxWidth, int maxHeight)
    {
        var view = new DashboardView(summary);
        view.Measure(new Size(DashboardView.DesignWidth, DashboardView.DesignHeight));
        view.Arrange(new Rect(0, 0, DashboardView.DesignWidth, DashboardView.DesignHeight));
        view.UpdateLayout();

        var scale = Math.Min(maxWidth / DashboardView.DesignWidth, maxHeight / DashboardView.DesignHeight);
        var width = Math.Max(1, (int)(DashboardView.DesignWidth * scale));
        var height = Math.Max(1, (int)(DashboardView.DesignHeight * scale));
        var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(view);
        bitmap.Freeze();
        return bitmap;
    }

    // A top-down 32-bit DIB; the dashboard is opaque, so premultiplied alpha is moot.
    static nint ToHBitmap(BitmapSource source)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var pixels = new byte[width * height * 4];
        source.CopyPixels(pixels, width * 4, 0);

        var info = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = width,
            Height = -height,
            Planes = 1,
            BitCount = 32,
        };
        var hbitmap = CreateDIBSection(0, ref info, 0, out var bits, 0, 0);
        if (hbitmap == 0 || bits == 0) throw new InvalidOperationException("CreateDIBSection failed.");
        Marshal.Copy(pixels, 0, bits, pixels.Length);
        return hbitmap;
    }
}
