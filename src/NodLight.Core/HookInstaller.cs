using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NodLight.Core;

/// <summary>
/// Adds and removes NodLight hook groups in Claude (settings.json) and Codex (hooks.json) config files.
/// NodLight only appends its own group to each event array and never edits groups it did not write.
/// </summary>
public static class HookInstaller
{
    public static readonly string[] ClaudeEvents =
        ["SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "Notification", "Stop", "SessionEnd"];

    public static readonly string[] CodexEvents =
        ["SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "PermissionRequest", "Stop"];

    const string Marker = "nodlight-hook";

    static readonly JsonSerializerOptions Output = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    static string[] EventsFor(AgentKind agent) => agent == AgentKind.Claude ? ClaudeEvents : CodexEvents;

    public static string Merge(string? existingJson, AgentKind agent, string helperPath)
    {
        var root = ParseRoot(existingJson);
        var hooks = HooksOf(root, create: true)!;
        var helper = helperPath.Replace('\\', '/');
        var agentArg = agent == AgentKind.Claude ? "claude" : "codex";

        foreach (var ev in EventsFor(agent))
        {
            var group = NewGroup(ev, $"\"{helper}\" {agentArg} {ev}");
            var array = EventArray(hooks, ev, create: true)!;
            var idx = IndexOfNodLightGroup(array);
            if (idx >= 0) array[idx] = group; // refresh in place so repeated merges are stable
            else array.Add(group);
        }

        return root.ToJsonString(Output);
    }

    public static string Unmerge(string existingJson)
    {
        var root = ParseRoot(existingJson);
        var hooks = HooksOf(root, create: false);
        if (hooks is null) return existingJson;

        var changed = false;
        foreach (var ev in hooks.Select(p => p.Key).ToList())
        {
            var array = EventArray(hooks, ev, create: false)!;
            var before = array.Count;
            for (var i = array.Count - 1; i >= 0; i--)
                if (IsNodLightGroup(array[i])) array.RemoveAt(i);
            if (array.Count == before) continue;
            changed = true;
            if (array.Count == 0) hooks.Remove(ev); // only drop arrays we emptied, never the user's own
        }

        if (!changed) return existingJson;
        if (hooks.Count == 0) root.Remove("hooks");
        return root.ToJsonString(Output);
    }

    /// <summary>True only when every event of <paramref name="agent"/> has a nodlight-hook entry.</summary>
    public static bool IsInstalled(string? json, AgentKind agent)
    {
        JsonObject? hooks;
        try { hooks = HooksOf(ParseRoot(json), create: false); }
        catch (InvalidDataException) { return false; }
        if (hooks is null) return false;

        foreach (var ev in EventsFor(agent))
        {
            if (!hooks.TryGetPropertyValue(ev, out var node) || node is not JsonArray array
                || IndexOfNodLightGroup(array) < 0)
                return false;
        }
        return true;
    }

    public static void InstallFile(string path, AgentKind agent, string helperPath)
    {
        var exists = File.Exists(path);
        var merged = Merge(exists ? File.ReadAllText(path) : null, agent, helperPath); // throws before any write

        if (exists)
        {
            var bak = path + ".nodlight.bak";
            if (!File.Exists(bak)) File.Copy(path, bak);
        }
        else if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } parent)
        {
            Directory.CreateDirectory(parent);
        }

        File.WriteAllText(path, merged, new UTF8Encoding(false));
    }

    public static void UninstallFile(string path)
    {
        if (!File.Exists(path)) return;
        var text = File.ReadAllText(path);
        var result = Unmerge(text);
        if (result != text) File.WriteAllText(path, result, new UTF8Encoding(false));
    }

    static JsonObject ParseRoot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();

        JsonNode? node;
        try
        {
            // Default (strict) document options: no comments, no trailing commas, no trailing data.
            node = JsonNode.Parse(json);
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            throw new InvalidDataException("Existing hook config is not valid JSON: " + e.Message, e);
        }

        return node as JsonObject
            ?? throw new InvalidDataException("Existing hook config root must be a JSON object.");
    }

    static JsonObject? HooksOf(JsonObject root, bool create)
    {
        if (!root.TryGetPropertyValue("hooks", out var node) || node is null)
        {
            if (!create) return null;
            var made = new JsonObject();
            root["hooks"] = made;
            return made;
        }
        return node as JsonObject
            ?? throw new InvalidDataException("\"hooks\" in the existing config must be a JSON object.");
    }

    static JsonArray? EventArray(JsonObject hooks, string ev, bool create)
    {
        if (!hooks.TryGetPropertyValue(ev, out var node) || node is null)
        {
            if (!create) return null;
            var made = new JsonArray();
            hooks[ev] = made;
            return made;
        }
        return node as JsonArray
            ?? throw new InvalidDataException($"\"hooks.{ev}\" in the existing config must be a JSON array.");
    }

    static JsonObject NewGroup(string ev, string command)
    {
        var group = new JsonObject();
        if (ev is "PreToolUse" or "PostToolUse" or "PermissionRequest") group["matcher"] = "*";
        group["hooks"] = new JsonArray(new JsonObject
        {
            ["type"] = "command",
            ["command"] = command,
            ["timeout"] = 5,
        });
        return group;
    }

    static int IndexOfNodLightGroup(JsonArray array)
    {
        for (var i = 0; i < array.Count; i++)
            if (IsNodLightGroup(array[i])) return i;
        return -1;
    }

    static bool IsNodLightGroup(JsonNode? group) =>
        group is JsonObject g && g["hooks"] is JsonArray inner && inner.Any(h =>
            h is JsonObject o && o["command"] is JsonValue v && v.TryGetValue<string>(out var cmd)
            && cmd.Contains(Marker, StringComparison.OrdinalIgnoreCase));
}
