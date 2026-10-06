using StateBallot.Core;

namespace StateBallot.States.Hi;

/// <summary>
/// Static "re-run next year" recommendation, same shape as WV/MD/NC/WY's -
/// nothing in this pipeline yet models HI's specific electoral calendar.
/// </summary>
public sealed class HiPublishSchedule : IPublishSchedule
{
    public NextRunInfo Recommend(CollectResult result, int year) => new()
    {
        RecommendedAfter = $"{year + 1}-01-01",
        Reason = $"All {year} candidates collected. Re-run next year to check for new filings.",
    };
}
