using System.Text;
using System.Text.Json;

namespace NodLight.Core;

public static class InterruptDetector
{
    const string ClaudeMarker = "[Request interrupted by user";
    const string CodexAbortType = "turn_aborted";

    public static bool IsInterrupt(AgentKind agent, string jsonLine)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonLine);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            return agent == AgentKind.Claude ? IsClaudeInterrupt(root) : IsCodexAbort(root);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Claude: {"type":"user","message":{"content":[{"type":"text","text":"[Request interrupted by user...]"}]}}
    // (content may also be a plain string). Only user messages count, and the text must START with the marker,
    // so assistant lines or user prompts that merely quote it are ignored.
    static bool IsClaudeInterrupt(JsonElement root)
    {
        if (Str(root, "type") != "user") return false;
        if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object) return false;
        if (!message.TryGetProperty("content", out var content)) return false;

        if (content.ValueKind == JsonValueKind.String)
            return StartsWithMarker(content.GetString());
        if (content.ValueKind != JsonValueKind.Array) return false;

        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object || Str(block, "type") != "text") continue;
            if (StartsWithMarker(Str(block, "text"))) return true;
        }
        return false;
    }

    // Codex: {"type":"event_msg","payload":{"type":"turn_aborted",...}}
    static bool IsCodexAbort(JsonElement root)
    {
        if (Str(root, "type") != "event_msg") return false;
        return root.TryGetProperty("payload", out var payload)
            && payload.ValueKind == JsonValueKind.Object
            && Str(payload, "type") == CodexAbortType;
    }

    static bool StartsWithMarker(string? s) =>
        s is not null && s.AsSpan().TrimStart().StartsWith(ClaudeMarker, StringComparison.Ordinal);

    static string? Str(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>
/// Follows an append-only transcript. The first call positions at the end of the file and returns nothing;
/// later calls return complete lines appended since. A trailing partial line is held until its newline arrives.
/// A missing file yields nothing; if it appears later it is read from its start. If the file shrinks
/// (truncated or replaced) reading restarts from the beginning. IO errors yield an empty list.
/// </summary>
public sealed class TranscriptTail(string path)
{
    bool started;
    long offset;
    byte[] pending = [];

    public IReadOnlyList<string> ReadNewLines()
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = fs.Length;

            if (!started)
            {
                started = true;
                offset = length;
                return [];
            }

            if (length < offset)
            {
                offset = 0;
                pending = [];
            }
            if (length == offset) return [];

            fs.Seek(offset, SeekOrigin.Begin);
            var buf = new byte[pending.Length + (int)Math.Min(length - offset, int.MaxValue - pending.Length)];
            pending.CopyTo(buf, 0);
            var read = 0;
            while (pending.Length + read < buf.Length)
            {
                var n = fs.Read(buf, pending.Length + read, buf.Length - pending.Length - read);
                if (n == 0) break;
                read += n;
            }
            offset += read;
            var total = pending.Length + read;

            var lines = new List<string>();
            var start = 0;
            for (var i = 0; i < total; i++)
            {
                if (buf[i] != (byte)'\n') continue;
                var end = i > start && buf[i - 1] == (byte)'\r' ? i - 1 : i;
                lines.Add(Encoding.UTF8.GetString(buf, start, end - start));
                start = i + 1;
            }
            pending = buf.AsSpan(start, total - start).ToArray();
            return lines;
        }
        catch (FileNotFoundException)
        {
            // Not there yet: once it appears, read it from the start.
            started = true;
            offset = 0;
            pending = [];
            return [];
        }
        catch (DirectoryNotFoundException)
        {
            started = true;
            offset = 0;
            pending = [];
            return [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
