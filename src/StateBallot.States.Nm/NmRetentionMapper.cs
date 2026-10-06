using System.Text.RegularExpressions;
using StateBallot.Core;

namespace StateBallot.States.Nm;

/// <summary>
/// Projects the NM candidate portal's "Judicial Retention" rows onto
/// MeasureRow rather than CandidateRow - a yes/no vote on one incumbent
/// judge, not a multi-candidate race (no Party, no opponent), the same
/// modeling choice NE's own judicial-retention sheet uses (see
/// NeRetentionMapper). The bare Contest value "Judicial Retention" covers
/// NM's appellate justices (Supreme Court, Court of Appeals - the export
/// doesn't say which); a composite "Judicial Retention&lt;br /&gt;Judge of
/// the Metropolitan Court DIVISION 2" (already read as one space-joined
/// string by HtmlTableParser - see NmCollector) names the specific lower-court
/// seat being retained, since more than one can be on the ballot at once.
/// </summary>
public static class NmRetentionMapper
{
    private static readonly Regex SlugPattern = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    public static MeasureRow ToMeasureRow(Dictionary<string, string> row, Election election, string sourceUrl)
    {
        var seat = NmSelectors.JudicialRetentionPrefix.Replace(row.GetValueOrDefault("Contest", ""), "").Trim();
        var name = NmCandidateMapper.JoinName(row);
        var county = row.GetValueOrDefault("Filing County", "").Trim();

        return new MeasureRow
        {
            State = "NM",
            ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd"),
            MeasureId = Slug($"retention-{seat}-{name}"),
            Title = seat.Length == 0
                ? $"Shall {name} be retained in office?"
                : $"Shall {name} be retained in office as {seat}?",
            Summary = null,
            FullTextUrl = null,
            // No separate "seat" slot on MeasureRow - the specific court/
            // division is the only thing distinguishing one retention vote
            // from another, so it goes in Jurisdiction, same as NE's.
            Jurisdiction = seat.Length == 0 ? "Judicial Retention" : seat,
            // Filing County here is the same imprecise signal it is for
            // CandidateRow's own multi-county offices (District Court Judge,
            // Municipal Judge) - kept anyway since a retention vote's real
            // scope (which counties actually vote on it) isn't otherwise
            // derivable from this export, but not to be read as authoritative
            // for a judicial district that spans more than one county.
            County = county.Length == 0 ? null : county,
            SourceUrl = sourceUrl,
        };
    }

    private static string Slug(string value) =>
        SlugPattern.Replace(value.ToLowerInvariant(), "-").Trim('-');
}
