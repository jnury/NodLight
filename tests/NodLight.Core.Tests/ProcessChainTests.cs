using NodLight.Core;

namespace NodLight.Core.Tests;

public class ProcessChainTests
{
    static readonly DateTime t0 = new(2026, 10, 27, 10, 0, 0, DateTimeKind.Utc);

    sealed class FakeTree(IEnumerable<ProcessInfo> procs) : IProcessTree
    {
        readonly Dictionary<int, ProcessInfo> map = procs.ToDictionary(p => p.Pid);
        public ProcessInfo? Get(int pid) => map.GetValueOrDefault(pid);
    }

    // Builds a chain child-first: pids 1..n, each one's parent is the next; the last has parent 0.
    static FakeTree Chain(params string[] names) =>
        new(names.Select((n, i) => new ProcessInfo(i + 1, i + 2 > names.Length ? 0 : i + 2, n, t0.AddSeconds(-i))));

    [Fact]
    public void Finds_alacritty_through_shells()
    {
        var r = ProcessChain.Resolve(1, Chain("hook.exe", "bash.exe", "claude.exe", "pwsh.exe", "alacritty.exe"));
        Assert.Equal(new ChainResult(5, false), r);
    }

    [Fact]
    public void Codex_npm_wrapper_is_not_nested()
    {
        var r = ProcessChain.Resolve(1, Chain("hook.exe", "codex.exe", "node.exe", "pwsh.exe", "alacritty.exe"));
        Assert.Equal(new ChainResult(5, false), r);
    }

    [Fact]
    public void Claude_print_inside_session_is_nested()
    {
        var r = ProcessChain.Resolve(1, Chain("hook.exe", "claude.exe", "bash.exe", "claude.exe", "pwsh.exe", "alacritty.exe"));
        Assert.Equal(new ChainResult(6, true), r);
    }

    [Fact]
    public void Names_are_compared_case_insensitively()
    {
        var r = ProcessChain.Resolve(1, Chain("hook.exe", "Claude.EXE", "bash.exe", "CLAUDE.exe", "Alacritty.exe"));
        Assert.Equal(new ChainResult(5, true), r);
    }

    [Fact]
    public void Start_process_itself_counts()
    {
        var r = ProcessChain.Resolve(1, Chain("claude.exe", "bash.exe", "claude.exe", "alacritty.exe"));
        Assert.Equal(new ChainResult(4, true), r);
    }

    [Fact]
    public void Pid_reuse_stops_walk()
    {
        var tree = new FakeTree([
            new ProcessInfo(1, 2, "hook.exe", t0),
            new ProcessInfo(2, 3, "claude.exe", t0.AddSeconds(-5)),
            new ProcessInfo(3, 4, "pwsh.exe", t0.AddMinutes(10)), // started after its child: reused pid
            new ProcessInfo(4, 0, "alacritty.exe", t0.AddMinutes(-20)),
        ]);
        Assert.Null(ProcessChain.Resolve(1, tree).AlacrittyPid);
    }

    [Fact]
    public void Unknown_start_time_does_not_trigger_reuse_check()
    {
        var tree = new FakeTree([
            new ProcessInfo(1, 2, "hook.exe", DateTime.MinValue),
            new ProcessInfo(2, 3, "pwsh.exe", t0),
            new ProcessInfo(3, 0, "alacritty.exe", t0.AddMinutes(-1)),
        ]);
        Assert.Equal(new ChainResult(3, false), ProcessChain.Resolve(1, tree));
    }

    [Fact]
    public void Unknown_parent_start_time_does_not_trigger_reuse_check()
    {
        var tree = new FakeTree([
            new ProcessInfo(1, 2, "hook.exe", t0),
            new ProcessInfo(2, 3, "pwsh.exe", DateTime.MinValue),
            new ProcessInfo(3, 0, "alacritty.exe", t0.AddMinutes(-1)),
        ]);
        Assert.Equal(new ChainResult(3, false), ProcessChain.Resolve(1, tree));
    }

    [Fact]
    public void No_alacritty_returns_null()
    {
        var r = ProcessChain.Resolve(1, Chain("hook.exe", "claude.exe", "pwsh.exe", "explorer.exe"));
        Assert.Null(r.AlacrittyPid);
    }

    [Fact]
    public void Missing_start_process_returns_null()
    {
        Assert.Equal(new ChainResult(null, false), ProcessChain.Resolve(99, Chain("hook.exe")));
    }

    [Fact]
    public void Cycle_is_bounded()
    {
        var tree = new FakeTree([
            new ProcessInfo(1, 2, "a.exe", t0),
            new ProcessInfo(2, 1, "b.exe", t0),
        ]);
        Assert.Null(ProcessChain.Resolve(1, tree).AlacrittyPid);
    }
}
