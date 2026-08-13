using StateBallot.Core;

namespace StateBallot.States.Wa;

/// <summary>
/// Washington certifies primary results and statewide measures roughly 17 days
/// after election day (RCW 29A.60.190/240); the VoteWA general guide fills in after.
/// </summary>
public sealed class WaPublishSchedule : IPublishSchedule
{
    public NextRunInfo Recommend(CollectResult result, int year)
    {
        if (result.PendingElections.Count > 0)
        {
            var earliestPending = result.PendingElections.MinBy(e => e.ElectionDate)!;
            var lastPublished = result.Elections
                .Where(e => result.PendingElections.All(p => p.ElectionId != e.ElectionId))
                .Where(e => e.ElectionDate < earliestPending.ElectionDate)
                .MaxBy(e => e.ElectionDate);

            var recommendedAfter = lastPublished is not null
                ? lastPublished.ElectionDate.AddDays(17)
                : earliestPending.ElectionDate.AddDays(-45);

            return new NextRunInfo
            {
                RecommendedAfter = recommendedAfter.ToString("yyyy-MM-dd"),
                Reason = lastPublished is not null
                    ? $"{earliestPending.Name} ({earliestPending.ElectionDate:yyyy-MM-dd}) ballot data is not published yet. " +
                      $"Washington certifies the {lastPublished.Name} results and statewide measures about 17 days after election day, " +
                      "after which the VoteWA general-election guide and certified candidate list are populated."
                    : $"{earliestPending.Name} ballot data is typically published about 45 days before election day.",
                NextElectionDate = earliestPending.ElectionDate.ToString("yyyy-MM-dd"),
                NextElectionType = earliestPending.ElectionType,
            };
        }

        var nextYear = year + 1;

        if (result.Elections.Count == 0)
        {
            return new NextRunInfo
            {
                RecommendedAfter = $"{nextYear}-05-20",
                Reason = $"All elections listed for {year} have already passed; nothing was collected this run. " +
                         $"Washington's {nextYear} candidate filing week ends in mid-May; the VoteWA candidate list " +
                         "is populated within days of filing week closing.",
            };
        }

        return new NextRunInfo
        {
            RecommendedAfter = $"{nextYear}-05-20",
            Reason =
                $"All {year} elections collected. Washington's {nextYear} candidate filing week ends in mid-May; " +
                "the VoteWA candidate list is populated within days of filing week closing.",
        };
    }
}
