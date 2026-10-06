using StateBallot.Core;

namespace StateBallot.States.Sc;

/// <summary>
/// Static "re-run next year" recommendation, same shape as every other
/// onboarded state's - nothing in this pipeline yet models SC's specific
/// electoral calendar.
/// </summary>
public sealed class ScPublishSchedule : IPublishSchedule
{
    public NextRunInfo Recommend(CollectResult result, int year) => new()
    {
        RecommendedAfter = $"{year + 1}-01-01",
        Reason = $"All {year} candidates collected. Re-run next year to check for new filings.",
    };
}
