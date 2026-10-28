using System.Diagnostics;
using System.IO;
using System.IO.Pipes;

namespace NodLight.App;

/// <summary>
/// How a second NodLight.exe start (the taskbar jump-list tasks launch the exe with an argument) hands its command to
/// the running deck: one line per connection over a per-user pipe, restricted to the current user.
/// </summary>
public static class CommandPipe
{
    public const string New = "new";
    public const string InstallHooks = "install-hooks";
    public const string RemoveHooks = "remove-hooks";
    public const string Hide = "hide";

    static string PipeName => "NodLight.Command." + Environment.UserName;

    /// <summary>The command a jump-list launch carries ("--install-hooks" -> "install-hooks"), or null.</summary>
    public static string? FromArgs(string[] args) => args switch
    {
        ["--new"] => New,
        ["--install-hooks"] => InstallHooks,
        ["--remove-hooks"] => RemoveHooks,
        ["--hide"] => Hide,
        _ => null,
    };

    public static bool TrySend(string command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(command);
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"NodLight: could not reach the running deck: {ex.Message}");
            return false;
        }
    }

    /// <summary>Accepts commands until cancelled; <paramref name="onCommand"/> runs on a pool thread for each known one.</summary>
    public static void Listen(Action<string> onCommand, CancellationToken stop) =>
        Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(stop);
                    using var reader = new StreamReader(server);
                    var line = await reader.ReadLineAsync(stop);
                    if (line is New or InstallHooks or RemoveHooks or Hide) onCommand(line);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Trace.WriteLine($"NodLight: command pipe failed: {ex.Message}");
                    try { await Task.Delay(500, stop); } catch (OperationCanceledException) { return; }
                }
            }
        }, stop);
}
