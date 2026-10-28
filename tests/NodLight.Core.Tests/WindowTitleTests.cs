using NodLight.Core;

namespace NodLight.Core.Tests;

public class WindowTitleTests
{
    [Theory]
    [InlineData("✳ NodLight", "NodLight", TitleState.Idle)]
    [InlineData("◐ Payments API v2", "Payments API v2", TitleState.Working)]
    [InlineData("◓ Burndown", "Burndown", TitleState.Working)]
    [InlineData("◑ Burndown", "Burndown", TitleState.Working)]
    [InlineData("◒ Burndown", "Burndown", TitleState.Working)]
    [InlineData("⠂ Several agent sessions", "Several agent sessions", TitleState.Working)]
    [InlineData("◐Burndown", "Burndown", TitleState.Working)]
    [InlineData("✳", "", TitleState.Idle)]
    public void Strips_the_status_glyph_and_reports_it(string title, string name, TitleState state) =>
        Assert.Equal((name, state), WindowTitle.Parse(title));

    [Theory]
    [InlineData("Plain title")]
    [InlineData("pwsh")]
    [InlineData("  ✳ not first")]
    [InlineData("Project ✳ inside")]
    [InlineData("★ other symbol")]
    public void Leaves_other_titles_alone(string title) =>
        Assert.Equal((title, TitleState.None), WindowTitle.Parse(title));

    [Theory, InlineData(null), InlineData("")]
    public void Empty_title_is_empty(string? title) => Assert.Equal(("", TitleState.None), WindowTitle.Parse(title));
}
