using System.Collections.ObjectModel;
using System.Diagnostics;
using NodLight.Core;

namespace NodLight.App;

/// <summary>
/// Drives the strip: feeds hook events, foreground changes, dead windows and transcript interrupts into the
/// <see cref="SessionStore"/>, and mirrors the store's ordered cards into <see cref="Cards"/>.
/// UI thread only (like the store).
/// </summary>
public sealed class DeckViewModel
{
    readonly SessionStore store;
    readonly WindowService windows;
    readonly Func<DateTimeOffset> clock;
    readonly Dictionary<string, TranscriptTail> tails = new(StringComparer.OrdinalIgnoreCase);

    // The Alacritty pid each card's window was found for; a window that no longer belongs to it is treated as gone.
    readonly Dictionary<long, int> pids = new();

    public ObservableCollection<CardViewModel> Cards { get; } = [];

    public DeckViewModel(SessionStore store, WindowService windows, Func<DateTimeOffset> clock)
    {
        this.store = store;
        this.windows = windows;
        this.clock = clock;
        store.Changed += Sync;
    }

    /// <summary>A hook event, already marshalled to the UI thread. Dropped when its terminal has no window.</summary>
    public void OnHookEvent(HookEvent e) => Guard("hook event", () =>
    {
        if (windows.FindMainWindow(e.AlacrittyPid) is not { } hwnd) return;
        pids[hwnd] = e.AlacrittyPid; // before Apply: the store's Changed handler (Sync) prunes pids without cards
        store.Apply(e, hwnd);
        // No foreground-change event fires when the event comes from the window the user is already in,
        // so a "your turn" there is read immediately.
        if (windows.IsForeground(hwnd)) store.Foreground(hwnd, clock());
    });

    /// <summary>Unknown windows (anything without a card) are ignored by the store.</summary>
    public void OnForeground(long hwnd) => Guard("foreground", () => store.Foreground(hwnd, clock()));

    public void Activate(CardViewModel card)
    {
        if (pids.TryGetValue(card.Hwnd, out var pid)) windows.Focus(card.Hwnd, pid);
    }

    /// <summary>
    /// Once a second: drop closed windows, check transcripts for interrupts, refresh titles and times.
    /// Each card (and each transcript) is guarded separately so one failure does not stall the others.
    /// </summary>
    public void Tick()
    {
        foreach (var card in store.Cards)
            Guard("window check", () =>
            {
                if (!pids.TryGetValue(card.Hwnd, out var pid) || !windows.IsAlive(card.Hwnd, pid))
                    store.WindowClosed(card.Hwnd);
            });

        PollTranscripts();
        Sync();
    }

    // One tail per transcript path, created on first sight (it starts at the current end of the file) and
    // dropped once no card references the path.
    void PollTranscripts()
    {
        var byPath = store.Cards
            .Where(c => !string.IsNullOrEmpty(c.TranscriptPath))
            .GroupBy(c => c.TranscriptPath!, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var stale in tails.Keys.Except(byPath.Select(g => g.Key), StringComparer.OrdinalIgnoreCase).ToList())
            tails.Remove(stale);

        foreach (var group in byPath)
            Guard("transcript poll", () =>
            {
                if (!tails.TryGetValue(group.Key, out var tail))
                    tails[group.Key] = tail = new TranscriptTail(group.Key);

                var lines = tail.ReadNewLines();
                if (lines.Count == 0) return;
                foreach (var card in group)
                    if (lines.Any(line => InterruptDetector.IsInterrupt(card.Agent, line)))
                        store.Interrupted(card.Hwnd, clock());
            });
    }

    // Reuses view models by hwnd so the list does not flicker, and moves them into the store's order.
    // Also runs as the store's Changed handler, so it must not throw back into the store's caller.
    void Sync() => Guard("sync", () =>
    {
        var now = clock();
        var ordered = store.Cards;
        var existing = Cards.ToDictionary(vm => vm.Hwnd);

        for (var i = 0; i < ordered.Count; i++)
        {
            var card = ordered[i];
            if (!existing.Remove(card.Hwnd, out var vm)) vm = new CardViewModel(card.Hwnd);
            Guard("card update", () => vm.Update(card, windows.GetTitle(card.Hwnd), now));

            var at = Cards.IndexOf(vm);
            if (at < 0) Cards.Insert(i, vm);
            else if (at != i) Cards.Move(at, i);
        }

        foreach (var gone in existing.Values) Cards.Remove(gone);

        var live = ordered.Select(c => c.Hwnd).ToHashSet();
        foreach (var hwnd in pids.Keys.Where(h => !live.Contains(h)).ToList()) pids.Remove(hwnd);
    });

    // The deck must survive a bug in one code path; failures go to the trace listeners instead of the user.
    static void Guard(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"NodLight: {what} failed: {ex}");
        }
    }
}
