using StateBallot.Core;

namespace StateBallot.States.Wy;

/// <summary>
/// Static "re-run next year" recommendation, same shape as WV/MD/NC's -
/// nothing in this pipeline yet models WY's specific electoral calendar.
/// </summary>
public sealed class WyPublishSchedule : IPublishSchedule
{
    public NextRunInfo Recommend(CollectResult result, int year) => new()
    {
        RecommendedAfter = $"{year + 1}-01-01",
        Reason = $"All {year} candidates collected. Re-run next year to check for new filings.",
    };
}
