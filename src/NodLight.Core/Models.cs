namespace NodLight.Core;

public enum AgentKind { Claude, Codex }

public enum SessionStatus { Working, Waiting, Idle }

public enum WaitKind { None, Permission, YourTurn }

public sealed record HookEvent(
    AgentKind Agent,
    string EventName,
    string? SessionId,
    string? Cwd,
    string? TranscriptPath,
    string? Detail,
    string? NotificationType,
    int AlacrittyPid,
    DateTimeOffset At);

public sealed record Card(
    long Hwnd,
    AgentKind Agent,
    string? SessionId,
    string? Cwd,
    string? TranscriptPath,
    SessionStatus Status,
    WaitKind Wait,
    string? Detail,
    DateTimeOffset Since);
