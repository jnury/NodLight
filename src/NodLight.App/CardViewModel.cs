using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using NodLight.Core;

namespace NodLight.App;

/// <summary>One card on the strip: a <see cref="Card"/> plus the live window title, formatted for display.</summary>
public sealed class CardViewModel(long hwnd) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public long Hwnd { get; } = hwnd;

    public string Title { get; private set => Set(ref field, value); } = "";
    public string AgentBadge { get; private set => Set(ref field, value); } = "";
    public string StatusLine { get; private set => Set(ref field, value); } = "";

    /// <summary>Card tooltip: the full title, for when it is cut off.</summary>
    public string ToolTip { get; private set => Set(ref field, value); } = "";

    /// <summary>The window's XAML colours the card from this (theme brushes are resolved there, not here).</summary>
    public SessionStatus Status { get; private set => Set(ref field, value); } = SessionStatus.Idle;

    /// <summary>True while the session is Waiting for the user; the card blinks.</summary>
    public bool NeedsAttention { get; private set => Set(ref field, value); }

    public string CpuText { get; private set => Set(ref field, value); } = "–";
    public string MemoryText { get; private set => Set(ref field, value); } = "–";
    public string IoText { get; private set => Set(ref field, value); } = "–";
    public bool CpuHigh { get; private set => Set(ref field, value); }
    public bool MemoryHigh { get; private set => Set(ref field, value); }
    public bool IoHigh { get; private set => Set(ref field, value); }

    /// <summary>Process count and the busiest processes, shown in the usage row's tooltip.</summary>
    public string UsageToolTip { get; private set => Set(ref field, value); } = "";

    public void SetUsage(ResourceUsage? usage)
    {
        if (usage is null) return;
        CpuText = ResourceFormat.Percent(usage.CpuPercent);
        MemoryText = ResourceFormat.Bytes(usage.MemoryBytes);
        IoText = ResourceFormat.Rate(usage.IoBytesPerSec);
        CpuHigh = usage.CpuHigh;
        MemoryHigh = usage.MemoryHigh;
        IoHigh = usage.IoHigh;
        UsageToolTip = $"{usage.ProcessCount} processes" + string.Concat(usage.Top.Select(p =>
            $"{Environment.NewLine}{p.Name}  {ResourceFormat.Percent(p.CpuPercent)}  {ResourceFormat.Bytes(p.MemoryBytes)}"));
    }

    public void Update(Card card, string title, DateTimeOffset now)
    {
        Title = title;
        AgentBadge = Badge(card.Agent);
        StatusLine = FormatStatusLine(card, now);
        ToolTip = title;
        Status = card.Status;
        NeedsAttention = card.Status == SessionStatus.Waiting;
    }

    public static string Badge(AgentKind agent) => agent == AgentKind.Claude ? "claude" : "codex";

    /// <summary>"Waiting · 4 min · webapp"; the project part is omitted when the cwd is unknown.</summary>
    public static string FormatStatusLine(Card card, DateTimeOffset now)
    {
        var line = $"{card.Status} · {TimeFormat.Short(now - card.Since)}";
        var project = ProjectName(card.Cwd);
        return project is null ? line : $"{line} · {project}";
    }

    /// <summary>Last path segment of the cwd ("C:\r\demo\" → "demo"), or null.</summary>
    public static string? ProjectName(string? cwd)
    {
        if (string.IsNullOrWhiteSpace(cwd)) return null;
        var name = Path.GetFileName(cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? cwd : name;
    }

    void Set<T>(ref T storage, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value)) return;
        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
