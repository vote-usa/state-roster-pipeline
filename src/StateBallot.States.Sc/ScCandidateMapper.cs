using StateBallot.Core;

namespace StateBallot.States.Sc;

/// <summary>Projects the SC Votes candidate search rows onto the canonical Core shapes.</summary>
public static class ScCandidateMapper
{
    public static Election ToElection(string type, DateOnly date, string sourceElectionId, string sourceUrl) => new()
    {
        State = "SC",
        ElectionId = $"{date.Year}-{type.ToLowerInvariant()}",
        Name = $"{date.Year} {type} Election",
        ElectionDate = date,
        ElectionType = type,
        Jurisdiction = "state",
        SourceUrl = sourceUrl,
    };

    /// <param name="row">One HTML-table row, keyed by SC's column headers (see HtmlTableParser).</param>
    /// <param name="fieldMap">data/input/sc/candidate_field_map.json - canonical field -> SC's own column
    /// name, for the fields that are a plain single-column passthrough (see CandidateFieldMapper).</param>
    public static CandidateRow ToCandidateRow(
        Dictionary<string, string> row, Election election, string sourceUrl, Dictionary<string, string> fieldMap)
    {
        var rawOffice = row.GetValueOrDefault("Office", "").Trim();
        var (office, district) = SplitDistrict(rawOffice);

        return new CandidateRow
        {
            State = "SC",
            ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd"),
            ElectionType = election.ElectionType,
            Office = office,
            District = district,
            County = JoinCounties(row.GetValueOrDefault("Associated Counties", "")),
            CandidateName = row.GetValueOrDefault("Name on Ballot", "").Trim(),
            Party = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Party)),
            Incumbent = null, // not published
            SourceUrl = sourceUrl,
            SourceCandidateId = null, // no source-provided id in this export
            // This search result carries no contact/filing-date/address info
            // at all - just office, county scope, name, party, and status -
            // the sparsest export onboarded so far, same tier as WY/CO's.
            FilingDate = null,
            Phone = null,
            CampaignPhone = null,
            Email = null,
            Website = null,
            MailingAddressLine = null,
            MailingCity = null,
            MailingState = null,
            MailingZip = null,
            ResidentialCity = null,
            ResidentialCounty = null,
            Status = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Status)), // raw "Active"/"Withdrew Before Primary"/"Defeated In Primary"/etc.
        };
    }

    /// <summary>"{office}, District {n}" -> (office, n); anything else kept verbatim with a null district.</summary>
    private static (string Office, string? District) SplitDistrict(string rawOffice)
    {
        var match = ScSelectors.DistrictSuffix.Match(rawOffice);
        return match.Success
            ? (match.Groups["office"].Value.Trim(), match.Groups["n"].Value)
            : (rawOffice, null);
    }

    /// <summary>
    /// The source's own "Associated Counties" cell is already a clean,
    /// comma-joined list for a multi-county race (e.g. a judicial circuit) -
    /// re-joined with "; " to match this project's own multi-county
    /// convention rather than left in the source's own delimiter.
    /// </summary>
    private static string? JoinCounties(string rawAssociatedCounties)
    {
        var trimmed = rawAssociatedCounties.Trim();
        if (trimmed.Length == 0)
            return null;
        var counties = trimmed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return string.Join("; ", counties);
    }
}
