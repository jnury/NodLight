using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace NodLight.Core;

public static class PipeProtocol
{
    public static string PipeName => "NodLight." + Environment.UserName;

    /// <summary>One JSON line (no embedded newlines).</summary>
    public static string Encode(HookEvent e) =>
        JsonSerializer.Serialize(e, CoreJsonContext.Default.HookEvent);

    /// <summary>Returns null for garbage or JSON lacking required information.</summary>
    public static HookEvent? Decode(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try
        {
            var e = JsonSerializer.Deserialize(line, CoreJsonContext.Default.HookEvent);
            return e is { EventName: not null } ? e : null;
        }
        catch (JsonException) { return null; }
    }
}

public static class PipeClient
{
    /// <summary>
    /// Sends one event. Never throws. <paramref name="timeoutMs"/> is the total budget
    /// for connect plus write. A missing pipe returns false immediately.
    /// </summary>
    public static bool TrySend(string pipeName, HookEvent e, int timeoutMs = 200)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            if (!PipeExists(pipeName)) return false;

            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            client.Connect(timeoutMs);

            var remaining = timeoutMs - (int)sw.ElapsedMilliseconds;
            if (remaining <= 0) return false;

            var bytes = Encoding.UTF8.GetBytes(PipeProtocol.Encode(e) + "\n");
            using var cts = new CancellationTokenSource(remaining);
            client.WriteAsync(bytes, cts.Token).AsTask().GetAwaiter().GetResult();
            client.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Enumerating \.\pipe\ does not connect to (and so never consumes) a pipe instance.
    // If enumeration itself fails we assume the pipe may exist and let Connect decide.
    static bool PipeExists(string pipeName)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(@"\.\pipe\", pipeName).Any();
        }
        catch
        {
            return true;
        }
    }
}

public sealed class PipeServer(string pipeName, Action<HookEvent> onEvent) : IDisposable
{
    static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);

    readonly CancellationTokenSource cts = new();
    Task? loop;

    public void Start()
    {
        if (loop is not null) return;
        // The first instance is created synchronously so a client that sends right
        // after Start() returns can always connect.
        var first = CreateInstance();
        loop = Task.Run(() => AcceptLoop(first));
    }

    NamedPipeServerStream CreateInstance()
    {
        // Restrict to the current user. Multiple instances are allowed so a fresh
        // listener can be created while earlier connections are still being handled.
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            pipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
    }

    async Task AcceptLoop(NamedPipeServerStream pipe)
    {
        var token = cts.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                var connected = pipe;
                pipe = CreateInstance();
                _ = Task.Run(() => HandleAsync(connected, token));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Client vanished mid-accept or instance creation failed: recycle and carry on.
                pipe.Dispose();
                try { pipe = CreateInstance(); }
                catch
                {
                    try { await Task.Delay(100, token).ConfigureAwait(false); } catch { break; }
                    pipe = CreateInstance();
                }
            }
        }
        pipe.Dispose();
    }

    async Task HandleAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        try
        {
            using (pipe)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(ReadTimeout);
                using var reader = new StreamReader(pipe, new UTF8Encoding(false));
                var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                if (line is null) return;
                var ev = PipeProtocol.Decode(line);
                if (ev is null) return;
                onEvent(ev);
            }
        }
        catch
        {
            // Timeouts, broken pipes and handler exceptions must never affect the accept loop.
        }
    }

    public void Dispose()
    {
        cts.Cancel();
        try { loop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        cts.Dispose();
    }
}
