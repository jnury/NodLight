using System.Text.Json.Nodes;
using NodLight.Core;

namespace NodLight.Core.Tests;

public sealed class HookInstallerTests : IDisposable
{
    const string Helper = @"C:\Apps\NodLight\nodlight-hook.exe";
    const string HelperFwd = "C:/Apps/NodLight/nodlight-hook.exe";

    readonly string dir = Path.Combine(Path.GetTempPath(), "nodlight-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }

    string TempFile(string? content = null)
    {
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, "settings.json");
        if (content is not null) File.WriteAllText(p, content);
        return p;
    }

    static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();

    static string Command(JsonObject root, string ev, int group = 0) =>
        (string)root["hooks"]![ev]![group]!["hooks"]![0]!["command"]!;

    const string UserStop = """{"model":"opus","hooks":{"Stop":[{"hooks":[{"type":"command","command":"my-stop.sh"}]}]}}""";

    [Fact]
    public void Merge_into_empty_creates_all_events()
    {
        var root = Parse(HookInstaller.Merge(null, AgentKind.Claude, Helper));
        Assert.Equal(7, HookInstaller.ClaudeEvents.Length);
        foreach (var ev in HookInstaller.ClaudeEvents)
        {
            Assert.Equal($"\"{HelperFwd}\" claude {ev}", Command(root, ev));
            var hook = root["hooks"]![ev]![0]!["hooks"]![0]!;
            Assert.Equal("command", (string)hook["type"]!);
            Assert.Equal(5, (int)hook["timeout"]!);
        }
    }

    [Fact]
    public void Matcher_only_on_tool_and_permission_events()
    {
        var root = Parse(HookInstaller.Merge(null, AgentKind.Codex, Helper));
        foreach (var ev in HookInstaller.CodexEvents)
        {
            var expected = ev is "PreToolUse" or "PostToolUse" or "PermissionRequest";
            var group = root["hooks"]![ev]![0]!.AsObject();
            Assert.Equal(expected, group.ContainsKey("matcher"));
            if (expected) Assert.Equal("*", (string)group["matcher"]!);
        }
    }

    [Theory, InlineData(null), InlineData(""), InlineData("  \r\n\t ")]
    public void Merge_null_empty_or_whitespace_is_new_config(string? existing)
    {
        var root = Parse(HookInstaller.Merge(existing, AgentKind.Claude, Helper));
        Assert.Equal(7, root["hooks"]!.AsObject().Count);
    }

    [Fact]
    public void Merge_preserves_other_keys_and_hooks()
    {
        var root = Parse(HookInstaller.Merge(UserStop, AgentKind.Claude, Helper));
        Assert.Equal("opus", (string)root["model"]!);
        var stop = root["hooks"]!["Stop"]!.AsArray();
        Assert.Equal(2, stop.Count);
        Assert.Equal("my-stop.sh", (string)stop[0]!["hooks"]![0]!["command"]!);
        Assert.Equal($"\"{HelperFwd}\" claude Stop", Command(root, "Stop", 1));
    }

    [Fact]
    public void Merge_twice_is_noop()
    {
        var once = HookInstaller.Merge(null, AgentKind.Claude, Helper);
        Assert.Equal(once, HookInstaller.Merge(once, AgentKind.Claude, Helper));
    }

    [Fact]
    public void Merge_output_keeps_quotes_readable()
    {
        Assert.Contains("\\\"C:/Apps/NodLight/nodlight-hook.exe\\\" claude Stop",
            HookInstaller.Merge(null, AgentKind.Claude, Helper));
    }

    [Fact]
    public void Codex_merge_uses_codex_events_and_agent_arg()
    {
        var root = Parse(HookInstaller.Merge(null, AgentKind.Codex, Helper));
        Assert.Equal(6, root["hooks"]!.AsObject().Count);
        foreach (var ev in HookInstaller.CodexEvents)
            Assert.EndsWith($" codex {ev}", Command(root, ev));
        Assert.EndsWith("codex PermissionRequest", Command(root, "PermissionRequest"));
        Assert.Null(root["hooks"]!["SessionEnd"]);
    }

    [Fact]
    public void Unmerge_removes_only_nodlight()
    {
        var merged = HookInstaller.Merge(UserStop, AgentKind.Claude, Helper);
        var root = Parse(HookInstaller.Unmerge(merged));
        Assert.Equal("opus", (string)root["model"]!);
        var hooks = root["hooks"]!.AsObject();
        Assert.Single(hooks);
        var stop = hooks["Stop"]!.AsArray();
        Assert.Single(stop);
        Assert.Equal("my-stop.sh", (string)stop[0]!["hooks"]![0]!["command"]!);
    }

    [Fact]
    public void Unmerge_without_nodlight_entries_is_noop_and_keeps_other_events()
    {
        var existing = """{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"a"}]}],"Custom":[],"Notification":[{"hooks":[{"type":"command","command":"b"}]}]}}""";
        var result = HookInstaller.Unmerge(existing);
        Assert.Equal(existing, result);
        Assert.Equal(3, Parse(result)["hooks"]!.AsObject().Count);
    }

    const string Mixed = """{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"my.sh"},{"type":"command","command":"\"C:/old/nodlight-hook.exe\" claude Stop","timeout":5},{"type":"command","command":"after.sh"}]}]}}""";

    [Fact]
    public void Merge_on_mixed_group_refreshes_only_nodlight_entry_in_place()
    {
        var merged = HookInstaller.Merge(Mixed, AgentKind.Claude, Helper);
        var stop = Parse(merged)["hooks"]!["Stop"]!.AsArray();
        Assert.Single(stop);
        var inner = stop[0]!["hooks"]!.AsArray();
        Assert.Equal(3, inner.Count);
        Assert.Equal("my.sh", (string)inner[0]!["command"]!);
        Assert.Equal($"\"{HelperFwd}\" claude Stop", (string)inner[1]!["command"]!);
        Assert.Equal("after.sh", (string)inner[2]!["command"]!);
        Assert.Equal(merged, HookInstaller.Merge(merged, AgentKind.Claude, Helper));
    }

    [Fact]
    public void Unmerge_on_mixed_group_keeps_user_hooks_and_their_order()
    {
        var root = Parse(HookInstaller.Unmerge(Mixed));
        var stop = root["hooks"]!["Stop"]!.AsArray();
        Assert.Single(stop);
        var inner = stop[0]!["hooks"]!.AsArray();
        Assert.Equal(["my.sh", "after.sh"], inner.Select(h => (string)h!["command"]!));
    }

    [Fact]
    public void Unmerge_and_IsInstalled_ignore_null_and_non_array_values_under_unrelated_events()
    {
        var merged = Parse(HookInstaller.Merge(null, AgentKind.Claude, Helper));
        merged["hooks"]!["Foo"] = null;
        merged["hooks"]!["Bar"] = new JsonObject { ["x"] = 1 };
        merged["hooks"]!["Baz"] = "str";
        var json = merged.ToJsonString();
        Assert.True(HookInstaller.IsInstalled(json, AgentKind.Claude));

        var hooks = Parse(HookInstaller.Unmerge(json))["hooks"]!.AsObject();
        Assert.Equal(3, hooks.Count);
        Assert.Null(hooks["Foo"]);
        Assert.True(hooks.ContainsKey("Foo"));
        Assert.Equal(1, (int)hooks["Bar"]!["x"]!);
        Assert.Equal("str", (string)hooks["Baz"]!);
    }

    [Fact]
    public void Unmerge_everything_nodlight_leaves_no_empty_event_arrays()
    {
        var root = Parse(HookInstaller.Unmerge(HookInstaller.Merge(null, AgentKind.Claude, Helper)));
        Assert.True(!root.ContainsKey("hooks") || root["hooks"]!.AsObject().Count == 0);
    }

    [Theory]
    [InlineData("{ \"a\": ")]
    [InlineData("{ // c\n \"a\": 1 }")]
    [InlineData("{ /* c */ \"a\": 1 }")]
    [InlineData("{ \"a\": 1, }")]
    [InlineData("{ \"a\": 1 } {}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"hooks\":[]}")]
    [InlineData("{\"hooks\":{\"Stop\":{}}}")]
    public void Merge_refuses_bad_input_and_file_stays_byte_identical(string bad)
    {
        Assert.Throws<InvalidDataException>(() => HookInstaller.Merge(bad, AgentKind.Claude, Helper));
        var path = TempFile(bad);
        var before = File.ReadAllBytes(path);
        Assert.Throws<InvalidDataException>(() => HookInstaller.InstallFile(path, AgentKind.Claude, Helper));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".nodlight.bak"));
    }

    [Fact]
    public void Unmerge_refuses_bad_input()
    {
        Assert.Throws<InvalidDataException>(() => HookInstaller.Unmerge("[]"));
        Assert.Throws<InvalidDataException>(() => HookInstaller.Unmerge("{ // c\n }"));
    }

    [Fact]
    public void InstallFile_on_missing_file_creates_parent_and_file_without_backup()
    {
        var path = Path.Combine(dir, "nested", "deeper", "settings.json");
        HookInstaller.InstallFile(path, AgentKind.Claude, Helper);
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".nodlight.bak"));
        Assert.True(HookInstaller.IsInstalled(File.ReadAllText(path), AgentKind.Claude));
    }

    [Fact]
    public void InstallFile_writes_backup_once()
    {
        const string original = """{"model":"opus"}""";
        var path = TempFile(original);
        HookInstaller.InstallFile(path, AgentKind.Claude, Helper);
        var bak = path + ".nodlight.bak";
        Assert.Equal(original, File.ReadAllText(bak));

        HookInstaller.InstallFile(path, AgentKind.Claude, Helper + "x");
        Assert.Equal(original, File.ReadAllText(bak));
    }

    [Fact]
    public void IsInstalled_true_only_when_every_event_of_that_agent_has_a_nodlight_entry()
    {
        Assert.False(HookInstaller.IsInstalled(null, AgentKind.Claude));
        Assert.False(HookInstaller.IsInstalled("", AgentKind.Claude));
        Assert.False(HookInstaller.IsInstalled("not json", AgentKind.Claude));
        Assert.False(HookInstaller.IsInstalled("{}", AgentKind.Claude));

        var claude = HookInstaller.Merge(null, AgentKind.Claude, Helper);
        Assert.True(HookInstaller.IsInstalled(claude, AgentKind.Claude));
        // Codex needs PermissionRequest, which a Claude config lacks.
        Assert.False(HookInstaller.IsInstalled(claude, AgentKind.Codex));

        var partial = Parse(claude);
        partial["hooks"]!.AsObject().Remove("SessionEnd");
        Assert.False(HookInstaller.IsInstalled(partial.ToJsonString(), AgentKind.Claude));

        var userOnly = """{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"x"}]}]}}""";
        Assert.False(HookInstaller.IsInstalled(userOnly, AgentKind.Claude));

        Assert.True(HookInstaller.IsInstalled(HookInstaller.Merge(null, AgentKind.Codex, Helper), AgentKind.Codex));
    }

    [Fact]
    public void UninstallFile_removes_nodlight_and_missing_file_does_not_throw()
    {
        HookInstaller.UninstallFile(Path.Combine(dir, "missing", "settings.json"));

        var path = TempFile("""{"model":"opus"}""");
        HookInstaller.InstallFile(path, AgentKind.Claude, Helper);
        HookInstaller.UninstallFile(path);
        var text = File.ReadAllText(path);
        Assert.False(HookInstaller.IsInstalled(text, AgentKind.Claude));
        Assert.Equal("opus", (string)Parse(text)["model"]!);
    }
}
