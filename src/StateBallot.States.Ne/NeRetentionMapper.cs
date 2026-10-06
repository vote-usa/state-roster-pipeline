using System.Text.RegularExpressions;
using StateBallot.Core;

namespace StateBallot.States.Ne;

/// <summary>
/// Projects the NE SOS statewide filing workbook's judicial-retention sheet
/// (sheet 1) rows onto MeasureRow. Retention questions ("Shall Judge X be
/// retained in office?") are a general-election-only ballot fixture in
/// Nebraska - judges are appointed, not primaried - so every row is stamped
/// with the General election regardless of the candidate-side Primary/General
/// split NeCandidateMapper has to work out (see NeCollector).
/// </summary>
public static class NeRetentionMapper
{
    private static readonly Regex SlugPattern = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    /// <param name="row">One workbook row, keyed by NE's column headers (see XlsxTableParser).</param>
    public static MeasureRow ToMeasureRow(Dictionary<string, string> row, Election generalElection, string sourceUrl)
    {
        var office = row.GetValueOrDefault("Office", "").Trim();
        var district = row.GetValueOrDefault("District (if applicable)", "").Trim();
        var judge = row.GetValueOrDefault("Judge (Ballot Name)", "").Trim();
        var question = row.GetValueOrDefault("Ballot Question", "").Trim();

        return new MeasureRow
        {
            State = "NE",
            ElectionDate = generalElection.ElectionDate.ToString("yyyy-MM-dd"),
            MeasureId = Slug($"retention-{office}-{district}-{judge}"),
            Title = question,
            Summary = null,
            FullTextUrl = null,
            // No county concept for a judicial district - fold office+district
            // into Jurisdiction instead, the one slot MeasureRow has for "where
            // this applies". "Statewide" (the workbook's own value for the
            // Workers' Compensation Court) collapses to just the office name.
            Jurisdiction = district.Length == 0 || string.Equals(district, "Statewide", StringComparison.OrdinalIgnoreCase)
                ? office
                : $"{office}, District {district}",
            County = null,
            SourceUrl = sourceUrl,
        };
    }

    private static string Slug(string value) =>
        SlugPattern.Replace(value.ToLowerInvariant(), "-").Trim('-');
}
