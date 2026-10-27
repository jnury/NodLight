using NodLight.Core;

namespace NodLight.Core.Tests;

public class SessionStoreTests
{
    static readonly DateTimeOffset t0 = new(2026, 10, 27, 10, 0, 0, TimeSpan.Zero);

    static HookEvent Ev(string name, int minutes = 0, string? notif = null) =>
        new(AgentKind.Claude, name, null, null, null, null, notif, 1, t0.AddMinutes(minutes));

    [Fact]
    public void Orders_waiting_longest_first_then_working_then_idle()
    {
        var s = new SessionStore();
        s.Apply(Ev("SessionStart", 0), 1);       // idle, oldest
        s.Apply(Ev("SessionStart", 1), 2);       // idle, newer -> first among idle
        s.Apply(Ev("PreToolUse", 2), 3);         // working, newer
        s.Apply(Ev("PreToolUse", 1), 4);         // working, older -> first among working
        s.Apply(Ev("Stop", 4), 5);               // waiting, newer
        s.Apply(Ev("Stop", 3), 6);               // waiting, older -> first overall

        Assert.Equal(new long[] { 6, 5, 4, 3, 2, 1 }, s.Cards.Select(c => c.Hwnd).ToArray());
    }

    [Fact]
    public void WindowClosed_removes_card()
    {
        var s = new SessionStore();
        s.Apply(Ev("SessionStart"), 1);
        s.WindowClosed(1);
        Assert.Empty(s.Cards);
    }

    [Fact]
    public void Event_after_close_creates_fresh_card()
    {
        var s = new SessionStore();
        s.Apply(Ev("SessionStart", 0), 1);
        s.WindowClosed(1);
        s.Apply(Ev("PreToolUse", 5), 1);
        var c = Assert.Single(s.Cards);
        Assert.Equal(t0.AddMinutes(5), c.Since);
    }

    [Fact]
    public void SessionEnd_removes_card()
    {
        var s = new SessionStore();
        s.Apply(Ev("SessionStart"), 1);
        s.Apply(Ev("SessionEnd"), 1);
        Assert.Empty(s.Cards);
    }

    [Fact]
    public void Changed_raised_once_per_real_change()
    {
        var s = new SessionStore();
        var n = 0;
        s.Changed += () => n++;
        s.Apply(Ev("SessionStart"), 1);
        Assert.Equal(1, n);
        s.Apply(Ev("PreToolUse", 1), 1);
        Assert.Equal(2, n);
        s.Apply(Ev("PostToolUse", 2), 1); // already working: record-equal, no change
        Assert.Equal(2, n);
    }

    [Fact]
    public void Changed_not_raised_for_noop()
    {
        var s = new SessionStore();
        var n = 0;
        s.Changed += () => n++;
        s.Apply(Ev("Notification", notif: "idle_prompt"), 1); // no card, reducer ignores
        s.WindowClosed(1);                                       // nothing to remove
        s.Apply(Ev("SessionEnd"), 1);                            // removal of non-existent card
        Assert.Equal(0, n);
        Assert.Empty(s.Cards);
    }

    [Fact]
    public void Idle_prompt_on_existing_card_is_noop()
    {
        var s = new SessionStore();
        s.Apply(Ev("SessionStart"), 1);
        var n = 0;
        s.Changed += () => n++;
        s.Apply(Ev("Notification", 1, "idle_prompt"), 1);
        Assert.Equal(0, n);
    }

    [Fact]
    public void Foreground_on_unknown_hwnd_is_noop()
    {
        var s = new SessionStore();
        var n = 0;
        s.Changed += () => n++;
        s.Foreground(99, t0);
        Assert.Equal(0, n);
        Assert.Empty(s.Cards);
    }

    [Fact]
    public void Foreground_clears_your_turn_and_raises_changed()
    {
        var s = new SessionStore();
        s.Apply(Ev("Stop"), 1);
        var n = 0;
        s.Changed += () => n++;
        s.Foreground(1, t0.AddMinutes(1));
        Assert.Equal(1, n);
        Assert.Equal(SessionStatus.Idle, s.Cards[0].Status);
        s.Foreground(1, t0.AddMinutes(2)); // already idle
        Assert.Equal(1, n);
    }

    [Fact]
    public void Interrupted_goes_idle_and_unknown_or_idle_is_noop()
    {
        var s = new SessionStore();
        var n = 0;
        s.Changed += () => n++;
        s.Interrupted(99, t0);
        Assert.Equal(0, n);
        s.Apply(Ev("PreToolUse"), 1);
        Assert.Equal(1, n);
        s.Interrupted(1, t0.AddMinutes(1));
        Assert.Equal(2, n);
        Assert.Equal(SessionStatus.Idle, s.Cards[0].Status);
        s.Interrupted(1, t0.AddMinutes(2));
        Assert.Equal(2, n);
    }

    [Fact]
    public void Ties_are_ordered_by_hwnd()
    {
        var s = new SessionStore();
        s.Apply(Ev("PreToolUse"), 7);
        s.Apply(Ev("PreToolUse"), 3);
        Assert.Equal(new long[] { 3, 7 }, s.Cards.Select(c => c.Hwnd).ToArray());
    }
}
