using System.Windows.Controls;
using NodLight.Core;

namespace NodLight.App;

/// <summary>The taskbar thumbnail: session count and a count per status. Rendered off-screen to a bitmap, never shown in a window.</summary>
public partial class DashboardView : UserControl
{
    public const double DesignWidth = 200;
    public const double DesignHeight = 125;

    public DashboardView(DashboardSummary summary)
    {
        InitializeComponent();
        DataContext = new DashboardViewModel(summary);
    }
}

/// <summary>The summary formatted for display.</summary>
public sealed class DashboardViewModel(DashboardSummary s)
{
    public int Sessions => s.Sessions;
    public string SessionsLabel => s.Sessions == 1 ? "session" : "sessions";
    public int Waiting => s.Waiting;
    public int Working => s.Working;
    public int Idle => s.Idle;
}
