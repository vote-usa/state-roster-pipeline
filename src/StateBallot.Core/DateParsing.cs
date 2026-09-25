using System.Globalization;

namespace StateBallot.Core;

/// <summary>Shared multi-format date parsing for state collectors.</summary>
public static class DateParsing
{
    /// <summary>
    /// Tries each format in <paramref name="formats"/> in order (invariant culture,
    /// exact match) and returns the first successful parse. Callers own their own
    /// format list and their own behavior on failure (throw vs. skip) - this only
    /// dedupes the multi-format-tolerant parsing loop itself.
    /// </summary>
    public static bool TryParseAny(string? value, string[] formats, out DateOnly result)
    {
        result = default;
        return value is not null &&
               DateOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }
}
