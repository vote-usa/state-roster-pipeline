using StateBallot.Core;

namespace StateBallot.States.Ms;

/// <summary>
/// Static "re-run next year" recommendation, same shape as WV/MD/NC/WY/HI's -
/// nothing in this pipeline yet models MS's specific electoral calendar. Worth
/// revisiting: the source itself is a live "currently qualified" snapshot (see
/// MsCandidateMapper.DetermineElection), so re-running more often around a
/// qualifying-period close or primary date would likely surface changes sooner
/// than a fixed yearly cadence.
/// </summary>
public sealed class MsPublishSchedule : IPublishSchedule
{
    public NextRunInfo Recommend(CollectResult result, int year) => new()
    {
        RecommendedAfter = $"{year + 1}-01-01",
        Reason = $"All {year} candidates collected. Re-run next year to check for new filings.",
    };
}
