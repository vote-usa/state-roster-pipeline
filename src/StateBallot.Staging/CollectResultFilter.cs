using System.Globalization;
using StateBallot.Core;

namespace StateBallot.Staging;

/// <summary>Narrows a collector result to one election date.</summary>
public static class CollectResultFilter
{
    /// <summary>
    /// Keeps only the rows for <paramref name="electionDate"/>. County directory rows and
    /// undated statewide measures are state-level, not election-scoped, so they are kept.
    /// </summary>
    public static CollectResult ToElectionDate(CollectResult result, string electionDate)
    {
        var filtered = new CollectResult
        {
            CountyCodes = result.CountyCodes,
            Sources = result.Sources,
        };

        filtered.Elections.AddRange(result.Elections.Where(e => Iso(e.ElectionDate) == electionDate));
        filtered.PendingElections.AddRange(result.PendingElections.Where(e => Iso(e.ElectionDate) == electionDate));
        filtered.Candidates.AddRange(result.Candidates.Where(c => c.ElectionDate == electionDate));
        filtered.Measures.AddRange(result.Measures.Where(m => m.ElectionDate == electionDate));
        filtered.StatewideProposedMeasures.AddRange(
            result.StatewideProposedMeasures.Where(m => m.ElectionDate is null || m.ElectionDate == electionDate));
        filtered.CountyBallots.AddRange(result.CountyBallots.Where(b => b.ElectionDate == electionDate));
        filtered.CountyDirectory.AddRange(result.CountyDirectory);
        filtered.Gaps.AddRange(result.Gaps);

        return filtered;
    }

    /// <summary>Election dates present in a result, ascending.</summary>
    public static IReadOnlyList<string> ElectionDates(CollectResult result) =>
        result.Elections.Select(e => Iso(e.ElectionDate)).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToList();

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
