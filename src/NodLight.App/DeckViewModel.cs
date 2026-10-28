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
    readonly ResourceMeter meter = new(Environment.ProcessorCount);
    readonly Dictionary<string, TranscriptTail> tails = new(StringComparer.OrdinalIgnoreCase);

    // Windows whose card was set to Working because the title shows the working spinner (not because of a hook).
    readonly HashSet<long> workingFromTitle = [];

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
        workingFromTitle.Remove(hwnd); // a hook event is more authoritative than the title
        pids[hwnd] = e.AlacrittyPid; // before Apply: the store's Changed handler (Sync) prunes pids without cards
        store.Apply(e, hwnd);
        // No foreground-change event fires when the event comes from the window the user is already in,
        // so a "your turn" there is read immediately.
        if (windows.IsForeground(hwnd)) store.Foreground(hwnd, clock());
    });

    /// <summary>
    /// Startup scan: adds an Idle card for each agent already running in an Alacritty window. The real status is
    /// unknown until the session's next hook event, which then takes over the card.
    /// </summary>
    public void DiscoverExisting(Func<IReadOnlyList<DiscoveredSession>> find, Func<int, string?> cwdOf) =>
        Guard("discovery", () =>
        {
            foreach (var session in find())
            {
                if (windows.FindMainWindow(session.AlacrittyPid) is not { } hwnd) continue;
                pids[hwnd] = session.AlacrittyPid;
                store.Discover(hwnd, session.Agent, clock(), cwdOf(session.AgentPid));
            }
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

        foreach (var card in store.Cards) Guard("title status", () => ApplyTitleStatus(card));

        PollTranscripts();
        Sync();
    }

    // The agent's title spinner is a second status signal, for what hooks miss (subagents, background work, sessions
    // found at startup). While it shows an Idle card as working the card is Working; once the spinner stops, a card
    // that was only Working because of it goes back to Idle.
    void ApplyTitleStatus(Card card)
    {
        var state = WindowTitle.Parse(windows.GetTitle(card.Hwnd)).State;
        if (state == TitleState.Working)
        {
            if (card.Status != SessionStatus.Idle) return;
            workingFromTitle.Add(card.Hwnd);
            store.TitleWorking(card.Hwnd, clock());
        }
        else if (workingFromTitle.Remove(card.Hwnd) && card.Status == SessionStatus.Working)
        {
            store.Interrupted(card.Hwnd, clock());
        }
    }

    /// <summary>Totals over all cards, for the taskbar thumbnail.</summary>
    public DashboardSummary Summarize() =>
        DashboardSummary.From(Cards.Select(vm => vm.Status));

    /// <summary>
    /// Every couple of seconds: measures CPU, memory and I/O of each card's window (all processes below its Alacritty
    /// process) and hands the result to the card. <paramref name="collect"/> maps root pids to their processes' counters.
    /// </summary>
    public void MeasureResources(Func<IReadOnlyList<int>, IReadOnlyDictionary<int, IReadOnlyList<ProcessCounters>>> collect) =>
        Guard("resource sample", () =>
        {
            var byRoot = Cards
                .Where(vm => pids.ContainsKey(vm.Hwnd))
                .GroupBy(vm => pids[vm.Hwnd])
                .ToDictionary(g => g.Key, g => g.ToList());

            var usage = meter.Measure(collect(byRoot.Keys.ToList()), clock());
            foreach (var (root, cards) in byRoot)
                foreach (var vm in cards)
                    vm.SetUsage(usage.GetValueOrDefault(root));
        });

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
            Guard("card update", () => vm.Update(card, WindowTitle.Parse(windows.GetTitle(card.Hwnd)).Name, now));

            var at = Cards.IndexOf(vm);
            if (at < 0) Cards.Insert(i, vm);
            else if (at != i) Cards.Move(at, i);
        }

        foreach (var gone in existing.Values) Cards.Remove(gone);

        var live = ordered.Select(c => c.Hwnd).ToHashSet();
        foreach (var hwnd in pids.Keys.Where(h => !live.Contains(h)).ToList()) pids.Remove(hwnd);
        workingFromTitle.RemoveWhere(h => !live.Contains(h));
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
