namespace NodLight.Core;

public sealed record ProcessInfo(int Pid, int ParentPid, string Name, DateTime StartUtc);

public interface IProcessTree
{
    ProcessInfo? Get(int pid);
}

public sealed record ChainResult(int? AlacrittyPid, bool Nested);

public static class ProcessChain
{
    const int MaxSteps = 64;

    /// <summary>
    /// Walks parent links from <paramref name="startPid"/> (inclusive) looking for alacritty.exe,
    /// counting claude.exe/codex.exe on the way. Nested is true when more than one agent is found.
    /// </summary>
    public static ChainResult Resolve(int startPid, IProcessTree tree)
    {
        var agents = 0;
        var current = tree.Get(startPid);

        for (var step = 0; step < MaxSteps && current is not null; step++)
        {
            if (Is(current, "alacritty.exe")) return new ChainResult(current.Pid, agents > 1);
            if (Is(current, "claude.exe") || Is(current, "codex.exe")) agents++;

            var parent = tree.Get(current.ParentPid);
            // A parent that started after its child means the PID was reused. MinValue means unknown start time.
            if (parent is not null
                && parent.StartUtc != DateTime.MinValue && current.StartUtc != DateTime.MinValue
                && parent.StartUtc > current.StartUtc)
                break;
            current = parent;
        }

        return new ChainResult(null, agents > 1);
    }

    static bool Is(ProcessInfo p, string name) => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase);
}
