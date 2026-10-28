using NodLight.Core;

namespace NodLight.Core.Tests;

public class ResourceMeterTests
{
    static readonly DateTimeOffset t0 = new(2026, 10, 27, 10, 0, 0, TimeSpan.Zero);
    static readonly DateTime started = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);
    const long Second = TimeSpan.TicksPerSecond;
    const long MB = 1024 * 1024;

    static ProcessCounters P(int pid, long cpuSeconds, long workingSetMb = 10, long ioMb = 0, string name = "p.exe") =>
        new(pid, name, started.AddSeconds(pid), cpuSeconds * Second, workingSetMb * MB, ioMb * MB);

    static Dictionary<int, IReadOnlyList<ProcessCounters>> Roots(params ProcessCounters[] procs) =>
        new() { [1] = procs };

    [Fact]
    public void First_sample_reports_memory_and_count_but_no_rates()
    {
        var u = new ResourceMeter(4).Measure(Roots(P(10, 100, 200, 500), P(11, 50, 300)), t0)[1];

        Assert.Equal((0.0, 0.0, 500 * MB, 2), (u.CpuPercent, u.IoBytesPerSec, u.MemoryBytes, u.ProcessCount));
    }

    [Fact]
    public void Cpu_is_a_share_of_the_whole_machine_over_the_interval()
    {
        var meter = new ResourceMeter(4);
        meter.Measure(Roots(P(10, 100), P(11, 50)), t0);

        // 2 s later the two processes used 2 s and 2 s of CPU: 4 core-seconds out of 8 available = 50 %.
        var u = meter.Measure(Roots(P(10, 102), P(11, 52)), t0.AddSeconds(2))[1];

        Assert.Equal(50, u.CpuPercent, 3);
        Assert.True(u.CpuHigh);
    }

    [Fact]
    public void Io_is_bytes_per_second()
    {
        var meter = new ResourceMeter(4);
        meter.Measure(Roots(P(10, 0, ioMb: 100)), t0);

        var u = meter.Measure(Roots(P(10, 0, ioMb: 160)), t0.AddSeconds(2))[1];

        Assert.Equal(30.0 * MB, u.IoBytesPerSec, 1);
    }

    [Fact]
    public void A_process_born_during_the_interval_counts_in_full()
    {
        var meter = new ResourceMeter(1);
        meter.Measure(Roots(P(10, 0)), t0);

        var u = meter.Measure(Roots(P(10, 0), P(11, 1, ioMb: 8)), t0.AddSeconds(2))[1];

        Assert.Equal(50, u.CpuPercent, 3);
        Assert.Equal(4.0 * MB, u.IoBytesPerSec, 1);
    }

    [Fact]
    public void An_exited_process_does_not_make_the_rates_negative()
    {
        var meter = new ResourceMeter(1);
        meter.Measure(Roots(P(10, 10, ioMb: 100), P(11, 10, ioMb: 100)), t0);

        var u = meter.Measure(Roots(P(10, 11, ioMb: 101)), t0.AddSeconds(2))[1];

        Assert.Equal(50, u.CpuPercent, 3);
        Assert.Equal(0.5 * MB, u.IoBytesPerSec, 1);
    }

    [Fact]
    public void Cpu_is_capped_at_100_and_top_lists_the_busiest_process_first()
    {
        var meter = new ResourceMeter(1);
        meter.Measure(Roots(P(10, 0, name: "idle.exe"), P(11, 0, name: "busy.exe")), t0);

        var u = meter.Measure(Roots(P(10, 0, name: "idle.exe"), P(11, 5, name: "busy.exe")), t0.AddSeconds(2))[1];

        Assert.Equal(100, u.CpuPercent);
        Assert.Equal(["busy.exe", "idle.exe"], u.Top.Select(t => t.Name).ToArray());
    }

    [Fact]
    public void A_root_left_out_is_forgotten_and_restarts_without_rates()
    {
        var meter = new ResourceMeter(1);
        meter.Measure(Roots(P(10, 0)), t0);
        meter.Measure(new Dictionary<int, IReadOnlyList<ProcessCounters>>(), t0.AddSeconds(2));

        var u = meter.Measure(Roots(P(10, 50)), t0.AddSeconds(4))[1];

        Assert.Equal(0, u.CpuPercent);
    }

    [Fact]
    public void High_thresholds()
    {
        Assert.True(new ResourceUsage(0, 4L * 1024 * MB, 0, 1, []).MemoryHigh);
        Assert.False(new ResourceUsage(0, 4L * 1024 * MB - 1, 0, 1, []).MemoryHigh);
        Assert.True(new ResourceUsage(0, 0, 50.0 * MB, 1, []).IoHigh);
        Assert.False(new ResourceUsage(49.9, 0, 0, 1, []).CpuHigh);
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(2048L, "2 KB")]
    [InlineData(340L * MB, "340 MB")]
    [InlineData(1288490189L, "1.2 GB")]
    public void Formats_bytes(long bytes, string expected) => Assert.Equal(expected, ResourceFormat.Bytes(bytes));

    [Theory]
    [InlineData(0.0, "0%")]
    [InlineData(3.14, "3.1%")]
    [InlineData(42.4, "42%")]
    public void Formats_percent(double percent, string expected) => Assert.Equal(expected, ResourceFormat.Percent(percent));

    [Fact]
    public void Descendants_walks_the_tree_and_drops_reused_pids()
    {
        // 1 -> 2 -> 4, 1 -> 3, and 5 claims parent 2 but started before it (a reused pid).
        var edges = new[] { (1, 2), (1, 3), (2, 4), (2, 5), (9, 8) }.ToLookup(e => e.Item1, e => e.Item2);
        var starts = new Dictionary<int, DateTime>
        {
            [1] = started, [2] = started.AddSeconds(1), [3] = started.AddSeconds(2),
            [4] = started.AddSeconds(3), [5] = started.AddSeconds(-5),
        };

        var found = ProcessTreeWalk.Descendants(1, edges, pid => starts.GetValueOrDefault(pid, DateTime.MinValue));

        Assert.Equal([2, 3, 4], found.OrderBy(p => p).ToArray());
    }
}
