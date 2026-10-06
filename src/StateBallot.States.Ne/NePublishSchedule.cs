using StateBallot.Core;

namespace StateBallot.States.Ne;

/// <summary>
/// Static "re-run next year" recommendation, same shape as WV/MD/NC/WY/HI/MS's -
/// nothing in this pipeline yet models NE's specific electoral calendar.
/// </summary>
public sealed class NePublishSchedule : IPublishSchedule
{
    public NextRunInfo Recommend(CollectResult result, int year) => new()
    {
        RecommendedAfter = $"{year + 1}-01-01",
        Reason = $"All {year} candidates collected. Re-run next year to check for new filings.",
    };
}
