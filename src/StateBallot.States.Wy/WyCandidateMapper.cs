using StateBallot.Core;

namespace StateBallot.States.Wy;

/// <summary>Projects the WY SOS candidate CSV rows onto the canonical Core shapes.</summary>
public static class WyCandidateMapper
{
    /// <param name="type">"Primary" or "General", from the JSON-LD event name.</param>
    public static Election ToElection(string type, DateOnly date, string sourceUrl) => new()
    {
        State = "WY",
        ElectionId = $"{date.Year}-{type.ToLowerInvariant()}",
        Name = $"{date.Year} {type} Election",
        ElectionDate = date,
        ElectionType = type,
        Jurisdiction = "state",
        SourceUrl = sourceUrl,
    };

    /// <param name="row">One CSV row, keyed by WY's column headers (see DelimitedTableParser).</param>
    /// <param name="fieldMap">data/input/wy/candidate_field_map.json - canonical field -> WY's own column
    /// name, for the fields that are a plain single-column passthrough (see CandidateFieldMapper).</param>
    public static CandidateRow ToCandidateRow(
        Dictionary<string, string> row, Election election, string sourceUrl, Dictionary<string, string> fieldMap)
    {
        var (office, district) = SplitOfficeSought(row.GetValueOrDefault("Office Sought", "").Trim());
        var cszMatch = WySelectors.MailingCityStateZip.Match(row.GetValueOrDefault("Mailing City State & Zip", ""));
        var withdrawn = NullIfEmpty(row.GetValueOrDefault("Date Withdrawn"));

        return new CandidateRow
        {
            State = "WY",
            ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd"),
            ElectionType = election.ElectionType,
            Office = office,
            District = district,
            County = null, // WY's export has no county concept
            CandidateName = row.GetValueOrDefault("Ballot Name", "").Trim(),
            Party = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Party)), // raw code (REP/DEM/LBR/CT/IND)
            Incumbent = null, // not published
            SourceUrl = sourceUrl,
            SourceCandidateId = null, // no source-provided id in this export
            FilingDate = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.FilingDate)), // raw M/d/yyyy or MM/dd/yyyy, not reformatted
            Email = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Email)),
            // The CSV's own column is explicitly labeled "Campaign Telephone", not a generic
            // contact number, so it maps to CampaignPhone rather than the generic Phone slot.
            CampaignPhone = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.CampaignPhone)),
            Website = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Website)),
            MailingAddressLine = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.MailingAddressLine)),
            MailingCity = cszMatch.Success ? cszMatch.Groups["city"].Value.Trim() : null,
            MailingState = cszMatch.Success ? cszMatch.Groups["state"].Value : null,
            MailingZip = cszMatch.Success ? cszMatch.Groups["zip"].Value : null,
            // WY has no single "status" column, only a separate withdrawal date - fold the two
            // real signals into one Status string in the same "Category - date" shape MD's own
            // source already uses, rather than inventing a new per-state convention. Not a plain
            // column lookup, so it can't be a field-map entry.
            Status = withdrawn is null ? null : $"Withdrawn - {withdrawn}",
        };
    }

    private static (string Office, string? District) SplitOfficeSought(string raw)
    {
        var stripped = WySelectors.PartySuffix.Match(raw) is { Success: true } partyMatch
            ? partyMatch.Groups["office"].Value.Trim()
            : raw;

        if (WySelectors.JudicialCode.Match(stripped) is { Success: true } judicialMatch)
            return (judicialMatch.Groups["office"].Value.Trim(), judicialMatch.Groups["code"].Value.Trim());

        if (WySelectors.TrailingDistrictNumber.Match(stripped) is { Success: true } districtMatch)
            return (districtMatch.Groups["office"].Value.Trim(), districtMatch.Groups["district"].Value.Trim());

        return (stripped, null);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
