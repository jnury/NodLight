using NodLight.Core;

namespace NodLight.Core.Tests;

public class TimeFormatTests
{
    [Theory]
    [InlineData(0, "0 s")]
    [InlineData(59, "59 s")]
    [InlineData(60, "1 min")]
    [InlineData(59 * 60, "59 min")]
    [InlineData(61 * 60, "1 h")]
    [InlineData(-5, "0 s")]
    public void Short_formats_floor(int seconds, string expected) =>
        Assert.Equal(expected, TimeFormat.Short(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Short_floors_fractions() =>
        Assert.Equal("59 s", TimeFormat.Short(TimeSpan.FromSeconds(59.9)));
}
