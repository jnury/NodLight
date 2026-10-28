using NodLight.Core;

namespace NodLight.Core.Tests;

public class SessionDiscoveryTests
{
    static readonly DateTime t0 = new(2026, 10, 27, 10, 0, 0, DateTimeKind.Utc);

    sealed class FakeTree(IReadOnlyList<ProcessInfo> procs) : IProcessTree
    {
        readonly Dictionary<int, ProcessInfo> map = procs.ToDictionary(p => p.Pid);
        public ProcessInfo? Get(int pid) => map.GetValueOrDefault(pid);
    }

    // Parents start earlier than children, so the PID-reuse check never trips.
    static ProcessInfo P(int pid, int parent, string name, int startSeconds) =>
        new(pid, parent, name, t0.AddSeconds(startSeconds));

    static IReadOnlyList<DiscoveredSession> Find(params ProcessInfo[] procs) =>
        SessionDiscovery.Find(procs, new FakeTree(procs));

    [Fact]
    public void Finds_claude_and_codex_in_separate_alacritty_windows()
    {
        var found = Find(
            P(1, 0, "alacritty.exe", 0), P(2, 1, "pwsh.exe", 1), P(3, 2, "claude.exe", 2),
            P(10, 0, "alacritty.exe", 0), P(11, 10, "pwsh.exe", 1), P(12, 11, "codex.exe", 2));

        Assert.Equal(
            [new DiscoveredSession(AgentKind.Claude, 3, 1), new DiscoveredSession(AgentKind.Codex, 12, 10)],
            found.OrderBy(s => s.AlacrittyPid).ToArray());
    }

    [Fact]
    public void Ignores_agents_outside_alacritty()
    {
        Assert.Empty(Find(P(1, 0, "WindowsTerminal.exe", 0), P(2, 1, "pwsh.exe", 1), P(3, 2, "claude.exe", 2)));
    }

    [Fact]
    public void Skips_nested_agents_and_keeps_the_outer_one()
    {
        var found = Find(
            P(1, 0, "alacritty.exe", 0), P(2, 1, "pwsh.exe", 1), P(3, 2, "claude.exe", 2),
            P(4, 3, "bash.exe", 3), P(5, 4, "claude.exe", 4));

        Assert.Equal([new DiscoveredSession(AgentKind.Claude, 3, 1)], found);
    }

    [Fact]
    public void One_session_per_window_newest_agent_wins()
    {
        var found = Find(
            P(1, 0, "alacritty.exe", 0), P(2, 1, "pwsh.exe", 1), P(3, 2, "claude.exe", 2), P(4, 2, "codex.exe", 5));

        Assert.Equal([new DiscoveredSession(AgentKind.Codex, 4, 1)], found);
    }
}
