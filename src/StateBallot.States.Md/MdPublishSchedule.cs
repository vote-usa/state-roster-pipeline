using StateBallot.Core;

namespace StateBallot.States.Md;

/// <summary>
/// Static "re-run next year" recommendation, same shape as WV's. MD's real
/// cycle is less regular than an annual filing window (statewide races are
/// mostly gubernatorial-cycle, off-years may have only municipal/special
/// elections or none at all), but nothing in this pipeline yet models MD's
/// specific electoral calendar, so a conservative "check again next year"
/// default is used rather than inventing cycle-specific logic.
/// </summary>
public sealed class MdPublishSchedule : IPublishSchedule
{
    public NextRunInfo Recommend(CollectResult result, int year) => new()
    {
        RecommendedAfter = $"{year + 1}-01-01",
        Reason = $"All {year} statewide candidates collected. Re-run next year to check for new filings.",
    };
}
