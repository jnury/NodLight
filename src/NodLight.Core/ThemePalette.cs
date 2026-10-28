namespace NodLight.Core;

/// <summary>
/// The strip's colours for the Windows light or dark mode, as 0xRRGGBB. Neutrals follow the Windows 11 surfaces;
/// the status colours (waiting, working, idle) keep their meaning but get a darker tone on light surfaces so they
/// stay readable. The Windows accent colour only tints interactive states (button hover), never the statuses.
/// </summary>
public static class ThemePalette
{
    /// <summary>Palette keys; the app exposes each as a brush resource named "&lt;Key&gt;Brush".</summary>
    public static IReadOnlyDictionary<string, uint> Create(bool light, uint accent)
    {
        var p = new Dictionary<string, uint>(light ? Light : Dark)
        {
            ["Accent"] = accent,
        };
        p["ButtonHover"] = Mix(p["Button"], accent, 0.35);
        return p;
    }

    static readonly Dictionary<string, uint> Dark = new()
    {
        ["Surface"] = 0x202020,
        ["Header"] = 0x2B2B2B,
        ["Edge"] = 0x3F3F3F,
        ["Card"] = 0x2D2D2D,
        ["CardWaiting"] = 0x3B2E24,
        ["Text"] = 0xF3F3F3,
        ["TextSecondary"] = 0xC9C9C9,
        ["TextMuted"] = 0x9E9E9E,
        ["Badge"] = 0x404040,
        ["BadgeText"] = 0xD6D6D6,
        ["Button"] = 0x3A3A3A,
        ["Waiting"] = 0xFAB387,
        ["Working"] = 0x7FD67A,
        ["Idle"] = 0x8C8C8C,
        ["HighUsage"] = 0xFF7A8E,
    };

    static readonly Dictionary<string, uint> Light = new()
    {
        ["Surface"] = 0xF3F3F3,
        ["Header"] = 0xE8E8E8,
        ["Edge"] = 0xC9C9C9,
        ["Card"] = 0xFFFFFF,
        ["CardWaiting"] = 0xFFEFE2,
        ["Text"] = 0x1B1B1B,
        ["TextSecondary"] = 0x4D4D4D,
        ["TextMuted"] = 0x666666,
        ["Badge"] = 0xE4E4E4,
        ["BadgeText"] = 0x3D3D3D,
        ["Button"] = 0xDADADA,
        ["Waiting"] = 0xC2540A,
        ["Working"] = 0x2B7A2F,
        ["Idle"] = 0x757575,
        ["HighUsage"] = 0xC42B1C,
    };

    /// <summary>Linear mix of two RGB colours: <paramref name="amount"/> of <paramref name="b"/>, the rest of <paramref name="a"/>.</summary>
    public static uint Mix(uint a, uint b, double amount)
    {
        static uint Channel(uint x, int shift) => (x >> shift) & 0xFF;
        uint Blend(int shift) =>
            (uint)Math.Round(Channel(a, shift) * (1 - amount) + Channel(b, shift) * amount);
        return (Blend(16) << 16) | (Blend(8) << 8) | Blend(0);
    }

    /// <summary>WCAG contrast ratio (1 to 21) between two RGB colours.</summary>
    public static double Contrast(uint a, uint b)
    {
        var (hi, lo) = (Luminance(a), Luminance(b));
        if (hi < lo) (hi, lo) = (lo, hi);
        return (hi + 0.05) / (lo + 0.05);
    }

    static double Luminance(uint rgb)
    {
        static double Linear(uint c)
        {
            var v = c / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear((rgb >> 16) & 0xFF) + 0.7152 * Linear((rgb >> 8) & 0xFF) + 0.0722 * Linear(rgb & 0xFF);
    }
}
