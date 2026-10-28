namespace NodLight.Core;

public sealed record DiscoveredSession(AgentKind Agent, int AgentPid, int AlacrittyPid);

public static class SessionDiscovery
{
    /// <summary>
    /// Agent processes (claude.exe, codex.exe) that run in an Alacritty window, found by the same parent-chain walk the
    /// hook helper uses. Nested agents (an agent started from inside another one) are skipped. At most one session per
    /// Alacritty process: the most recently started agent wins.
    /// </summary>
    public static IReadOnlyList<DiscoveredSession> Find(IEnumerable<ProcessInfo> processes, IProcessTree tree) =>
        processes
            .Select(p => (Process: p, Agent: AgentOf(p.Name)))
            .Where(x => x.Agent is not null)
            .OrderByDescending(x => x.Process.StartUtc)
            .ThenBy(x => x.Process.Pid)
            .Select(x => (x.Process, Agent: x.Agent!.Value, Chain: ProcessChain.Resolve(x.Process.Pid, tree)))
            .Where(x => x.Chain is { AlacrittyPid: not null, Nested: false })
            .DistinctBy(x => x.Chain.AlacrittyPid)
            .Select(x => new DiscoveredSession(x.Agent, x.Process.Pid, x.Chain.AlacrittyPid!.Value))
            .ToList();

    static AgentKind? AgentOf(string name) =>
        name.Equals("claude.exe", StringComparison.OrdinalIgnoreCase) ? AgentKind.Claude
        : name.Equals("codex.exe", StringComparison.OrdinalIgnoreCase) ? AgentKind.Codex
        : null;
}
