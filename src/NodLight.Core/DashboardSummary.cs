namespace NodLight.Core;

/// <summary>How many sessions there are and how many are in each status, for the taskbar thumbnail.</summary>
public sealed record DashboardSummary(int Sessions, int Waiting, int Working, int Idle)
{
    public static DashboardSummary From(IEnumerable<SessionStatus> statuses)
    {
        var list = statuses.ToList();
        return new DashboardSummary(
            list.Count,
            list.Count(s => s == SessionStatus.Waiting),
            list.Count(s => s == SessionStatus.Working),
            list.Count(s => s == SessionStatus.Idle));
    }
}
