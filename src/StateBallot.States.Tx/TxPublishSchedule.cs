using StateBallot.Core;

namespace StateBallot.States.Tx;

public sealed class TxPublishSchedule : IPublishSchedule
{
    public NextRunInfo Recommend(CollectResult result, int year)
    {
        if (result.PendingElections.Count > 0)
        {
            return new NextRunInfo
            {
                RecommendedAfter = DateTime.UtcNow.Date.AddDays(7).ToString("yyyy-MM-dd"),
                Reason = $"{result.PendingElections.Count} election(s) had no candidates yet as of this run; " +
                         "candidate filing periods are typically still open. Re-run in about a week.",
            };
        }

        if (result.Elections.Count == 0)
        {
            return new NextRunInfo
            {
                RecommendedAfter = $"{year + 1}-01-01",
                Reason = $"All elections listed for {year} have already passed; nothing was collected this run. " +
                         $"Re-run once {year + 1} elections are listed.",
            };
        }

        return new NextRunInfo
        {
            RecommendedAfter = $"{year + 1}-01-01",
            Reason = $"All {year} elections collected. Re-run once {year + 1} elections are listed.",
        };
    }
}
