using StateBallot.Core;

namespace StateBallot.States.Vt;

/// <summary>Projects the VT SOS candidate XLSX rows onto the canonical Core shapes.</summary>
public static class VtCandidateMapper
{
    /// <param name="type">"Primary" or "General".</param>
    public static Election ToElection(string type, DateOnly date, string sourceUrl) => new()
    {
        State = "VT",
        ElectionId = $"{date.Year}-{type.ToLowerInvariant()}",
        Name = $"{date.Year} {type} Election",
        ElectionDate = date,
        ElectionType = type,
        Jurisdiction = "state",
        SourceUrl = sourceUrl,
    };

    /// <param name="row">One XLSX row, keyed by VT's column headers (see XlsxTableParser).</param>
    /// <param name="fieldMap">data/input/vt/candidate_field_map.json - canonical field -> VT's own column
    /// name, for the fields that are a plain single-column passthrough (see CandidateFieldMapper).</param>
    public static CandidateRow ToCandidateRow(
        Dictionary<string, string> row, Election election, string sourceUrl, Dictionary<string, string> fieldMap)
    {
        var office = row.GetValueOrDefault("Contest", "").Trim();
        var rawDistrict = row.GetValueOrDefault("District Name", "").Trim();
        var (district, county) = SplitDistrict(office, rawDistrict);

        return new CandidateRow
        {
            State = "VT",
            ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd"),
            ElectionType = election.ElectionType,
            Office = office,
            District = district,
            County = county,
            CandidateName = row.GetValueOrDefault("Name On Ballot", "").Trim(),
            Party = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Party)),
            Incumbent = null, // not published
            SourceUrl = sourceUrl,
            SourceCandidateId = null, // no source-provided id in this export
            FilingDate = null, // not published (only a per-candidate financial-disclosure filename, out of CandidateRow's scope)
            // "Day Time Phone" and "Evening Phone" are two separate personal
            // contact numbers, neither labeled as a campaign line the way WY's
            // "Campaign Telephone" is - coalesced into the one generic Phone
            // slot (day first) rather than dropping Evening Phone entirely or
            // mislabeling it CampaignPhone.
            Phone = CoalescePhone(row),
            CampaignPhone = null,
            Email = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Email)),
            Website = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Website)),
            MailingAddressLine = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.MailingAddressLine)),
            MailingCity = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.MailingCity)),
            MailingState = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.MailingState)),
            MailingZip = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.MailingZip)),
            ResidentialCity = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.ResidentialCity)), // "Town Of Residence"
            ResidentialCounty = null, // not published (only the town, no county lookup table for this)
            Status = null, // not published
        };
    }

    private static string? CoalescePhone(Dictionary<string, string> row)
    {
        var day = row.GetValueOrDefault("Day Time Phone", "").Trim();
        if (day.Length > 0)
            return day;
        var evening = row.GetValueOrDefault("Evening Phone", "").Trim();
        return evening.Length > 0 ? evening : null;
    }

    /// <summary>
    /// "District Name" is "N/A" for statewide/at-large offices, a compound
    /// county-abbreviation + number code for State Senator/Representative
    /// (e.g. "ADD 1", "CHI CT 1", "BEN RUT" - kept verbatim, not decomposed;
    /// see OcdDivisionId.HasDistrict in Core for why treating the trailing
    /// number as a plain district id would be wrong), a real county name for
    /// the county-elected row offices (VtSelectors.CountyLevelOffices), or a
    /// town name for Justice of the Peace (kept in District verbatim -
    /// CandidateRow has no town-level field, and County specifically means
    /// county).
    /// </summary>
    private static (string? District, string? County) SplitDistrict(string office, string rawDistrict)
    {
        if (rawDistrict.Length == 0 || VtSelectors.IsNoDistrictMarker(rawDistrict))
            return (null, null);

        if (VtSelectors.CountyLevelOffices.Contains(office))
            return (null, rawDistrict);

        return (rawDistrict, null);
    }
}
