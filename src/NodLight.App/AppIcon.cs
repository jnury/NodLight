using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NodLight.App;

/// <summary>The app icon, drawn in code: used for the taskbar button and the strip's title bar.</summary>
public static class AppIcon
{
    // Three stacked cards in the status colours (waiting, working, idle) on the strip's background.
    public static BitmapSource Create()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(Frozen(0x11, 0x11, 0x1B), null, new Rect(0, 0, 64, 64), 12, 12);
            DrawBar(dc, 8, Frozen(0xFA, 0xB3, 0x87));
            DrawBar(dc, 24, Frozen(0xA6, 0xE3, 0xA1));
            DrawBar(dc, 40, Frozen(0x6C, 0x70, 0x86));
        }

        var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    static void DrawBar(DrawingContext dc, double top, Brush brush) =>
        dc.DrawRoundedRectangle(brush, null, new Rect(8, top, 48, 14), 4, 4);

    static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
