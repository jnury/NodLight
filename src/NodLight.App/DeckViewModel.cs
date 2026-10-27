using System.Collections.ObjectModel;
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

    public ObservableCollection<CardViewModel> Cards { get; } = [];

    public DeckViewModel(SessionStore store, WindowService windows, Func<DateTimeOffset> clock)
    {
        this.store = store;
        this.windows = windows;
        this.clock = clock;
        store.Changed += Sync;
    }

    /// <summary>A hook event, already marshalled to the UI thread. Dropped when its terminal has no window.</summary>
    public void OnHookEvent(HookEvent e)
    {
        if (windows.FindMainWindow(e.AlacrittyPid) is { } hwnd) store.Apply(e, hwnd);
    }

    /// <summary>Unknown windows (anything without a card) are ignored by the store.</summary>
    public void OnForeground(long hwnd) => store.Foreground(hwnd, clock());

    public void Activate(CardViewModel card) => windows.Focus(card.Hwnd);

    /// <summary>Once a second: drop closed windows, check transcripts for interrupts, refresh titles and times.</summary>
    public void Tick()
    {
        foreach (var card in store.Cards)
            if (!windows.IsAlive(card.Hwnd)) store.WindowClosed(card.Hwnd);

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
        {
            if (!tails.TryGetValue(group.Key, out var tail))
                tails[group.Key] = tail = new TranscriptTail(group.Key);

            var lines = tail.ReadNewLines();
            if (lines.Count == 0) continue;
            foreach (var card in group)
                if (lines.Any(line => InterruptDetector.IsInterrupt(card.Agent, line)))
                    store.Interrupted(card.Hwnd, clock());
        }
    }

    // Reuses view models by hwnd so the list does not flicker, and moves them into the store's order.
    void Sync()
    {
        var now = clock();
        var ordered = store.Cards;
        var existing = Cards.ToDictionary(vm => vm.Hwnd);

        for (var i = 0; i < ordered.Count; i++)
        {
            var card = ordered[i];
            if (!existing.Remove(card.Hwnd, out var vm)) vm = new CardViewModel(card.Hwnd);
            vm.Update(card, windows.GetTitle(card.Hwnd), now);

            var at = Cards.IndexOf(vm);
            if (at < 0) Cards.Insert(i, vm);
            else if (at != i) Cards.Move(at, i);
        }

        foreach (var gone in existing.Values) Cards.Remove(gone);
    }
}
