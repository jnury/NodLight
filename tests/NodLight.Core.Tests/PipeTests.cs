using System.Diagnostics;
using NodLight.Core;

namespace NodLight.Core.Tests;

public class PipeTests
{
    static string UniqueName() => "NodLightTest." + Guid.NewGuid().ToString("N");

    static HookEvent Sample(string eventName = "Notification") => new(
        AgentKind.Claude, eventName, "s1", @"C:\r\webapp", @"C:\t.jsonl",
        "needs permission", "permission_prompt", 4242,
        new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Encode_decode_roundtrip()
    {
        var e = Sample();
        var line = PipeProtocol.Encode(e);
        Assert.DoesNotContain('\n', line);
        Assert.Equal(e, PipeProtocol.Decode(line));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"Agent\":\"Bogus\"}")]
    public void Decode_garbage_is_null(string line)
    {
        Assert.Null(PipeProtocol.Decode(line));
    }

    [Fact]
    public void Server_receives_sent_event()
    {
        var name = UniqueName();
        var tcs = new TaskCompletionSource<HookEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var server = new PipeServer(name, e => tcs.TrySetResult(e));
        server.Start();

        Assert.True(PipeClient.TrySend(name, Sample(), 1000));
        Assert.True(tcs.Task.Wait(2000));
        Assert.Equal(Sample(), tcs.Task.Result);
    }

    [Fact]
    public void Server_survives_bad_lines_idle_clients_and_throwing_handler()
    {
        var name = UniqueName();
        var received = new System.Collections.Concurrent.BlockingCollection<HookEvent>();
        var first = true;
        using var server = new PipeServer(name, e =>
        {
            received.Add(e);
            if (Interlocked.Exchange(ref first, false)) throw new InvalidOperationException("boom");
        });
        server.Start();

        // A client that connects and never sends.
        using var idle = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.Out);
        idle.Connect(1000);

        // A client that sends garbage.
        using (var bad = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.Out))
        {
            bad.Connect(1000);
            var bytes = System.Text.Encoding.UTF8.GetBytes("garbage\n");
            bad.Write(bytes, 0, bytes.Length);
        }

        Assert.True(PipeClient.TrySend(name, Sample("A"), 1000));
        Assert.True(received.TryTake(out var a, 2000));
        Assert.Equal("A", a!.EventName);

        Assert.True(PipeClient.TrySend(name, Sample("B"), 1000));
        Assert.True(received.TryTake(out var b, 2000));
        Assert.Equal("B", b!.EventName);
    }

    [Fact]
    public void Dispose_stops_server_promptly()
    {
        var name = UniqueName();
        var server = new PipeServer(name, _ => { });
        server.Start();
        var sw = Stopwatch.StartNew();
        server.Dispose();
        Assert.True(sw.ElapsedMilliseconds < 2000);
        Assert.False(PipeClient.TrySend(name, Sample(), 200));
    }

    [Fact]
    public void TrySend_without_server_returns_false_within_500ms()
    {
        var sw = Stopwatch.StartNew();
        var ok = PipeClient.TrySend(UniqueName(), Sample());
        sw.Stop();
        Assert.False(ok);
        Assert.True(sw.ElapsedMilliseconds < 500, $"took {sw.ElapsedMilliseconds} ms");
    }
}
