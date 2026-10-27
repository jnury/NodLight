using NodLight.Core;

namespace NodLight.Core.Tests;

public class StatusReducerTests
{
    static readonly DateTimeOffset t0 = new(2026, 10, 27, 10, 0, 0, TimeSpan.Zero);
    const long hwnd = 42;

    static HookEvent Ev(string name, string? detail = null, string? notif = null, DateTimeOffset? at = null,
        AgentKind agent = AgentKind.Claude, string? session = null) =>
        new(agent, name, session, null, null, detail, notif, 1, at ?? t0);

    static Card CardOf(SessionStatus s, WaitKind w = WaitKind.None, string? detail = null, string? session = null) =>
        new(hwnd, AgentKind.Claude, session, null, null, s, w, detail, t0);

    [Fact]
    public void SessionStart_creates_idle_card() =>
        Assert.Equal(SessionStatus.Idle, StatusReducer.Apply(null, Ev("SessionStart"), 42)!.Status);

    [Theory, InlineData("UserPromptSubmit"), InlineData("PreToolUse"), InlineData("PostToolUse")]
    public void Activity_sets_working(string n) =>
        Assert.Equal(SessionStatus.Working, StatusReducer.Apply(null, Ev(n), 42)!.Status);

    [Fact]
    public void Permission_notification_waits_with_detail()
    {
        var c = StatusReducer.Apply(null, Ev("Notification", "Claude needs your permission to use Bash", "permission_prompt"), 42)!;
        Assert.Equal((SessionStatus.Waiting, WaitKind.Permission, "Claude needs your permission to use Bash"), (c.Status, c.Wait, c.Detail));
    }

    [Fact]
    public void Codex_PermissionRequest_waits()
    {
        var c = StatusReducer.Apply(null, Ev("PermissionRequest", "Allow shell: npm test", agent: AgentKind.Codex), 42)!;
        Assert.Equal((AgentKind.Codex, SessionStatus.Waiting, WaitKind.Permission, "Allow shell: npm test"),
            (c.Agent, c.Status, c.Wait, c.Detail));
    }

    [Fact]
    public void Stop_waits_your_turn()
    {
        var c = StatusReducer.Apply(null, Ev("Stop"), 42)!;
        Assert.Equal((SessionStatus.Waiting, WaitKind.YourTurn, "Your turn"), (c.Status, c.Wait, c.Detail));
    }

    [Fact]
    public void Idle_prompt_notification_changes_nothing()
    {
        var cur = CardOf(SessionStatus.Idle);
        var c = StatusReducer.Apply(cur, Ev("Notification", "waiting", "idle_prompt", t0.AddSeconds(60)), 42)!;
        Assert.Same(cur, c);
        Assert.Equal(t0, c.Since);
    }

    [Fact]
    public void Auth_success_notification_changes_nothing()
    {
        var cur = CardOf(SessionStatus.Working, session: "a");
        Assert.Same(cur, StatusReducer.Apply(cur, Ev("Notification", "Signed in", "auth_success", session: "a"), 42));
    }

    [Fact]
    public void Auth_success_notification_without_a_card_creates_none() =>
        Assert.Null(StatusReducer.Apply(null, Ev("Notification", "Signed in", "auth_success"), 42));

    [Fact]
    public void Elicitation_dialog_notification_waits_with_detail()
    {
        var c = StatusReducer.Apply(CardOf(SessionStatus.Working), Ev("Notification", "Pick an option", "elicitation_dialog"), 42)!;
        Assert.Equal((SessionStatus.Waiting, WaitKind.Permission, "Pick an option"), (c.Status, c.Wait, c.Detail));
    }

    [Fact]
    public void Notification_without_type_waits_for_older_versions()
    {
        var c = StatusReducer.Apply(CardOf(SessionStatus.Working), Ev("Notification", "Claude needs your permission"), 42)!;
        Assert.Equal((SessionStatus.Waiting, WaitKind.Permission, "Claude needs your permission"), (c.Status, c.Wait, c.Detail));
    }

    [Fact]
    public void SessionStart_for_the_same_session_changes_nothing()
    {
        // Claude fires SessionStart (source "compact") after auto-compaction, mid-turn.
        var cur = CardOf(SessionStatus.Working, session: "a");
        Assert.Same(cur, StatusReducer.Apply(cur, Ev("SessionStart", session: "a", at: t0.AddMinutes(3)), 42));

        var waiting = CardOf(SessionStatus.Waiting, WaitKind.Permission, "x", session: "a");
        Assert.Same(waiting, StatusReducer.Apply(waiting, Ev("SessionStart", session: "a"), 42));
    }

    [Fact]
    public void SessionStart_without_session_ids_still_resets_to_idle()
    {
        var c = StatusReducer.Apply(CardOf(SessionStatus.Working), Ev("SessionStart", at: t0.AddMinutes(1)), 42)!;
        Assert.Equal((SessionStatus.Idle, t0.AddMinutes(1)), (c.Status, c.Since));
    }

    [Fact]
    public void SessionEnd_removes_card() =>
        Assert.Null(StatusReducer.Apply(CardOf(SessionStatus.Idle), Ev("SessionEnd"), 42));

    [Fact]
    public void Since_only_resets_on_status_change()
    {
        var w = StatusReducer.Apply(null, Ev("PreToolUse"), 42)!;
        var c = StatusReducer.Apply(w, Ev("PreToolUse", at: t0.AddSeconds(30)), 42)!;
        Assert.Equal(t0, c.Since);
    }

    [Fact]
    public void New_session_id_replaces_card()
    {
        var cur = CardOf(SessionStatus.Working, session: "a");
        var c = StatusReducer.Apply(cur, Ev("SessionStart", session: "b"), 42)!;
        Assert.Equal(("b", SessionStatus.Idle), (c.SessionId, c.Status));
    }

    [Fact]
    public void Foreground_turns_your_turn_into_idle()
    {
        var now = t0.AddMinutes(5);
        var c = StatusReducer.OnForeground(CardOf(SessionStatus.Waiting, WaitKind.YourTurn, "Your turn"), now);
        Assert.Equal((SessionStatus.Idle, WaitKind.None, now), (c.Status, c.Wait, c.Since));
    }

    [Fact]
    public void Foreground_keeps_permission_waiting()
    {
        var cur = CardOf(SessionStatus.Waiting, WaitKind.Permission, "x");
        Assert.Equal(cur, StatusReducer.OnForeground(cur, t0.AddMinutes(5)));
    }

    [Fact]
    public void Interrupt_sets_idle()
    {
        var c = StatusReducer.OnInterrupt(CardOf(SessionStatus.Working, detail: "Bash"), t0.AddSeconds(5));
        Assert.Equal((SessionStatus.Idle, WaitKind.None, (string?)null, t0.AddSeconds(5)), (c.Status, c.Wait, c.Detail, c.Since));
    }

    [Fact]
    public void Unknown_event_with_different_session_keeps_original_card()
    {
        var cur = CardOf(SessionStatus.Working, session: "b");
        Assert.Same(cur, StatusReducer.Apply(cur, Ev("PreCompact", session: "a"), 42));
    }

    [Fact]
    public void Idle_prompt_with_different_session_keeps_original_card()
    {
        var cur = CardOf(SessionStatus.Idle, session: "b");
        Assert.Same(cur, StatusReducer.Apply(cur, Ev("Notification", "x", "idle_prompt", session: "a"), 42));
    }

    [Fact]
    public void Unknown_event_changes_nothing()
    {
        var cur = CardOf(SessionStatus.Working);
        Assert.Same(cur, StatusReducer.Apply(cur, Ev("PreCompact"), 42));
    }
}
