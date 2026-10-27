using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using NodLight.Core;

namespace NodLight.App;

/// <summary>One card on the strip: a <see cref="Card"/> plus the live window title, formatted for display.</summary>
public sealed class CardViewModel(long hwnd) : INotifyPropertyChanged
{
    static readonly Brush WaitingBrush = Frozen(0xFA, 0xB3, 0x87);
    static readonly Brush WorkingBrush = Frozen(0xA6, 0xE3, 0xA1);
    static readonly Brush IdleBrush = Frozen(0x6C, 0x70, 0x86);
    static readonly Brush CardBackground = Frozen(0x23, 0x23, 0x34);
    static readonly Brush WaitingBackground = Frozen(0x3A, 0x2C, 0x22);
    static readonly Brush TitleText = Frozen(0xDD, 0xDD, 0xDD);
    static readonly Brush IdleTitleText = Frozen(0x99, 0x99, 0x99);

    public event PropertyChangedEventHandler? PropertyChanged;

    public long Hwnd { get; } = hwnd;

    public string Title { get; private set => Set(ref field, value); } = "";
    public string AgentBadge { get; private set => Set(ref field, value); } = "";
    public string StatusLine { get; private set => Set(ref field, value); } = "";

    /// <summary>What the session is asking for; null unless Waiting.</summary>
    public string? Detail { get; private set => Set(ref field, value); }

    /// <summary>Card tooltip: the full title, plus the detail on a second line when there is one.</summary>
    public string ToolTip { get; private set => Set(ref field, value); } = "";

    /// <summary>Status colour, used for the left border.</summary>
    public Brush Brush { get; private set => Set(ref field, value); } = IdleBrush;
    public Brush Background { get; private set => Set(ref field, value); } = CardBackground;
    public Brush TitleBrush { get; private set => Set(ref field, value); } = TitleText;

    public void Update(Card card, string title, DateTimeOffset now)
    {
        Title = title;
        AgentBadge = Badge(card.Agent);
        StatusLine = FormatStatusLine(card, now);
        Detail = card.Status == SessionStatus.Waiting && !string.IsNullOrWhiteSpace(card.Detail) ? card.Detail : null;
        ToolTip = Detail is null ? title : title + Environment.NewLine + Detail;
        Brush = card.Status switch
        {
            SessionStatus.Waiting => WaitingBrush,
            SessionStatus.Working => WorkingBrush,
            _ => IdleBrush,
        };
        Background = card.Status == SessionStatus.Waiting ? WaitingBackground : CardBackground;
        TitleBrush = card.Status == SessionStatus.Idle ? IdleTitleText : TitleText;
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

    static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    void Set<T>(ref T storage, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value)) return;
        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
