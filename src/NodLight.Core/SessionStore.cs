namespace NodLight.Core;

/// <summary>
/// Holds one <see cref="Card"/> per terminal window (keyed by hwnd).
/// Not thread-safe: all calls, including the <see cref="Changed"/> handlers, happen on the UI thread; no locking is needed.
/// </summary>
public sealed class SessionStore
{
    readonly Dictionary<long, Card> cards = new();

    /// <summary>Raised once per mutating call that actually changed state.</summary>
    public event Action? Changed;

    /// <summary>
    /// Sorted snapshot: Waiting (oldest Since first), then Working (oldest first), then Idle (newest first).
    /// Ties are broken by Hwnd ascending so the order is deterministic.
    /// </summary>
    public IReadOnlyList<Card> Cards =>
        cards.Values
            .OrderBy(c => c.Status switch { SessionStatus.Waiting => 0, SessionStatus.Working => 1, _ => 2 })
            .ThenBy(c => c.Status == SessionStatus.Idle ? -c.Since.UtcTicks : c.Since.UtcTicks)
            .ThenBy(c => c.Hwnd)
            .ToList();

    public void Apply(HookEvent e, long hwnd)
    {
        cards.TryGetValue(hwnd, out var current);
        Set(hwnd, current, StatusReducer.Apply(current, e, hwnd));
    }

    /// <summary>
    /// Adds an Idle card for a session found running at startup, before any hook event. A window that already has a
    /// card is left alone: hook events are the more accurate source.
    /// </summary>
    public void Discover(long hwnd, AgentKind agent, DateTimeOffset now, string? cwd = null)
    {
        if (cards.ContainsKey(hwnd)) return;
        Set(hwnd, null, new Card(hwnd, agent, null, cwd, null, SessionStatus.Idle, WaitKind.None, null, now));
    }

    public void WindowClosed(long hwnd)
    {
        if (cards.Remove(hwnd)) Changed?.Invoke();
    }

    public void Foreground(long hwnd, DateTimeOffset now)
    {
        if (cards.TryGetValue(hwnd, out var c)) Set(hwnd, c, StatusReducer.OnForeground(c, now));
    }

    public void TitleWorking(long hwnd, DateTimeOffset now)
    {
        if (cards.TryGetValue(hwnd, out var c)) Set(hwnd, c, StatusReducer.OnTitleWorking(c, now));
    }

    public void Interrupted(long hwnd, DateTimeOffset now)
    {
        if (cards.TryGetValue(hwnd, out var c)) Set(hwnd, c, StatusReducer.OnInterrupt(c, now));
    }

    // Records compare by value, so a reducer returning an equal card is a no-op.
    void Set(long hwnd, Card? before, Card? after)
    {
        if (Equals(before, after)) return;
        if (after is null) cards.Remove(hwnd);
        else cards[hwnd] = after;
        Changed?.Invoke();
    }
}
