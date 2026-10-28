using NodLight.Core;

namespace NodLight.Core.Tests;

public class ExecutableLocatorTests
{
    static readonly char Sep = Path.PathSeparator;

    [Fact]
    public void Prefers_the_first_path_entry_that_has_the_file()
    {
        var existing = new HashSet<string> { Path.Combine("b", "a.exe"), Path.Combine("c", "a.exe") };

        var found = ExecutableLocator.Find("a.exe", $"a{Sep}b{Sep}c", [], existing.Contains);

        Assert.Equal(Path.Combine("b", "a.exe"), found);
    }

    [Fact]
    public void Falls_back_to_the_extra_directories_after_path()
    {
        var found = ExecutableLocator.Find("a.exe", $"x{Sep}y", ["fallback"], p => p == Path.Combine("fallback", "a.exe"));

        Assert.Equal(Path.Combine("fallback", "a.exe"), found);
    }

    [Fact]
    public void Returns_null_when_missing_and_skips_blank_or_quoted_entries()
    {
        Assert.Null(ExecutableLocator.Find("a.exe", $"{Sep}  {Sep}\"bad|dir\"", [], _ => false));
        Assert.Equal(Path.Combine("q", "a.exe"), ExecutableLocator.Find("a.exe", "\"q\"", [], _ => true));
        Assert.Null(ExecutableLocator.Find("a.exe", null, [], _ => false));
    }
}
