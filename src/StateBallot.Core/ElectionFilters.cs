namespace StateBallot.Core;

/// <summary>Filters elections (and dates) to the collector's target year / "upcoming" window.</summary>
public static class ElectionFilters
{
    /// <summary>
    /// Elections in <paramref name="year"/>. When <paramref name="year"/> is the
    /// current calendar year, only dates on or after <paramref name="today"/>;
    /// otherwise the whole year (back-fill).
    /// </summary>
    public static List<Election> ForTargetYear(
        IEnumerable<Election> elections, int year, DateOnly? today = null)
    {
        var asOf = today ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        return elections
            .Where(e => InTargetYear(e.ElectionDate, year, asOf))
            .OrderBy(e => e.ElectionDate)
            .ThenBy(e => e.ElectionId, StringComparer.Ordinal)
            .ToList();
    }

    public static bool InTargetYear(DateOnly date, int year, DateOnly? today = null)
    {
        var asOf = today ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        if (date.Year != year)
            return false;
        return year != asOf.Year || date >= asOf;
    }
}
