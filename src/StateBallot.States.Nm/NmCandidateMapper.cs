using StateBallot.Core;

namespace StateBallot.States.Nm;

/// <summary>Projects the NM candidate portal's export rows onto the canonical Core shapes.</summary>
public static class NmCandidateMapper
{
    public static Election ToElection(string type, DateOnly date, string sourceUrl) => new()
    {
        State = "NM",
        ElectionId = $"{date.Year}-{type.ToLowerInvariant()}",
        Name = $"{date.Year} {type} Election",
        ElectionDate = date,
        ElectionType = type,
        Jurisdiction = "state",
        SourceUrl = sourceUrl,
    };

    /// <summary>
    /// A "Judicial Retention" row (see NmRetentionMapper) is a yes/no vote on
    /// one incumbent, not a multi-candidate race - the caller checks this
    /// before deciding whether to map a row here or there.
    /// </summary>
    public static bool IsJudicialRetention(Dictionary<string, string> row) =>
        NmSelectors.JudicialRetentionPrefix.IsMatch(row.GetValueOrDefault("Contest", ""));

    /// <param name="row">One export row, keyed by NM's column headers (see HtmlTableParser).</param>
    /// <param name="fieldMap">data/input/nm/candidate_field_map.json - canonical field -> NM's own column
    /// name, for the fields that are a plain single-column passthrough (see CandidateFieldMapper).</param>
    public static CandidateRow ToCandidateRow(
        Dictionary<string, string> row, Election election, string sourceUrl, Dictionary<string, string> fieldMap)
    {
        var office = row.GetValueOrDefault("Contest", "").Trim();
        var rawDistrict = row.GetValueOrDefault("District", "").Trim();
        var filingCounty = row.GetValueOrDefault("Filing County", "").Trim();

        return new CandidateRow
        {
            State = "NM",
            ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd"),
            ElectionType = election.ElectionType,
            Office = office,
            District = NormalizeDistrict(rawDistrict),
            // Filing County is the office's own real jurisdiction only for a
            // fixed set of whole/sub-divided-county offices (see
            // NmSelectors.CountyScopedOffices) - for everything else (a
            // legislative/congressional district, a multi-county judicial
            // district, a municipal court) it's merely where this candidate
            // personally filed, not the race's true scope.
            County = NmSelectors.CountyScopedOffices.Contains(office) && filingCounty.Length > 0
                ? filingCounty
                : null,
            CandidateName = JoinName(row),
            Party = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Party)),
            Incumbent = null, // not published
            SourceUrl = sourceUrl,
            SourceCandidateId = null, // no source-provided id in this export
            FilingDate = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.FilingDate)), // raw "M/d/yyyy h:mm:ss tt", not reformatted
            Phone = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Phone)),
            CampaignPhone = null, // export has only one generic phone column, not a labeled "campaign" one
            Email = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Email)),
            Website = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Website)),
            // "Address"/"City"/"State"/"Zip" are already separate, clean
            // columns - the export's own "Mailing Address" column is a
            // redundant composite of these same values (street then
            // city/state/zip joined with a literal "<br />"), not used here.
            MailingAddressLine = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.MailingAddressLine)),
            MailingCity = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.MailingCity)),
            MailingState = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.MailingState)),
            MailingZip = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.MailingZip)),
            ResidentialCity = null, // not published (only a mailing/campaign address)
            ResidentialCounty = null,
            Status = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Status)), // raw "Qualified"/"Withdrawn"/"Disqualified"
        };
    }

    /// <summary>First + Middle + Last, skipping any blank part - also correctly renders NM's own joint Governor/Lt.
    /// Governor ticket rows, where the export itself splits "GREGGORY D HULL" / "AND" / "DAVID M GALLEGOS" across these
    /// same three columns rather than using a distinct joint-ticket shape.</summary>
    internal static string JoinName(Dictionary<string, string> row)
    {
        var parts = new[] { "First Name", "Middle Name", "Last Name" }
            .Select(col => row.GetValueOrDefault(col, "").Trim())
            .Where(part => part.Length > 0);
        return string.Join(' ', parts);
    }

    /// <summary>"DISTRICT 33" / "COUNTY COMMISSION DISTRICT 1" -> bare number; anything else (a division, a judicial district, a municipal court) kept verbatim; blank -> null.</summary>
    private static string? NormalizeDistrict(string rawDistrict)
    {
        if (rawDistrict.Length == 0)
            return null;
        var match = NmSelectors.NumberedDistrict.Match(rawDistrict);
        return match.Success ? match.Groups["n"].Value : rawDistrict;
    }
}
