using StateBallot.Core.Output;

namespace StateBallot.Staging;

/// <summary>
/// Assigns collector rows to the election they belong to. Runs are per election, but
/// candidate and measure rows carry only an election date and type, so the election has
/// to be identified from those. A row that cannot be placed is reported, never dropped.
/// </summary>
public static class ElectionMatcher
{
    public const string NoMatchingElection = "no_matching_election";
    public const string AmbiguousElection = "ambiguous_election";

    /// <summary>
    /// The index of the one election a row belongs to, or null plus a reason. A unique type
    /// match wins. Failing that, a single election on the date wins. Several elections share
    /// a date in some states (WA ran a Special and a Conservation election on 2025-02-11),
    /// so a date alone is not always enough.
    /// </summary>
    public static (int? Index, string? Reason) Find(
        IReadOnlyList<ElectionOut> elections, string? electionDate, string? electionType)
    {
        if (string.IsNullOrWhiteSpace(electionDate))
            return (null, NoMatchingElection);

        var onDate = new List<int>();
        for (var i = 0; i < elections.Count; i++)
        {
            if (string.Equals(elections[i].ElectionDate, electionDate, StringComparison.Ordinal))
                onDate.Add(i);
        }

        if (onDate.Count == 0)
            return (null, NoMatchingElection);
        if (onDate.Count == 1)
            return (onDate[0], null);

        if (!string.IsNullOrWhiteSpace(electionType))
        {
            var byType = onDate
                .Where(i => string.Equals(elections[i].ElectionType, electionType, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (byType.Count == 1)
                return (byType[0], null);
        }

        return (null, AmbiguousElection);
    }

    /// <summary>One row that could not be placed, with the reason.</summary>
    public sealed record Unassigned<T>(T Row, string Reason);

    /// <summary>Rows grouped by election index, plus the ones that could not be placed.</summary>
    public sealed record Grouping<T>(
        IReadOnlyDictionary<int, List<T>> ByElection,
        IReadOnlyList<Unassigned<T>> Unassigned);

    public static Grouping<T> Group<T>(
        IReadOnlyList<ElectionOut> elections,
        IEnumerable<T> rows,
        Func<T, (string? Date, string? Type)> key)
    {
        var byElection = new Dictionary<int, List<T>>();
        var unassigned = new List<Unassigned<T>>();

        foreach (var row in rows)
        {
            var (date, type) = key(row);
            var (index, reason) = Find(elections, date, type);
            if (index is { } i)
            {
                if (!byElection.TryGetValue(i, out var list))
                    byElection[i] = list = new List<T>();
                list.Add(row);
            }
            else
            {
                unassigned.Add(new Unassigned<T>(row, reason!));
            }
        }

        return new Grouping<T>(byElection, unassigned);
    }
}
