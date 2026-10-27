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
            // One budget shared by the existence check, connect and write.
            var sw = Stopwatch.StartNew();
            if (!PipeExists(pipeName)) return false;

            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            client.Connect(Math.Max(1, timeoutMs - (int)sw.ElapsedMilliseconds));

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

    // Enumerating \\.\pipe\ does not connect to (and so never consumes) a pipe instance.
    // If enumeration itself fails we assume the pipe may exist and let Connect decide.
    static bool PipeExists(string pipeName)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(@"\\.\pipe\", pipeName).Any();
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
    int disposed;

    /// <summary>
    /// Starts listening. The first instance is created with <see cref="PipeOptions.FirstPipeInstance"/>,
    /// so if another process already owns the pipe name this throws (IOException or
    /// UnauthorizedAccessException) instead of silently listening beside a squatter.
    /// </summary>
    public void Start()
    {
        if (loop is not null) return;
        // The first instance is created synchronously so a client that sends right
        // after Start() returns can always connect.
        var first = CreateInstance(PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance);
        loop = Task.Run(() => AcceptLoop(first));
    }

    NamedPipeServerStream CreateInstance(PipeOptions options = PipeOptions.Asynchronous)
    {
        // Restrict to the current user. Multiple instances are allowed so a fresh
        // listener can be created while earlier connections are still being handled.
        if (!OperatingSystem.IsWindows())
            return new NamedPipeServerStream(pipeName, PipeDirection.In,
                NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, options);
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            pipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, options, 0, 0, security);
    }

    // Never exits except on cancellation. `pipe` is null whenever no listening instance exists.
    async Task AcceptLoop(NamedPipeServerStream? pipe)
    {
        var token = cts.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    pipe ??= CreateInstance();
                    await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                    var connected = pipe;
                    pipe = null; // ownership moves to the handler
                    _ = Task.Run(() => HandleAsync(connected, token));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    // Client vanished mid-accept or instance creation failed: drop only a
                    // pipe that was not handed off, back off briefly, and retry.
                    pipe?.Dispose();
                    pipe = null;
                    try { await Task.Delay(100, token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
        finally
        {
            pipe?.Dispose();
        }
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
        if (Interlocked.Exchange(ref disposed, 1) == 1) return;
        cts.Cancel();
        try { loop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        cts.Dispose();
    }
}
