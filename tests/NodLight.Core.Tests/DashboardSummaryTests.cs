using NodLight.Core;

namespace NodLight.Core.Tests;

public class DashboardSummaryTests
{
    [Fact]
    public void Empty_deck_is_all_zero() =>
        Assert.Equal(new DashboardSummary(0, 0, 0, 0), DashboardSummary.From([]));

    [Fact]
    public void Counts_sessions_per_status()
    {
        var s = DashboardSummary.From(
        [
            SessionStatus.Waiting, SessionStatus.Working, SessionStatus.Idle, SessionStatus.Idle, SessionStatus.Waiting,
        ]);

        Assert.Equal(new DashboardSummary(5, 2, 1, 2), s);
    }
}
