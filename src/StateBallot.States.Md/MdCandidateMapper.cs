using StateBallot.Core;

namespace StateBallot.States.Md;

/// <summary>Projects the MD SBE statewide candidate CSV rows onto the canonical Core shapes.</summary>
public static class MdCandidateMapper
{
    /// <param name="kind">"Primary" or "General", from the scraped &lt;dt&gt; label.</param>
    public static Election ToElection(int year, string kind, DateOnly date, string sourceUrl) => new()
    {
        State = "MD",
        ElectionId = $"{year}-{kind.ToLowerInvariant()}",
        Name = $"{year} {kind} Election",
        ElectionDate = date,
        ElectionType = kind,
        Jurisdiction = "state",
        SourceUrl = sourceUrl,
    };

    /// <param name="row">One CSV row, keyed by MD's column headers (see DelimitedTableParser).</param>
    public static CandidateRow ToCandidateRow(Dictionary<string, string> row, Election election, string sourceUrl)
    {
        var first = row.GetValueOrDefault("Candidate First Name and Middle Name", "").Trim();
        var last = row.GetValueOrDefault("Candidate Ballot Last Name and Suffix", "").Trim();
        var contest = row.GetValueOrDefault("Contest Run By District Name and Number", "").Trim();
        var cszMatch = MdSelectors.MailingCityStateZip.Match(
            row.GetValueOrDefault("Campaign Mailing City State and Zip", ""));

        return new CandidateRow
        {
            State = "MD",
            ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd"),
            ElectionType = election.ElectionType,
            Office = row.GetValueOrDefault("Office Name", "").Trim(),
            District = DistrictFrom(contest),
            County = null, // statewide list only; MD's separate "all_counties" export isn't covered yet
            CandidateName = $"{first} {last}".Trim(),
            Party = NullIfEmpty(row.GetValueOrDefault("Office Political Party")),
            Incumbent = null, // not published in this export
            SourceUrl = sourceUrl,
            SourceCandidateId = null, // no source-provided id in this export
            FilingDate = NullIfEmpty(row.GetValueOrDefault("Filing Type and Date")), // raw "Type - MM/DD/YYYY", not split
            Email = NullIfEmpty(row.GetValueOrDefault("Email")),
            Phone = NullIfEmpty(row.GetValueOrDefault("Public Phone")),
            Website = NullIfEmpty(row.GetValueOrDefault("Website")),
            MailingAddressLine = NullIfEmpty(row.GetValueOrDefault("Campaign Mailing Address")),
            MailingCity = cszMatch.Success ? cszMatch.Groups["city"].Value.Trim() : null,
            MailingState = cszMatch.Success ? cszMatch.Groups["state"].Value : null,
            MailingZip = cszMatch.Success ? cszMatch.Groups["zip"].Value : null,
            ResidentialCounty = StripCountySuffix(row.GetValueOrDefault("Candidate Residential Jurisdiction")),
            Status = NullIfEmpty(row.GetValueOrDefault("Candidate Status")),
        };
    }

    /// <summary>"State Of Maryland" means the race has no district (statewide); everything else is the district/circuit name as-is.</summary>
    private static string? DistrictFrom(string contest) =>
        string.IsNullOrWhiteSpace(contest) || string.Equals(contest, "State Of Maryland", StringComparison.OrdinalIgnoreCase)
            ? null
            : contest;

    private static string? StripCountySuffix(string? jurisdiction)
    {
        if (string.IsNullOrWhiteSpace(jurisdiction))
            return null;
        var trimmed = jurisdiction.Trim();
        return trimmed.EndsWith(" County", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^" County".Length]
            : trimmed;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
