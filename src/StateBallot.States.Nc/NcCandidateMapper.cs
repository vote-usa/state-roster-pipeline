using StateBallot.Core;

namespace StateBallot.States.Nc;

/// <summary>Projects the NC SBE candidate filing CSV rows onto the canonical Core shapes.</summary>
public static class NcCandidateMapper
{
    /// <param name="isLatest">True for the latest of the distinct election_dt values found in the
    /// file (always the general - NC's file has no separate "which election is this" flag, so the
    /// latest date is treated as the general and any earlier one as a primary).</param>
    public static Election ToElection(DateOnly date, bool isLatest, string sourceUrl)
    {
        var type = isLatest ? "General" : "Primary";
        return new Election
        {
            State = "NC",
            ElectionId = $"{date.Year}-{type.ToLowerInvariant()}",
            Name = $"{date.Year} {type} Election",
            ElectionDate = date,
            ElectionType = type,
            Jurisdiction = "state",
            SourceUrl = sourceUrl,
        };
    }

    /// <param name="row">One CSV row, keyed by NC's column headers (see DelimitedTableParser).</param>
    /// <param name="county">
    /// The county this row's ballot line is attributed to, or null to build the
    /// deduplicated statewide/multi-county row (see NcCollector - the same
    /// underlying row is mapped once per county for CountyBallots, and once
    /// county-less for the top-level Candidates list, when it spans &gt;1 county).
    /// </param>
    /// <param name="fieldMap">data/input/nc/candidate_field_map.json - canonical field -> NC's own column
    /// name, for the fields that are a plain single-column passthrough (see CandidateFieldMapper).</param>
    public static CandidateRow ToCandidateRow(
        Dictionary<string, string> row, Election election, string sourceUrl, string? county, Dictionary<string, string> fieldMap)
    {
        var (office, district) = SplitContest(row.GetValueOrDefault("contest_name", "").Trim());

        return new CandidateRow
        {
            State = "NC",
            ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd"),
            ElectionType = election.ElectionType,
            Office = office,
            District = district,
            County = county,
            CandidateName = row.GetValueOrDefault("name_on_ballot", "").Trim(),
            Party = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Party)), // raw 3-letter code (DEM/REP/GRE/LIB/UNA/...)
            Incumbent = null, // not published
            SourceUrl = sourceUrl,
            SourceCandidateId = null, // no source-provided id in this export
            FilingDate = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.FilingDate)), // raw MM/DD/YYYY, not reformatted
            Email = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Email)),
            // Three raw phone-like columns (phone/office_phone/business_phone) map onto CandidateRow's
            // one generic Phone slot - none of them is labeled as specifically "campaign" contact info,
            // so CampaignPhone is left null rather than guessing which raw column that would be. A
            // coalesce like this can't be expressed as a single field-map entry, so it stays hand-written.
            Phone = FirstNonEmpty(row.GetValueOrDefault("phone"), row.GetValueOrDefault("business_phone"), row.GetValueOrDefault("office_phone")),
            MailingAddressLine = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.MailingAddressLine)),
            MailingCity = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.MailingCity)),
            MailingState = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.MailingState)),
            MailingZip = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.MailingZip)),
        };
    }

    private static (string Office, string? District) SplitContest(string contest)
    {
        var match = NcSelectors.ContestWithDistrict.Match(contest);
        return match.Success
            ? (match.Groups["office"].Value.Trim(), match.Groups["district"].Value)
            : (contest, null);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.Select(NullIfEmpty).FirstOrDefault(v => v is not null);

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
