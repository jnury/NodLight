namespace NodLight.Core;

/// <summary>Cumulative counters of one process at one instant. CPU time and I/O are totals since the process started.</summary>
public sealed record ProcessCounters(
    int Pid, string Name, DateTime StartUtc, long CpuTicks, long WorkingSetBytes, long IoBytes);

public sealed record ProcessUsage(string Name, double CpuPercent, long MemoryBytes);

/// <summary>
/// One session's resource use over the last interval. <see cref="CpuPercent"/> is a share of the whole machine
/// (all logical cores), like Task Manager. <see cref="IoBytesPerSec"/> counts every read and write the processes
/// issue (files, pipes, network), which is what Windows reports per process.
/// </summary>
public sealed record ResourceUsage(
    double CpuPercent, long MemoryBytes, double IoBytesPerSec, int ProcessCount, IReadOnlyList<ProcessUsage> Top)
{
    public const double HighCpuPercent = 50;
    public const long HighMemoryBytes = 4L * 1024 * 1024 * 1024;
    public const double HighIoBytesPerSec = 50 * 1024 * 1024;

    public bool CpuHigh => CpuPercent >= HighCpuPercent;
    public bool MemoryHigh => MemoryBytes >= HighMemoryBytes;
    public bool IoHigh => IoBytesPerSec >= HighIoBytesPerSec;
}

/// <summary>
/// Turns cumulative per-process counters into rates, per session root. Keeps the previous sample of each root,
/// so call <see cref="Measure"/> on a steady tick with every live root each time (roots left out are forgotten).
/// A process first seen after the first sample started inside the interval, so its whole total counts: that keeps
/// short-lived children (compilers, test hosts) that live only part of an interval in the figures.
/// Processes that start and exit between two samples are still invisible.
/// </summary>
public sealed class ResourceMeter(int logicalCores)
{
    sealed record Previous(DateTimeOffset At, Dictionary<(int Pid, DateTime Start), (long Cpu, long Io)> Procs);

    readonly Dictionary<int, Previous> previous = new();

    public Dictionary<int, ResourceUsage> Measure(
        IReadOnlyDictionary<int, IReadOnlyList<ProcessCounters>> byRoot, DateTimeOffset now)
    {
        var result = new Dictionary<int, ResourceUsage>();
        var next = new Dictionary<int, Previous>();

        foreach (var (root, procs) in byRoot)
        {
            previous.TryGetValue(root, out var before);
            var elapsedTicks = before is null ? 0 : (now - before.At).Ticks;

            double cpuDelta = 0, ioDelta = 0;
            var top = new List<ProcessUsage>();
            var current = new Dictionary<(int Pid, DateTime Start), (long Cpu, long Io)>();

            foreach (var p in procs)
            {
                var key = (p.Pid, p.StartUtc);
                current[key] = (p.CpuTicks, p.IoBytes);

                var cpu = 0.0;
                if (before is not null && elapsedTicks > 0)
                {
                    var (prevCpu, prevIo) = before.Procs.GetValueOrDefault(key);
                    cpu = Math.Max(0, p.CpuTicks - prevCpu);
                    cpuDelta += cpu;
                    ioDelta += Math.Max(0, p.IoBytes - prevIo);
                }
                top.Add(new ProcessUsage(p.Name, Percent(cpu, elapsedTicks), p.WorkingSetBytes));
            }

            next[root] = new Previous(now, current);
            result[root] = new ResourceUsage(
                Percent(cpuDelta, elapsedTicks),
                procs.Sum(p => p.WorkingSetBytes),
                elapsedTicks > 0 ? ioDelta / (elapsedTicks / (double)TimeSpan.TicksPerSecond) : 0,
                procs.Count,
                top.OrderByDescending(t => t.CpuPercent).ThenByDescending(t => t.MemoryBytes).Take(5).ToList());
        }

        previous.Clear();
        foreach (var (root, state) in next) previous[root] = state;
        return result;
    }

    double Percent(double cpuTicks, long elapsedTicks) =>
        elapsedTicks <= 0 ? 0 : Math.Min(100, cpuTicks / (elapsedTicks * (double)Math.Max(1, logicalCores)) * 100);
}

public static class ResourceFormat
{
    /// <summary>"340 MB", "1.2 GB": binary units, one decimal from GB up.</summary>
    public static string Bytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} KB",
        _ => $"{bytes} B",
    };

    public static string Rate(double bytesPerSec) => Bytes((long)bytesPerSec) + "/s";

    public static string Percent(double percent) => percent < 10 && percent > 0 ? $"{percent:0.0}%" : $"{percent:0}%";
}

public static class ProcessTreeWalk
{
    /// <summary>
    /// Every descendant of <paramref name="root"/> (not the root itself) from a pid -> children lookup. A child whose
    /// start time is earlier than its parent's is a reused pid, not a real child, and its branch is dropped.
    /// <paramref name="startOf"/> returns <see cref="DateTime.MinValue"/> for an unknown start time, which skips the check.
    /// </summary>
    public static List<int> Descendants(int root, ILookup<int, int> childrenOf, Func<int, DateTime> startOf)
    {
        var result = new List<int>();
        var seen = new HashSet<int> { root };
        var queue = new Queue<int>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            var parentStart = startOf(parent);
            foreach (var child in childrenOf[parent])
            {
                if (!seen.Add(child)) continue;
                var childStart = startOf(child);
                if (parentStart != DateTime.MinValue && childStart != DateTime.MinValue && childStart < parentStart) continue;
                result.Add(child);
                queue.Enqueue(child);
            }
        }
        return result;
    }
}
