using System.Text.Json;

namespace NodLight.Core;

public sealed record HookPayload(
    string? SessionId,
    string? Cwd,
    string? TranscriptPath,
    string? Detail,
    string? NotificationType);

public static class HookPayloadParser
{
    const int MaxDetail = 80;

    public static HookPayload Parse(AgentKind agent, string eventName, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new HookPayload(null, null, null, null, null);

            return new HookPayload(
                Str(root, "session_id"),
                Str(root, "cwd"),
                Str(root, "transcript_path"),
                Truncate(BuildDetail(eventName, root)),
                Str(root, "notification_type"));
        }
        catch (JsonException)
        {
            return new HookPayload(null, null, null, null, null);
        }
    }

    static string? BuildDetail(string eventName, JsonElement root)
    {
        switch (eventName)
        {
            case "Notification":
                return Str(root, "message");
            case "PermissionRequest":
                var tool = Str(root, "tool_name");
                if (tool is null) return null;
                string? command = null;
                if (root.TryGetProperty("tool_input", out var input) && input.ValueKind == JsonValueKind.Object)
                    command = Command(input);
                return string.IsNullOrEmpty(command) ? $"Allow {tool}" : $"Allow {tool}: {command}";
            default:
                return null;
        }
    }

    // Codex sends the command either as a string or as an argv array; an array's strings are joined with spaces.
    static string? Command(JsonElement input)
    {
        if (!input.TryGetProperty("command", out var v)) return null;
        if (v.ValueKind == JsonValueKind.String) return v.GetString();
        if (v.ValueKind != JsonValueKind.Array) return null;
        return string.Join(' ', v.EnumerateArray()
            .Where(a => a.ValueKind == JsonValueKind.String)
            .Select(a => a.GetString()));
    }

    static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static string? Truncate(string? s) =>
        s is { Length: > MaxDetail } ? s[..MaxDetail] + "…" : s;
}
