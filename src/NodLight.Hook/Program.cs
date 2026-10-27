using System.Text;
using NodLight.Core;
using NodLight.Hook;

// Hook helper: nodlight-hook <claude|codex> <EventName>, JSON payload on stdin.
// Must never fail, block, or write output (agents treat hook output as meaningful).
// Exception: --dump-chain is a manual diagnostic and prints to stdout.
const int StdinBudgetMs = 150; // pipe send has its own separate 200 ms budget

var at = DateTimeOffset.UtcNow; // when the agent fired the hook, before any of the work below

try
{
    if (args is ["--dump-chain"])
    {
        foreach (var p in new Win32ProcessTree().Chain(Environment.ProcessId))
            Console.Out.WriteLine($"{p.Pid} {p.Name}");
        return 0;
    }

    // Drain stdin before any drop path, so the agent never blocks writing a large payload to a helper that left early.
    var json = ReadStdin(StdinBudgetMs);

    if (args.Length != 2) return 0;
    AgentKind agent;
    if (args[0].Equals("claude", StringComparison.OrdinalIgnoreCase)) agent = AgentKind.Claude;
    else if (args[0].Equals("codex", StringComparison.OrdinalIgnoreCase)) agent = AgentKind.Codex;
    else return 0;
    var eventName = args[1];

    var chain = ProcessChain.Resolve(Environment.ProcessId, new Win32ProcessTree());
    if (chain.AlacrittyPid is not { } alacrittyPid || chain.Nested) return 0;

    var payload = HookPayloadParser.Parse(agent, eventName, json);
    var ev = new HookEvent(agent, eventName, payload.SessionId, payload.Cwd, payload.TranscriptPath,
        payload.Detail, payload.NotificationType, alacrittyPid, at);
    PipeClient.TrySend(PipeProtocol.PipeName, ev);
}
catch
{
    // Swallow everything: a hook must never disturb the agent.
}
return 0;

// Reads stdin only when redirected, bounded by a time budget; returns whatever arrived.
static string ReadStdin(int budgetMs)
{
    try
    {
        if (!Console.IsInputRedirected) return "{}";
        var buffer = new MemoryStream();
        var input = Console.OpenStandardInput();
        var reader = Task.Run(() =>
        {
            var chunk = new byte[8192];
            int n;
            while ((n = input.Read(chunk, 0, chunk.Length)) > 0)
                lock (buffer) buffer.Write(chunk, 0, n);
        });
        reader.Wait(budgetMs);
        lock (buffer) return Encoding.UTF8.GetString(buffer.ToArray());
    }
    catch
    {
        return "{}";
    }
}
