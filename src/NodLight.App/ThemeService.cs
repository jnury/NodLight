using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using NodLight.Core;
using Microsoft.Win32;

namespace NodLight.App;

/// <summary>
/// Keeps the app's brush resources in step with the Windows colour mode and accent colour. The window's XAML refers
/// to them with DynamicResource, so replacing a brush here repaints everything. Call from the UI thread.
/// </summary>
public static class ThemeService
{
    const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Raised on the UI thread after the resources were refreshed (and once when it starts).</summary>
    public static event Action? Changed;

    public static void Start(Application app)
    {
        Apply(app);
        // Raised on a system thread whenever Windows settings change (colour mode, accent, ...).
        UserPreferenceChangedEventHandler onChanged = (_, _) => app.Dispatcher.BeginInvoke(() => Apply(app));
        SystemEvents.UserPreferenceChanged += onChanged;
        app.Exit += (_, _) => SystemEvents.UserPreferenceChanged -= onChanged;
    }

    /// <summary>Whether apps (the strip) should draw light; the default when Windows does not say is dark.</summary>
    public static bool AppsUseLightTheme => ReadFlag("AppsUseLightTheme", defaultValue: false);

    /// <summary>Whether the taskbar and its menus draw light, which is what the taskbar jump list follows.</summary>
    public static bool SystemUsesLightTheme => ReadFlag("SystemUsesLightTheme", defaultValue: false);

    static void Apply(Application app)
    {
        try
        {
            var accent = SystemParameters.WindowGlassColor;
            var palette = ThemePalette.Create(AppsUseLightTheme, ((uint)accent.R << 16) | ((uint)accent.G << 8) | accent.B);
            foreach (var (key, rgb) in palette)
            {
                var brush = new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
                brush.Freeze();
                app.Resources[key + "Brush"] = brush;
            }
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"NodLight: theme update failed: {ex}");
        }
    }

    static bool ReadFlag(string name, bool defaultValue)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(name) is int value ? value != 0 : defaultValue;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return defaultValue;
        }
    }
}
