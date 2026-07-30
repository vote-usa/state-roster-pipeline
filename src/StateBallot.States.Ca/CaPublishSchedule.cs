using StateBallot.Core;

namespace StateBallot.States.Ca;

/// <summary>
/// California posts the certified list of candidates no later than the 68th day
/// before a statewide election (Elections Code s. 8148).
/// </summary>
public sealed class CaPublishSchedule : IPublishSchedule
{
    public NextRunInfo Recommend(CollectResult result, int year)
    {
        if (result.PendingElections.Count > 0)
        {
            var earliestPending = result.PendingElections.MinBy(e => e.ElectionDate)!;
            return new NextRunInfo
            {
                RecommendedAfter = earliestPending.ElectionDate.AddDays(-67).ToString("yyyy-MM-dd"),
                Reason =
                    $"{earliestPending.Name} candidate list is not posted yet. California's Secretary of State " +
                    $"certifies the list of candidates 68 days before election day (Elections Code s. 8148), " +
                    $"i.e. by {earliestPending.ElectionDate.AddDays(-68):yyyy-MM-dd} for this election.",
                NextElectionDate = earliestPending.ElectionDate.ToString("yyyy-MM-dd"),
                NextElectionType = earliestPending.ElectionType,
            };
        }

        var nextYear = year + 1;
        return new NextRunInfo
        {
            RecommendedAfter = $"{nextYear}-01-15",
            Reason =
                $"All {year} elections collected. Check early {nextYear} for newly proclaimed special " +
                "elections and the year's county-administered election calendar; statewide primary " +
                "candidate lists post 68 days before the primary.",
        };
    }
}
