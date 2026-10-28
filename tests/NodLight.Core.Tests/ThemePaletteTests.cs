using NodLight.Core;

namespace NodLight.Core.Tests;

public class ThemePaletteTests
{
    const uint Accent = 0x0078D4;

    public static TheoryData<bool> Themes => new() { true, false };

    [Theory, MemberData(nameof(Themes))]
    public void Text_is_readable_on_the_surfaces_it_sits_on(bool light)
    {
        var p = ThemePalette.Create(light, Accent);

        Assert.True(ThemePalette.Contrast(p["Text"], p["Card"]) >= 7);
        Assert.True(ThemePalette.Contrast(p["Text"], p["Header"]) >= 7);
        Assert.True(ThemePalette.Contrast(p["TextSecondary"], p["Card"]) >= 4.5);
        Assert.True(ThemePalette.Contrast(p["TextMuted"], p["Card"]) >= 4.5);
        Assert.True(ThemePalette.Contrast(p["TextMuted"], p["CardWaiting"]) >= 4.5);
        Assert.True(ThemePalette.Contrast(p["BadgeText"], p["Badge"]) >= 4.5);
        Assert.True(ThemePalette.Contrast(p["Text"], p["Button"]) >= 4.5);
        Assert.True(ThemePalette.Contrast(p["Text"], p["ButtonHover"]) >= 4.5);
    }

    [Theory, MemberData(nameof(Themes))]
    public void Status_and_warning_colours_are_visible_on_cards(bool light)
    {
        var p = ThemePalette.Create(light, Accent);

        foreach (var key in new[] { "Waiting", "Working", "Idle" })
            Assert.True(ThemePalette.Contrast(p[key], p["Card"]) >= 3, key);
        Assert.True(ThemePalette.Contrast(p["HighUsage"], p["Card"]) >= 4.5);
        Assert.True(ThemePalette.Contrast(p["Waiting"], p["CardWaiting"]) >= 3);
    }

    [Fact]
    public void Light_and_dark_differ_and_share_the_same_keys()
    {
        var light = ThemePalette.Create(true, Accent);
        var dark = ThemePalette.Create(false, Accent);

        Assert.Equal(dark.Keys.Order(), light.Keys.Order());
        Assert.NotEqual(dark["Surface"], light["Surface"]);
        Assert.True(ThemePalette.Contrast(light["Surface"], dark["Surface"]) > 10);
    }

    [Fact]
    public void Accent_tints_button_hover_only()
    {
        var a = ThemePalette.Create(false, 0x0078D4);
        var b = ThemePalette.Create(false, 0xE81123);

        Assert.NotEqual(a["ButtonHover"], b["ButtonHover"]);
        Assert.Equal(a["Waiting"], b["Waiting"]);
        Assert.Equal(a["Card"], b["Card"]);
    }

    [Fact]
    public void Mix_blends_each_channel()
    {
        Assert.Equal(0x808080u, ThemePalette.Mix(0x000000, 0xFFFFFF, 0.5));
        Assert.Equal(0x102030u, ThemePalette.Mix(0x102030, 0xFFFFFF, 0));
    }
}
