using NodLight.Core;

namespace NodLight.Core.Tests;

public class Win32ProcessTreeTests
{
    [Fact]
    public void Reads_the_current_directory_of_a_running_process()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(
            Environment.CurrentDirectory.TrimEnd('\\'),
            Win32ProcessTree.CurrentDirectoryOf(Environment.ProcessId));
    }
}
