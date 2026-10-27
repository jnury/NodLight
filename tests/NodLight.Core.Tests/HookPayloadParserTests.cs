using NodLight.Core;

namespace NodLight.Core.Tests;

public class HookPayloadParserTests
{
    [Fact]
    public void Claude_notification()
    {
        var json = """{"session_id":"s1","cwd":"C:\\r\\webapp","transcript_path":"C:\\t.jsonl","message":"Claude needs your permission to use Bash","notification_type":"permission_prompt"}""";
        var p = HookPayloadParser.Parse(AgentKind.Claude, "Notification", json);
        Assert.Equal("s1", p.SessionId);
        Assert.Equal(@"C:\r\webapp", p.Cwd);
        Assert.Equal(@"C:\t.jsonl", p.TranscriptPath);
        Assert.Equal("Claude needs your permission to use Bash", p.Detail);
        Assert.Equal("permission_prompt", p.NotificationType);
    }

    [Fact]
    public void Codex_permission_request_builds_detail()
    {
        var p = HookPayloadParser.Parse(AgentKind.Codex, "PermissionRequest",
            """{"session_id":"s2","tool_name":"shell","tool_input":{"command":"npm test"}}""");
        Assert.Equal("s2", p.SessionId);
        Assert.Equal("Allow shell: npm test", p.Detail);
    }

    [Fact]
    public void Codex_command_as_array_is_joined_with_spaces()
    {
        var p = HookPayloadParser.Parse(AgentKind.Codex, "PermissionRequest",
            """{"tool_name":"shell","tool_input":{"command":["npm","run","test"]}}""");
        Assert.Equal("Allow shell: npm run test", p.Detail);
    }

    [Fact]
    public void Command_array_with_non_strings_or_empty_falls_back_to_tool_name()
    {
        Assert.Equal("Allow shell", HookPayloadParser.Parse(AgentKind.Codex, "PermissionRequest",
            """{"tool_name":"shell","tool_input":{"command":[]}}""").Detail);
        Assert.Equal("Allow shell: git status", HookPayloadParser.Parse(AgentKind.Codex, "PermissionRequest",
            """{"tool_name":"shell","tool_input":{"command":["git",1,null,"status"]}}""").Detail);
    }

    [Fact]
    public void Long_detail_truncated_to_80_chars_with_ellipsis()
    {
        var long_ = new string('x', 200);
        var p = HookPayloadParser.Parse(AgentKind.Claude, "Notification", $$"""{"message":"{{long_}}"}""");
        Assert.Equal(new string('x', 80) + "…", p.Detail);
    }

    [Fact]
    public void Tool_input_without_command_uses_tool_name_only()
    {
        var p = HookPayloadParser.Parse(AgentKind.Codex, "PermissionRequest",
            """{"tool_name":"apply_patch","tool_input":{"patch":"..."}}""");
        Assert.Equal("Allow apply_patch", p.Detail);
    }

    [Fact]
    public void Invalid_json_returns_nulls()
    {
        var p = HookPayloadParser.Parse(AgentKind.Claude, "Notification", "not json");
        Assert.Equal(new HookPayload(null, null, null, null, null), p);
    }
}
