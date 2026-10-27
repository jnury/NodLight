using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NodLight.Core;

/// <summary>
/// Adds and removes NodLight hook groups in Claude (settings.json) and Codex (hooks.json) config files.
/// NodLight works at hook-entry granularity: it appends its own group to an event array when no nodlight-hook entry
/// exists, refreshes or removes only its own entries otherwise, and leaves every other entry and group untouched.
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

    // Anything a shell could read as syntax. Whitespace plus quotes, operators, expansion and glob characters.
    const string ShellSpecial = "\"'`&|;()<>^%$[]{},@#!~=*?";

    /// <summary>
    /// The hook command for one event: <c>{helper} {agent} {event}</c> with the helper as a forward-slash path.
    /// A plain path is left unquoted because that form runs under pwsh, cmd and Git Bash alike; pwsh cannot run a
    /// quoted path without a leading call operator (<c>&amp;</c>), which cmd and bash reject. A path with whitespace
    /// or shell metacharacters must be quoted, so it falls back to <c>"{helper}" {agent} {event}</c>, which works in
    /// cmd and Git Bash but not when the agent runs hooks through PowerShell.
    /// </summary>
    public static string CommandFor(string helperPath, AgentKind agent, string ev)
    {
        var helper = helperPath.Replace('\\', '/');
        var agentArg = agent == AgentKind.Claude ? "claude" : "codex";
        var plain = helper.Length > 0 && !helper.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || ShellSpecial.Contains(c));
        return plain ? $"{helper} {agentArg} {ev}" : $"\"{helper}\" {agentArg} {ev}";
    }

    public static string Merge(string? existingJson, AgentKind agent, string helperPath)
    {
        var root = ParseRoot(existingJson);
        var hooks = HooksOf(root, create: true)!;

        foreach (var ev in EventsFor(agent))
        {
            var command = CommandFor(helperPath, agent, ev);
            var array = EventArray(hooks, ev, create: true)!;
            var entry = FindNodLightEntry(array);
            if (entry is null)
            {
                array.Add(NewGroup(ev, command));
            }
            else
            {
                // refresh in place so repeated merges are stable and neighbouring entries stay untouched
                entry["type"] = "command";
                entry["command"] = command;
                entry["timeout"] = 5;
            }
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
            if (hooks[ev] is not JsonArray array) continue; // not ours: never throw on unrelated values
            var removed = false;
            for (var i = array.Count - 1; i >= 0; i--)
            {
                if (array[i] is not JsonObject group || group["hooks"] is not JsonArray inner) continue;
                for (var j = inner.Count - 1; j >= 0; j--)
                {
                    if (!IsNodLightEntry(inner[j])) continue;
                    inner.RemoveAt(j);
                    removed = true;
                    if (inner.Count == 0) array.RemoveAt(i); // drop the group only if we emptied it
                }
            }
            if (!removed) continue;
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
                || FindNodLightEntry(array) is null)
                return false;
        }
        return true;
    }

    /// <summary>
    /// True only when every event of <paramref name="agent"/> has a nodlight-hook entry whose command is exactly what
    /// <see cref="Merge"/> would write for <paramref name="helperPath"/>. False for a stale entry (another helper path,
    /// or the older quoted form), so the next install refreshes it.
    /// </summary>
    public static bool IsCurrent(string? json, AgentKind agent, string helperPath)
    {
        JsonObject? hooks;
        try { hooks = HooksOf(ParseRoot(json), create: false); }
        catch (InvalidDataException) { return false; }
        if (hooks is null) return false;

        foreach (var ev in EventsFor(agent))
        {
            if (!hooks.TryGetPropertyValue(ev, out var node) || node is not JsonArray array
                || FindNodLightEntry(array) is not { } entry
                || entry["command"]?.GetValue<string>() != CommandFor(helperPath, agent, ev))
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

        WriteAtomic(path, merged);
    }

    public static void UninstallFile(string path)
    {
        if (!File.Exists(path)) return;
        var text = File.ReadAllText(path);
        var result = Unmerge(text);
        if (result != text) WriteAtomic(path, result);
    }

    /// <summary>
    /// Writes beside the target and moves over it, so a crash mid-write never leaves a truncated config.
    /// A symlinked config (dotfile repos) is resolved first so the link itself is kept and its target updated.
    /// </summary>
    static void WriteAtomic(string path, string content)
    {
        var info = new FileInfo(path);
        var target = info.LinkTarget is null ? path : info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
        var tmp = target + ".nodlight.tmp";
        try
        {
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            File.Move(tmp, target, overwrite: true);
        }
        finally
        {
            try { File.Delete(tmp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
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

    static JsonObject? FindNodLightEntry(JsonArray eventArray)
    {
        foreach (var group in eventArray)
            if (group is JsonObject g && g["hooks"] is JsonArray inner)
                foreach (var entry in inner)
                    if (IsNodLightEntry(entry)) return (JsonObject)entry!;
        return null;
    }

    static bool IsNodLightEntry(JsonNode? entry) =>
        entry is JsonObject o && o["command"] is JsonValue v && v.TryGetValue<string>(out var cmd)
        && cmd.Contains(Marker, StringComparison.OrdinalIgnoreCase);
}
