using System.Text;
using NodLight.Core;

namespace NodLight.Core.Tests;

public class InterruptTests : IDisposable
{
    // UNVERIFIED: no real Claude interrupt line was found in the newest 20 transcripts; structure follows
    // the brief (user message whose text block starts with "[Request interrupted by user").
    const string ClaudeInterrupt =
        """{"parentUuid":"00000000-0000-0000-0000-000000000000","isSidechain":false,"type":"user","message":{"role":"user","content":[{"type":"text","text":"[Request interrupted by user]"}]},"uuid":"11111111-1111-1111-1111-111111111111","timestamp":"2026-10-27T10:00:00.000Z"}""";

    // UNVERIFIED payload: real Codex rollouts use {"timestamp","ordinal","type":"event_msg","payload":{"type":...}};
    // turn_aborted was not present in the files inspected.
    const string CodexAborted =
        """{"timestamp":"2026-10-27T10:00:00.000Z","ordinal":42,"type":"event_msg","payload":{"type":"turn_aborted","reason":"interrupted"}}""";

    const string AssistantQuoting =
        """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"The marker is [Request interrupted by user] and turn_aborted."}]}}""";

    readonly string dir = Directory.CreateTempSubdirectory("nodlight-tail-").FullName;
    string P(string name = "t.jsonl") => Path.Combine(dir, name);

    public void Dispose() => Directory.Delete(dir, true);

    [Fact]
    public void Claude_interrupt_line_detected()
    {
        Assert.True(InterruptDetector.IsInterrupt(AgentKind.Claude, ClaudeInterrupt));
        var forTool = ClaudeInterrupt.Replace("by user]", "by user for tool use]");
        Assert.True(InterruptDetector.IsInterrupt(AgentKind.Claude, forTool));
        var asString = """{"type":"user","message":{"role":"user","content":"[Request interrupted by user]"}}""";
        Assert.True(InterruptDetector.IsInterrupt(AgentKind.Claude, asString));
    }

    [Fact]
    public void Codex_turn_aborted_detected()
    {
        Assert.True(InterruptDetector.IsInterrupt(AgentKind.Codex, CodexAborted));
    }

    [Fact]
    public void Ordinary_assistant_line_not_interrupt()
    {
        Assert.False(InterruptDetector.IsInterrupt(AgentKind.Claude, AssistantQuoting));
        Assert.False(InterruptDetector.IsInterrupt(AgentKind.Codex, AssistantQuoting));
        var codexMsg = """{"type":"response_item","payload":{"type":"message","role":"assistant","content":[{"type":"output_text","text":"turn_aborted"}]}}""";
        Assert.False(InterruptDetector.IsInterrupt(AgentKind.Codex, codexMsg));
        var userQuoting = """{"type":"user","message":{"role":"user","content":[{"type":"text","text":"why does it say [Request interrupted by user]?"}]}}""";
        Assert.False(InterruptDetector.IsInterrupt(AgentKind.Claude, userQuoting));
    }

    [Fact]
    public void Wrong_agent_or_bad_json_not_interrupt()
    {
        Assert.False(InterruptDetector.IsInterrupt(AgentKind.Codex, ClaudeInterrupt));
        Assert.False(InterruptDetector.IsInterrupt(AgentKind.Claude, CodexAborted));
        Assert.False(InterruptDetector.IsInterrupt(AgentKind.Claude, "{not json"));
        Assert.False(InterruptDetector.IsInterrupt(AgentKind.Codex, ""));
        Assert.False(InterruptDetector.IsInterrupt(AgentKind.Claude, "[1,2]"));
        Assert.False(InterruptDetector.IsInterrupt(AgentKind.Claude, """{"type":"user","message":{"content":[1,null]}}"""));
    }

    [Fact]
    public void Tail_starts_at_end()
    {
        File.WriteAllText(P(), "old1\nold2\n");
        var tail = new TranscriptTail(P());
        Assert.Empty(tail.ReadNewLines());
        Assert.Empty(tail.ReadNewLines());
    }

    [Fact]
    public void Tail_returns_appended_lines_only()
    {
        File.WriteAllText(P(), "old\n");
        var tail = new TranscriptTail(P());
        tail.ReadNewLines();
        File.AppendAllText(P(), "a\r\nb\n");
        Assert.Equal(new[] { "a", "b" }, tail.ReadNewLines());
        Assert.Empty(tail.ReadNewLines());
    }

    [Fact]
    public void Tail_holds_partial_line_until_newline()
    {
        File.WriteAllText(P(), "");
        var tail = new TranscriptTail(P());
        tail.ReadNewLines();
        File.AppendAllText(P(), "{\"a\":");
        Assert.Empty(tail.ReadNewLines());
        File.AppendAllText(P(), "1}\r");
        Assert.Empty(tail.ReadNewLines());
        File.AppendAllText(P(), "\nnext");
        Assert.Equal(new[] { "{\"a\":1}" }, tail.ReadNewLines());
        File.AppendAllText(P(), "\n");
        Assert.Equal(new[] { "next" }, tail.ReadNewLines());
    }

    [Fact]
    public void Tail_handles_multibyte_split_across_reads()
    {
        File.WriteAllText(P(), "");
        var tail = new TranscriptTail(P());
        tail.ReadNewLines();
        var bytes = Encoding.UTF8.GetBytes("é\n");
        using (var fs = new FileStream(P(), FileMode.Append)) fs.Write(bytes, 0, 1);
        Assert.Empty(tail.ReadNewLines());
        using (var fs = new FileStream(P(), FileMode.Append)) fs.Write(bytes, 1, bytes.Length - 1);
        Assert.Equal(new[] { "é" }, tail.ReadNewLines());
    }

    [Fact]
    public void Tail_missing_file_empty()
    {
        var tail = new TranscriptTail(P("nope.jsonl"));
        Assert.Empty(tail.ReadNewLines());
        Assert.Empty(tail.ReadNewLines());
    }

    [Fact]
    public void Tail_file_appearing_later_is_read_from_start()
    {
        var tail = new TranscriptTail(P("late.jsonl"));
        Assert.Empty(tail.ReadNewLines());
        File.WriteAllText(P("late.jsonl"), "first\n");
        Assert.Equal(new[] { "first" }, tail.ReadNewLines());
    }

    [Fact]
    public void Tail_restarts_from_beginning_when_file_shrinks()
    {
        File.WriteAllText(P(), "aaaaaaaaaa\nbbbbbbbbbb\n");
        var tail = new TranscriptTail(P());
        tail.ReadNewLines();
        File.WriteAllText(P(), "new\n");
        Assert.Equal(new[] { "new" }, tail.ReadNewLines());
    }

    [Fact]
    public void Tail_reads_while_writer_holds_file_open()
    {
        File.WriteAllText(P(), "");
        using var w = new FileStream(P(), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        var tail = new TranscriptTail(P());
        tail.ReadNewLines();
        w.Write("x\n"u8); w.Flush();
        Assert.Equal(new[] { "x" }, tail.ReadNewLines());
    }
}
