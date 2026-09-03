using StateBallot.Core;

namespace StateBallot.States.Co;

/// <summary>Projects the CO SOS candidate XLSX rows onto the canonical Core shapes.</summary>
public static class CoCandidateMapper
{
    /// <param name="type">"Primary" or "General".</param>
    public static Election ToElection(string type, DateOnly date, string sourceUrl) => new()
    {
        State = "CO",
        ElectionId = $"{date.Year}-{type.ToLowerInvariant()}",
        Name = $"{date.Year} {type} Election",
        ElectionDate = date,
        ElectionType = type,
        Jurisdiction = "state",
        SourceUrl = sourceUrl,
    };

    /// <param name="row">One XLSX row, keyed by CO's column headers (see XlsxTableParser).</param>
    /// <param name="fieldMap">data/input/co/candidate_field_map.json - canonical field -> CO's own column
    /// name, for the fields that are a plain single-column passthrough (see CandidateFieldMapper).</param>
    public static CandidateRow ToCandidateRow(
        Dictionary<string, string> row, Election election, string sourceUrl, Dictionary<string, string> fieldMap)
    {
        var office = row.GetValueOrDefault("Office", "").Trim();
        var rawDistrict = row.GetValueOrDefault("District", "").Trim();
        var (district, county) = SplitDistrict(office, rawDistrict);
        var isWriteIn = string.Equals(row.GetValueOrDefault("Write In?"), "Y", StringComparison.OrdinalIgnoreCase);

        return new CandidateRow
        {
            State = "CO",
            ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd"),
            ElectionType = election.ElectionType,
            Office = office,
            District = district,
            County = county,
            CandidateName = row.GetValueOrDefault("Candidate Name", "").Trim(),
            Party = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Party)),
            Incumbent = null, // not published
            SourceUrl = sourceUrl,
            // No source-provided id, filing date, address, or contact info in this
            // export - v1 scope is candidates only (name/office/district/party),
            // same scope CO's own SOS site publishes.
            SourceCandidateId = null,
            // The export's own "Write In?" column is the only other per-candidate
            // signal it carries; fold it into Status rather than inventing a new
            // CandidateRow field for one state (same idiom WY uses for its
            // withdrawal-date signal).
            Status = isWriteIn ? "Write-in" : null,
        };
    }

    /// <summary>
    /// The XLSX "District" column is overloaded: a bare number for
    /// congressional/legislative/judicial-district races, "State"/"Statewide"
    /// for statewide offices, or a county name for county-court races (the only
    /// offices whose district value CO's own export uses that way - see
    /// CoSelectors.CountyLevelOffices). Anything else (e.g. RTD's single-letter
    /// subdistrict codes) is left as a plain district value verbatim.
    /// </summary>
    private static (string? District, string? County) SplitDistrict(string office, string rawDistrict)
    {
        if (rawDistrict.Length == 0)
            return (null, null);

        if (CoSelectors.IsStatewideMarker(rawDistrict))
            return (null, null);

        if (CoSelectors.CountyLevelOffices.Contains(office))
            return (null, rawDistrict);

        return (rawDistrict, null);
    }
}
