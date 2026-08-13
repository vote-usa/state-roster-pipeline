using StateBallot.Core;

namespace StateBallot.States.Wv;

public sealed class WvPublishSchedule : IPublishSchedule
{
    public NextRunInfo Recommend(CollectResult result, int year) => new()
    {
        RecommendedAfter = $"{year + 1}-01-01",
        Reason = $"All {year} candidates collected. Re-run once {year + 1} filings open.",
    };
}
