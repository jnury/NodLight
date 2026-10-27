namespace NodLight.Core;

public static class StatusReducer
{
    /// <summary>Applies a hook event to a card. Returns null when the card should be removed.</summary>
    public static Card? Apply(Card? current, HookEvent e, long hwnd)
    {
        if (e.EventName == "SessionEnd") return null;

        // Claude re-fires SessionStart for the running session (e.g. after auto-compaction): not a new session.
        if (e.EventName == "SessionStart" && current is { SessionId: not null } && current.SessionId == e.SessionId)
            return current;

        (SessionStatus Status, WaitKind Wait, string? Detail)? target = e.EventName switch
        {
            "SessionStart" => (SessionStatus.Idle, WaitKind.None, null),
            "UserPromptSubmit" or "PreToolUse" or "PostToolUse" => (SessionStatus.Working, WaitKind.None, null),
            // Only prompts that need the user wait; null (older versions without the field) is treated as one.
            // Other types (idle_prompt, auth_success, ...) change nothing.
            "Notification" when e.NotificationType is null or "permission_prompt" or "elicitation_dialog"
                => (SessionStatus.Waiting, WaitKind.Permission, e.Detail),
            "Notification" => null,
            "PermissionRequest" => (SessionStatus.Waiting, WaitKind.Permission, e.Detail),
            "Stop" => (SessionStatus.Waiting, WaitKind.YourTurn, "Your turn"),
            _ => null,
        };

        if (target is null) return current;

        // A different session id in the same window means a new session: start fresh.
        // Only applies when the event actually produces a card; no-change events keep the original.
        if (current is { SessionId: not null } && e.SessionId is not null && current.SessionId != e.SessionId)
            current = null;

        var (status, wait, detail) = target.Value;
        var card = current ?? new Card(hwnd, e.Agent, null, null, null, status, wait, detail, e.At);
        var changed = card.Status != status || card.Wait != wait;

        return card with
        {
            Agent = e.Agent,
            SessionId = e.SessionId ?? card.SessionId,
            Cwd = e.Cwd ?? card.Cwd,
            TranscriptPath = e.TranscriptPath ?? card.TranscriptPath,
            Status = status,
            Wait = wait,
            Detail = detail,
            Since = changed ? e.At : card.Since,
        };
    }

    /// <summary>The user focused the window: an unread "your turn" becomes idle; permission waits persist.</summary>
    public static Card OnForeground(Card c, DateTimeOffset now) =>
        c is { Status: SessionStatus.Waiting, Wait: WaitKind.YourTurn }
            ? c with { Status = SessionStatus.Idle, Wait = WaitKind.None, Detail = null, Since = now }
            : c;

    /// <summary>The user interrupted the agent: back to idle.</summary>
    public static Card OnInterrupt(Card c, DateTimeOffset now) =>
        c is { Status: SessionStatus.Idle, Wait: WaitKind.None, Detail: null }
            ? c
            : c with { Status = SessionStatus.Idle, Wait = WaitKind.None, Detail = null, Since = now };
}
